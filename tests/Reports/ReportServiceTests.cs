using WaterBilling.Components.Pdf;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Reports;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Reports;

public class ReportServiceTests : BillingFixture
{
    private static readonly DateOnly Day1 = new(2026, 11, 5);
    private static readonly DateOnly Day2 = new(2026, 11, 6);
    private static readonly ReportFilter Both = new(Day1, Day2);
    private long _kaveri, _kaveriVehicle;

    public ReportServiceTests() => Me.User = Db.AddUser(UserRole.Admin);

    // Two suppliers, two days, cash + credit + volume sales, a cancellation, receipts, a payment.
    private async Task TradeAsync()
    {
        _kaveri = (await Svc.Suppliers.CreateAsync(new SupplierForm { Name = "Kaveri Tankers", ShortName = "Kaveri", Phone = "9123456780", OpeningBalance = 1000 })).Value;
        _kaveriVehicle = (await Svc.Vehicles.CreateAsync(new VehicleForm { VehicleNo = "TN 01 AB 1234", SupplierId = _kaveri, TankCapacityLitres = 9000, Rate = 120 })).Value;

        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });   // 150 cash
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = VolumeVehicle, Rate = 12.35m, VolumeKl = 10 });                      // 123.50 credit
        var wrong = (await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = _kaveriVehicle, Rate = 120 })).Value;
        await Svc.Invoices.CancelAsync(wrong, "wrong vehicle", 1);
        Db.Clock.Advance(TimeSpan.FromDays(1));
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = _kaveriVehicle, Rate = 120, Trips = 2 });                           // 240 credit
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, BillToType = PartyType.Customer, CustomerId = Customer }); // 150 to customer
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = _kaveri, Amount = 100 });
        await Svc.Payments.CreateAsync(new PaymentForm { AccountId = 10, Amount = 50 });
    }

    private Task<ReportTable> Build(ReportKind kind, ReportFilter? f = null) => Svc.Reports.BuildAsync(kind, f ?? Both);

    private static long Paise(string formatted) => Money.FromRupees(decimal.Parse(formatted.Replace(",", "").Replace(" Dr", "").Replace(" Cr", "")));

    [Fact]
    public async Task FR_RPT_07_Sales_Report_Totals_Equal_Water_Sales_Ledger()
    {
        await TradeAsync();

        var daily = await Build(ReportKind.DailySales);
        var dateWise = await Build(ReportKind.DateWiseSales);
        var supplierWise = await Build(ReportKind.SupplierWiseSales);

        using var ctx = Db.NewContext();
        // Water Sales credits net of reversals, by value date in the period.
        var ledgerSales = -ctx.LedgerEntries.Where(e => e.AccountId == SystemAccounts.WaterSales && e.ValueDate >= Day1 && e.ValueDate <= Day2)
            .Sum(e => e.DebitPaise - e.CreditPaise);
        Assert.Equal(66350, ledgerSales);
        Assert.Equal(ledgerSales, Paise(daily.FooterRows[^1][^1]));
        Assert.Equal(ledgerSales, Paise(dateWise.FooterRows[0][^1]));
        Assert.Equal(ledgerSales, Paise(supplierWise.FooterRows[0][^1]));
        Assert.Equal(4, daily.Rows.Count);   // the cancelled invoice is excluded
    }

    [Fact]
    public async Task Date_Wise_Groups_By_Day()
    {
        await TradeAsync();
        var table = await Build(ReportKind.DateWiseSales);
        Assert.Equal(["05/11/2026", "06/11/2026"], table.Rows.Select(r => r[0]));
        Assert.Equal(["273.50", "390.00"], table.Rows.Select(r => r[5]));   // 150 + 123.50 ; 240 + 150
    }

    [Fact]
    public async Task FR_RPT_02_Vehicle_Report_Has_Supplier_Subtotals_And_Grand_Total()
    {
        await TradeAsync();
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 100 });

        var table = await Build(ReportKind.VehicleOperational);

        Assert.Contains(table.Rows, r => r[0] == "Subtotal - Kaveri Tankers" && r[4] == "240.00" && r[5] == "100.00" && r[6] == "140.00");
        Assert.Contains(table.Rows, r => r[0] == "Subtotal - Sangeetha Water Supply" && r[4] == "423.50");
        Assert.Contains(table.Rows, r => r[1] == "TN 67 C" && r[2] == "10.0" && r[3] == "12.35/kL");
        Assert.Equal(["Grand total", "", "10.0", "", "663.50", "350.00", "313.50"], table.FooterRows[0]);
    }

    [Fact]
    public async Task FR_RPT_07_Outstanding_Equals_Party_Ledger_Balances()
    {
        await TradeAsync();

        var suppliers = await Build(ReportKind.SupplierOutstanding);
        var customers = await Build(ReportKind.CustomerOutstanding);

        using var ctx = Db.NewContext();
        var kaveri = suppliers.Rows.Single(r => r[0] == "Kaveri Tankers");
        Assert.Equal(await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, _kaveri), Paise(kaveri[4]));
        // The opening entry was posted on day 1, inside the period, so it is a period debit:
        // 1,000 opening + 120 (later cancelled) + 240 billed; credits 120 reversal + 100 received.
        Assert.Equal(("0.00", "1,360.00", "220.00", "1,140.00 Dr"), (kaveri[1], kaveri[2], kaveri[3], kaveri[4]));
        var fromDay2 = await Build(ReportKind.SupplierOutstanding, new ReportFilter(Day2, Day2));
        Assert.Equal("1,000.00 Dr", fromDay2.Rows.Single(r => r[0] == "Kaveri Tankers")[1]);   // b/f into day 2
        var customer = Assert.Single(customers.Rows);
        Assert.Equal("150.00 Dr", customer[4]);
    }

    [Fact]
    public async Task FR_RPT_07_Trial_Balance_Balances_And_Matches_Ledgers()
    {
        await TradeAsync();

        var tb = await Build(ReportKind.TrialBalance);

        Assert.Equal(tb.FooterRows[0][2], tb.FooterRows[0][3]);   // total debit = total credit
        using var ctx = Db.NewContext();
        var cashRow = tb.Rows.Single(r => r[0] == "Cash in Hand");
        Assert.Equal(await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash), Paise(cashRow[2]));
    }

    [Fact]
    public async Task Cash_Book_Closing_Equals_Cash_Ledger()
    {
        await TradeAsync();

        var book = await Build(ReportKind.CashBook);

        using var ctx = Db.NewContext();
        Assert.Equal(await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash), Paise(book.FooterRows[^1][5]));
        Assert.Equal(("250.00", "50.00"), (book.FooterRows[0][3], book.FooterRows[0][4]));   // in: 150 cash sale + 100 receipt; out: 50 fuel
    }

    [Fact]
    public async Task FR_RPT_06_Supplier_And_Vehicle_Filters()
    {
        await TradeAsync();

        var onlyKaveri = await Build(ReportKind.DailySales, Both with { SupplierId = _kaveri });
        var onlyVolume = await Build(ReportKind.DailySales, Both with { VehicleId = VolumeVehicle });
        var day2 = await Build(ReportKind.DailySales, new ReportFilter(Day2, Day2));

        Assert.All(onlyKaveri.Rows, r => Assert.Contains("TN 01 AB 1234", r[2]));
        Assert.Equal("TN 67 C", Assert.Single(onlyVolume.Rows)[2].Split(' ', 2)[1]);
        Assert.Equal(2, day2.Rows.Count);
        Assert.Contains("Supplier: Kaveri Tankers", onlyKaveri.SubtitleLines);
    }

    [Fact]
    public async Task Every_Report_Prints_As_Pdf()
    {
        await TradeAsync();
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        foreach (var kind in Enum.GetValues<ReportKind>())
        {
            var pdf = ReportPdf.Generate(await Build(kind), new CompanyInfo(), DateTime.UtcNow);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }

    [Fact]
    public async Task Billing_Clerk_Can_Run_Reports()
    {
        Me.User = Db.AddUser(UserRole.BillingClerk);
        Assert.NotNull(await Build(ReportKind.DailySales));
    }
}
