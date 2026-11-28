using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Reports;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

// Client scenario (Nov 2026): a supplier owes for some trips, pays MORE and asks us to keep the extra as an
// advance for future trips. The extra stays on their account and pays their next invoices automatically.
public class AdvanceTests : BillingFixture
{
    private ReceiptService Rcp => Svc.Receipts;
    private static readonly DateOnly Day = new(2026, 11, 5);

    private async Task<long> Trip(decimal rate = 150, long? vehicle = null)
    {
        var r = await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = vehicle ?? TripVehicle, Rate = rate });
        Assert.True(r.Ok, r.Error);
        return r.Value;
    }

    private Invoice Get(long id) { using var db = Db.NewContext(); return db.Invoices.Single(i => i.Id == id); }
    private Task<long> Advance() => Rcp.AdvanceAvailableAsync(PartyType.Supplier, Supplier);
    private async Task<long> Balance() { using var db = Db.NewContext(); return await LedgerQueries.PartyBalanceAsync(db, PartyType.Supplier, Supplier); }

    private void Balanced()
    {
        using var db = Db.NewContext();
        Assert.Equal(db.LedgerEntries.Sum(e => e.DebitPaise), db.LedgerEntries.Sum(e => e.CreditPaise));
    }

    [Fact]
    public async Task ADV_01_Paying_More_Than_The_Ticked_Invoices_Keeps_The_Rest_As_Advance()
    {
        var a = await Trip(150);
        var b = await Trip(129.68m, VolumeVehicle);                      // owes ₹279.68

        var r = await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 400, Mode = PaymentMode.Cash, InvoiceIds = [a, b] });

        Assert.True(r.Ok, r.Error);
        Assert.Equal((InvoiceStatus.Paid, InvoiceStatus.Paid), (Get(a).Status, Get(b).Status));
        Assert.Equal(12032, await Advance());                           // ₹120.32 kept as advance
        Assert.Equal(-12032, await Balance());                          // the supplier's account is ₹120.32 in their favour (Cr)
        var row = (await Rcp.HistoryAsync(new ReceiptFilter(Day, Day))).Single();
        Assert.Equal((40000L, 27968L), (row.AmountPaise, row.AllocatedPaise));
        Balanced();
    }

    [Fact]
    public async Task ADV_02_Next_Invoices_Are_Paid_From_The_Advance_Until_It_Runs_Out()
    {
        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 400 });   // nothing pending: all ₹400 is advance

        var first = await Trip(150);
        var second = await Trip(150);
        var third = await Trip(150);

        Assert.Equal((15000L, InvoiceStatus.Paid), (Get(first).AllocatedPaise, Get(first).Status));
        Assert.Equal((15000L, InvoiceStatus.Paid), (Get(second).AllocatedPaise, Get(second).Status));
        Assert.Equal((10000L, InvoiceStatus.PartPaid), (Get(third).AllocatedPaise, Get(third).Status));   // ₹100 left of the advance
        Assert.Equal(0, await Advance());
        var fourth = await Trip(150);
        Assert.Equal((0L, InvoiceStatus.Posted), (Get(fourth).AllocatedPaise, Get(fourth).Status));       // Not paid
        Assert.Equal(5000 + 15000, await Balance());                    // owes ₹50 + ₹150
        using (var db = Db.NewContext()) Assert.All(db.ReceiptAllocations, a => Assert.True(a.FromAdvance));
        Me.User = Db.AddUser(UserRole.Admin);
        var log = (await new WaterBilling.Services.Admin.ActivityLogService(Db, Me, Db.Clock).SearchAsync(new(Day, Day, Text: "PaidFromAdvance")));
        Assert.NotEmpty(log);
        Balanced();
    }

    [Fact]
    public async Task ADV_03_Trip_Report_Counts_Advance_Payments_As_Received()
    {
        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 200 });
        await Trip(150);
        await Trip(150);

        Me.User = Db.AddUser(UserRole.Admin);
        var report = await Svc.TripReports.BuildAsync(new ReportFilter(Day, Day));
        Assert.Equal((30000L, 20000L, 10000L), (report.BillPaise, report.ReceivedPaise, report.OutstandingPaise));
    }

    [Fact]
    public async Task ADV_04_Cancelling_The_Receipt_Takes_Back_What_The_Advance_Paid()
    {
        var a = await Trip(150);
        var receipt = (await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 400, InvoiceIds = [a] })).Value;
        var later = await Trip(150);                                     // paid from the ₹250 advance
        Assert.Equal(InvoiceStatus.Paid, Get(later).Status);

        Me.User = Db.AddUser(UserRole.Admin);
        var row = (await Rcp.HistoryAsync(new ReceiptFilter(Day, Day))).Single(r => r.VoucherId == receipt);
        Assert.True((await Rcp.CancelAsync(receipt, "Cheque bounced", row.Version)).Ok);

        Assert.Equal((0L, InvoiceStatus.Posted), (Get(a).AllocatedPaise, Get(a).Status));
        Assert.Equal((0L, InvoiceStatus.Posted), (Get(later).AllocatedPaise, Get(later).Status));
        Assert.Equal(0, await Advance());
        Assert.Equal(30000, await Balance());
        Balanced();
    }

    [Fact]
    public async Task ADV_05_A_Refund_Reduces_The_Advance()
    {
        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 300, Mode = PaymentMode.Bank });
        Me.User = Db.AddUser(UserRole.Admin);
        var refund = await Svc.Payments.CreateAsync(new PaymentForm { AccountId = SystemAccounts.SupplierReceivables, PartyType = PartyType.Supplier,
            PartyId = Supplier, Amount = 100, Mode = PaymentMode.Bank });
        Assert.True(refund.Ok, refund.Error);

        Assert.Equal(20000, await Advance());                           // ₹300 received - ₹100 paid back
        var trip = await Trip(150);
        Assert.Equal(15000, Get(trip).AllocatedPaise);
        Assert.Equal(5000, await Advance());
        Balanced();
    }

    [Fact]
    public async Task ADV_06_Cancelling_An_Invoice_Paid_From_Advance_Returns_The_Money_To_The_Advance()
    {
        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 300 });
        var trip = await Trip(150);
        Assert.Equal(15000, await Advance());

        Me.User = Db.AddUser(UserRole.Admin);
        Assert.True((await Svc.Invoices.CancelAsync(trip, "Wrong vehicle", Get(trip).Version)).Ok);

        Assert.Equal(30000, await Advance());
        Assert.Equal(-30000, await Balance());
        var ticked = await Trip(150);                                    // paid from the advance again
        Assert.Equal(InvoiceStatus.Paid, Get(ticked).Status);
        Balanced();
    }

    [Fact]
    public async Task ADV_07_An_Invoice_Paid_By_A_Ticked_Receipt_Still_Needs_That_Receipt_Cancelled_First()
    {
        var a = await Trip(150);
        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 150, InvoiceIds = [a] });
        Me.User = Db.AddUser(UserRole.Admin);

        var r = await Svc.Invoices.CancelAsync(a, "test", Get(a).Version);

        Assert.False(r.Ok);
        Assert.Contains("Cancel those receipts first", r.Error);
    }
}
