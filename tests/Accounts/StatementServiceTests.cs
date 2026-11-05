using System.Net;
using WaterBilling.Components.Pdf;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

public class StatementServiceTests : BillingFixture
{
    private static readonly DateOnly Day1 = new(2026, 11, 5);
    private static readonly DateOnly Day2 = new(2026, 11, 6);

    // Day 1 (05/11): invoice 150 credit. Day 2 (06/11): invoice 150 credit, receipt 100 cash,
    // and cancel day 1's invoice (reversal dated day 2, value date day 1).
    private async Task<long> BuildHistoryAsync()
    {
        var first = (await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 })).Value;
        Db.Clock.Advance(TimeSpan.FromDays(1));
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });
        var inv = (await Svc.Invoices.GetAsync(first))!;
        await Svc.Invoices.CancelAsync(first, "Wrong vehicle", inv.Version);
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 100 });
        Me.User = Db.AddUser(UserRole.Accountant);
        return first;
    }

    [Fact]
    public async Task FR_LED_05_Supplier_Statement_Has_Opening_Totals_And_Closing()
    {
        await BuildHistoryAsync();

        var s = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day2, Day2)))!;

        Assert.Equal(15000, s.OpeningPaise);                 // day 1's invoice
        Assert.Equal(3, s.Lines.Count);                      // invoice, reversal, receipt
        Assert.Equal(15000, s.TotalDebitPaise);
        Assert.Equal(25000, s.TotalCreditPaise);             // reversal 150 + receipt 100
        Assert.Equal(5000, s.ClosingPaise);
        Assert.Equal(s.OpeningPaise + s.TotalDebitPaise - s.TotalCreditPaise, s.ClosingPaise);
    }

    [Fact]
    public async Task FR_LED_04_Statement_Balance_Matches_Stored_Running_Balance()
    {
        await BuildHistoryAsync();
        var s = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day1, Day2)))!;

        using var ctx = Db.NewContext();
        var stored = ctx.LedgerEntries.Where(e => e.SupplierId == Supplier).OrderBy(e => e.PostingSeq)
            .Select(e => e.RunningBalancePaise).ToList();
        Assert.Equal(stored, s.Lines.Select(l => l.BalancePaise));
    }

    [Fact]
    public async Task FR_LED_06_Sort_By_Value_Date_Moves_The_Reversal_To_Its_Original_Day()
    {
        await BuildHistoryAsync();

        var byDate = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day1, Day1)))!;
        var byValue = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day1, Day1, SortByValueDate: true)))!;

        Assert.Single(byDate.Lines);                      // only the invoice was entered on day 1
        Assert.Equal(2, byValue.Lines.Count);             // the reversal belongs to day 1
        Assert.Equal(0, byValue.ClosingPaise);
        Assert.Contains("Reversal of INV-1", byValue.Lines[1].Particulars);
    }

    [Fact]
    public async Task FR_LED_07_Vehicle_Statement_Shows_Only_Its_Tagged_Lines()
    {
        await BuildHistoryAsync();
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 500 });

        var vehicle = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Vehicle, TripVehicle, Day1, Day2)))!;
        var supplier = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day1, Day2)))!;

        Assert.Equal(0, vehicle.ClosingPaise);            // both trips paid or reversed
        Assert.Equal(-45000, supplier.ClosingPaise);      // advance stays on the supplier, untagged
        Assert.True(vehicle.Lines.Count < supplier.Lines.Count);
    }

    [Fact]
    public async Task Cash_Book_Is_The_Cash_Account_Statement()
    {
        await BuildHistoryAsync();

        var s = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Account, SystemAccounts.Cash, Day1, Day2)))!;

        Assert.Equal("Cash Book", s.Title);
        Assert.Equal(10000, s.ClosingPaise);
    }

    [Fact]
    public async Task Statement_Table_Has_Spec_Columns_And_Dr_Cr()
    {
        await BuildHistoryAsync();
        var s = (await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day2, Day2)))!;

        var table = StatementService.ToTable(s);

        Assert.Equal(["Date", "Value Date", "Particulars", "Voucher / Ref No", "Debit (Dr)", "Credit (Cr)", "Balance"],
            table.Columns.Select(c => c.Title));
        Assert.Equal("Opening Balance b/f", table.Rows[0][2]);
        Assert.Equal("150.00 Dr", table.Rows[0][6]);
        Assert.Equal("Closing Balance c/f", table.FooterRows[^1][2]);
        Assert.Equal("50.00 Dr", table.FooterRows[^1][6]);

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = ReportPdf.Generate(table, new CompanyInfo(), DateTime.UtcNow);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Dispatch_Operator_Can_See_Statements()
    {
        Me.User = Db.AddUser(UserRole.DispatchOperator);
        Assert.NotNull(await Svc.Statements.GetAsync(new StatementRequest(StatementKind.Supplier, Supplier, Day1, Day2)));
    }
}

public class StatementPdfEndpointTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;
    public StatementPdfEndpointTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Cash_Book_Pdf_Downloads_For_Admin()
    {
        var client = await _factory.AdminClientAsync();
        var response = await client.GetAsync("/pdf/statement?kind=Account&id=1&from=2026-01-01&to=2026-12-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
    }
}
