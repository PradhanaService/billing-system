using WaterBilling.Components.Pdf;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Reports;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Reports;

// Module 9 of the final design: Vehicle Trip Report (service and PDF).
public class TripReportTests : BillingFixture
{
    private static readonly DateOnly Day1 = new(2026, 11, 5);
    private static readonly DateOnly Day2 = new(2026, 11, 6);
    private long _kaveri, _a3;

    public TripReportTests()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    // Sangeetha: A1 cash 150 (in 09:10 AM), A2 credit 129.68. Kaveri: A3 credit 2 trips 240 (part paid 100),
    // plus one cancelled A3 invoice that must not be counted.
    private async Task TradeAsync()
    {
        _kaveri = (await Svc.Suppliers.CreateAsync(new SupplierForm { Name = "Kaveri Tankers", ShortName = "Kaveri", Phone = "9123456780" })).Value;
        _a3 = (await Svc.Vehicles.CreateAsync(new VehicleForm { Code = "A3", VehicleNo = "TN 45 B 2210", SupplierId = _kaveri, TankCapacityLitres = 9000, Rate = 120 })).Value;
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash,
                                                         InDate = Day1, InTime = "09:10 AM", OutDate = Day1, OutTime = "09:40 AM" });
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = VolumeVehicle, Rate = 129.68m });
        var wrong = (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = _a3, Rate = 120 })).Value;
        await Svc.Invoices.CancelAsync(wrong, "wrong vehicle", 1);
        Db.Clock.Advance(TimeSpan.FromDays(1));
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = _a3, Rate = 120, Trips = 2 });
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = _kaveri, Amount = 100 });
    }

    private Task<TripReport> Build(long? supplier = null, long? vehicle = null) => Svc.TripReports.BuildAsync(new ReportFilter(Day1, Day2, supplier, vehicle));

    [Fact]
    public async Task RPT_01_All_Suppliers_Gives_Vehicle_Summary_Subtotals_And_Grand_Total()
    {
        await TradeAsync();
        var r = await Build();

        Assert.Equal(TripReportShape.AllSuppliers, r.Shape);
        Assert.Equal(["Kaveri Tankers", "Sangeetha Water Supply"], r.Suppliers.Select(s => s.Name));
        var kaveri = r.Suppliers[0];
        Assert.Equal((2, 24000L, 10000L, 14000L), (kaveri.Trips, kaveri.BillPaise, kaveri.ReceivedPaise, kaveri.OutstandingPaise));
        var sangeetha = r.Suppliers[1];
        Assert.Equal(["A1", "A2"], sangeetha.Vehicles.Select(v => v.Code));
        Assert.Equal((15000L, 15000L), (sangeetha.Vehicles[0].BillPaise, sangeetha.Vehicles[0].ReceivedPaise));   // cash = received
        Assert.Equal((4, 51968L, 25000L, 26968L), (r.Trips, r.BillPaise, r.ReceivedPaise, r.OutstandingPaise));
        Assert.Contains("2 suppliers · 3 vehicles", r.Subtitle);
    }

    [Fact]
    public async Task RPT_02_One_Supplier_Groups_Trips_By_Vehicle()
    {
        await TradeAsync();
        var r = await Build(supplier: Supplier);

        Assert.Equal(TripReportShape.OneSupplier, r.Shape);
        Assert.Equal("Sangeetha Water Supply", r.Title);
        var groups = r.Suppliers.Single().Vehicles;
        Assert.Equal(2, groups.Count);
        var line = groups[0].Lines.Single();
        Assert.Equal(("1", 1, PayState.Paid, "09:10 AM", "09:40 AM"),
            (line.InvoiceNo, line.SNo, line.State, IndianTime.FormatTime(line.InTimeUtc!.Value), IndianTime.FormatTime(line.OutTimeUtc!.Value)));
        Assert.Equal(PayState.NotPaid, groups[1].Lines.Single().State);
    }

    [Fact]
    public async Task RPT_03_One_Vehicle_Lists_Its_Trips_And_Leaves_Out_Cancelled()
    {
        await TradeAsync();
        var r = await Build(vehicle: _a3);

        Assert.Equal(TripReportShape.OneVehicle, r.Shape);
        Assert.Equal("A3 · TN 45 B 2210", r.Title);
        Assert.StartsWith("Kaveri Tankers", r.Subtitle);
        var line = r.Lines.Single();   // the cancelled invoice is not counted
        Assert.Equal(("4", 2, 24000L, PayState.PartPaid), (line.InvoiceNo, line.Trips, line.AmountPaise, line.State));
    }

    [Fact]
    public async Task FR_RPT_07_Trip_Report_Total_Equals_Water_Sales_Ledger()
    {
        await TradeAsync();
        var r = await Build();
        using var ctx = Db.NewContext();
        var ledgerSales = -ctx.LedgerEntries.Where(e => e.AccountId == SystemAccounts.WaterSales && e.ValueDate >= Day1 && e.ValueDate <= Day2)
            .Sum(e => e.DebitPaise - e.CreditPaise);
        Assert.Equal(ledgerSales, r.BillPaise);
        var supplierBalance = await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, _kaveri)
                            + await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier);
        Assert.Equal(supplierBalance, r.OutstandingPaise);
    }

    [Fact]
    public async Task FR_RPT_06_Date_Range_Filters_Trips()
    {
        await TradeAsync();
        var r = await Svc.TripReports.BuildAsync(new ReportFilter(Day2, Day2));
        Assert.Equal(["Kaveri Tankers"], r.Suppliers.Select(s => s.Name));
        await Assert.ThrowsAsync<ArgumentException>(() => Svc.TripReports.BuildAsync(new ReportFilter(Day2, Day1)));
    }

    [Fact]
    public async Task RPT_04_Pdf_Has_The_Same_Totals_As_The_Screen()
    {
        await TradeAsync();
        foreach (var r in new[] { await Build(), await Build(supplier: Supplier), await Build(vehicle: _a3) })
        {
            var table = TripReportService.ToTable(r);
            Assert.Contains(table.FooterRows, row => row.Contains(Money.Format(r.BillPaise)));
            var pdf = ReportPdf.Generate(table, new CompanyInfo(), DateTime.UtcNow);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }
    }

    [Fact]
    public async Task NFR_SEC_05_Not_Logged_In_Is_Refused()
    {
        Me.User = null;
        await Assert.ThrowsAsync<WaterBilling.Services.Security.NotAllowedException>(() => Build());
    }
}
