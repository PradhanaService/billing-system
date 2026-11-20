using Microsoft.EntityFrameworkCore;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;

namespace WaterBilling.Tests.Billing;

// Module 6 (final design): Invoice Display search and the Edit flow.
// Edit = cancel and reissue (FR-INV-11, CLAUDE.md principle 1): the ledger is never changed in place.
public class InvoiceEditTests : BillingFixture
{
    private InvoiceService Inv => Svc.Invoices;
    private static readonly DateOnly Day = new(2026, 11, 5);

    private async Task<long> Credit(string inTime = "10:05 AM") =>
        (await Inv.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Credit,
                                                 InDate = Day, InTime = inTime })).Value;

    private async Task<InvoiceRevision> RevisionFor(long id)
    {
        var d = (await Inv.GetAsync(id))!;
        return new InvoiceRevision { EditedBySalesPersonId = Seller, CapacityLitres = d.CapacityLitres, Rate = d.RatePaise / 100m,
                                     InDate = Day, InTime = "10:05 AM", Version = d.Version };
    }

    [Fact]
    public async Task FR_INV_11_Edit_Cancels_The_Old_Invoice_And_Issues_A_Corrected_Copy()
    {
        var id = await Credit();
        var r = await RevisionFor(id);
        (r.Rate, r.CapacityLitres, r.OutDate, r.OutTime) = (175m, 13000, Day, "10:30 AM");

        var result = await Inv.ReviseAsync(id, r);

        Assert.True(result.Ok, result.Error);
        var old = (await Inv.GetAsync(id))!;
        var copy = (await Inv.GetAsync(result.Value))!;
        Assert.Equal(InvoiceStatus.Cancelled, old.Status);
        Assert.Equal(copy.DisplayNo, old.ReplacedByNo);
        Assert.Equal(old.DisplayNo, copy.ReplacesNo);
        Assert.Equal((17500L, 13000), (copy.AmountPaise, copy.CapacityLitres));
        Assert.Equal("10:30 AM", Services.Common.IndianTime.FormatTime(copy.OutTimeUtc!.Value));
        Assert.Equal("Ravi", copy.CreatedBy);   // Created by stays the original maker
    }

    [Fact]
    public async Task FR_LED_01_Edit_Keeps_The_Ledger_Balanced_And_Never_Changes_Old_Lines()
    {
        var id = await Credit();
        using (var db = Db.NewContext())
        {
            var linesBefore = await db.LedgerEntries.CountAsync();
            var r = await RevisionFor(id);
            r.Rate = 200m;
            Assert.True((await Inv.ReviseAsync(id, r)).Ok);

            using var after = Db.NewContext();
            Assert.Equal(linesBefore + 4, await after.LedgerEntries.CountAsync());   // reversal (2) + new sale (2)
            Assert.Equal(await after.LedgerEntries.SumAsync(l => l.DebitPaise), await after.LedgerEntries.SumAsync(l => l.CreditPaise));
            // Supplier now owes the corrected amount only: 150 reversed, 200 charged.
            var supplierDr = await after.LedgerEntries.Where(l => l.SupplierId == Supplier).SumAsync(l => l.DebitPaise - l.CreditPaise);
            Assert.Equal(20000, supplierDr);
        }
    }

    [Fact]
    public async Task Edit_Moves_Receipts_To_The_Corrected_Copy_And_Refuses_A_Lower_Amount()
    {
        var id = await Credit();
        Assert.True((await Svc.Receipts.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 100 })).Ok);   // pays 100 of 150

        var lower = await RevisionFor(id);
        lower.Rate = 80m;
        var refused = await Inv.ReviseAsync(id, lower);
        Assert.False(refused.Ok);
        Assert.Contains("cannot be lower", refused.Error);

        var r = await RevisionFor(id);
        r.Rate = 160m;
        var copyId = (await Inv.ReviseAsync(id, r)).Value;
        var copy = (await Inv.GetAsync(copyId))!;
        Assert.Equal(10000, copy.AllocatedPaise);
        Assert.Single(await Svc.InvoiceSearch.ReceiptsForAsync(copyId));
        Assert.Empty(await Svc.InvoiceSearch.ReceiptsForAsync(id));
    }

    [Fact]
    public async Task Edit_Needs_A_Salesperson_On_The_Sales_Login_A_Change_And_The_Current_Version()
    {
        var id = await Credit();
        var r = await RevisionFor(id);

        Assert.Contains("Nothing was changed", (await Inv.ReviseAsync(id, r)).Error);
        r.Rate = 175m; r.EditedBySalesPersonId = null;
        Assert.Contains("which salesperson is editing", (await Inv.ReviseAsync(id, r)).Error);
        r.EditedBySalesPersonId = Seller; r.Version = 99;
        Assert.Contains("changed by someone else", (await Inv.ReviseAsync(id, r)).Error);
    }

    [Fact]
    public async Task Edit_Refuses_A_Cancelled_Invoice_And_Out_Before_In()
    {
        var id = await Credit();
        var r = await RevisionFor(id);
        (r.OutDate, r.OutTime) = (Day, "09:00 AM");
        Assert.Contains("Out Time cannot be before In Time", (await Inv.ReviseAsync(id, r)).Error);

        var d = (await Inv.GetAsync(id))!;
        Assert.True((await Inv.CancelAsync(id, "wrong vehicle", d.Version)).Ok);
        var again = await RevisionFor(id);
        again.Rate = 175m;
        Assert.Contains("cancelled and cannot be edited", (await Inv.ReviseAsync(id, again)).Error);
    }

    [Fact]
    public async Task Edit_Is_Recorded_In_The_Activity_Log_With_Who_And_Before_After()
    {
        var id = await Credit();
        var r = await RevisionFor(id);
        r.Rate = 175m;
        await Inv.ReviseAsync(id, r);

        using var db = Db.NewContext();
        var log = await db.ActivityLogs.SingleAsync(l => l.Action == "INVOICE_EDITED");
        Assert.Contains("Ravi", log.Reason);
        Assert.Contains("15000", log.BeforeJson);
        Assert.Contains("17500", log.AfterJson);
    }

    // ---- Invoice Display search ----
    [Fact]
    public async Task ID_01_Search_By_Number_Vehicle_Status_And_In_Time_Range()
    {
        var morning = await Credit("09:30 AM");
        var noon = await Credit("12:10 PM");
        var cash = (await Inv.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = VolumeVehicle, Rate = 129.68m,
                                                           PaymentMode = InvoicePaymentMode.Cash, InDate = Day, InTime = "05:45 PM" })).Value;
        var all = new InvoiceFilter(Day, Day);
        var search = Svc.InvoiceSearch;

        Assert.Equal(3, (await search.SearchAsync(all)).Count);
        var noonNo = (await Inv.GetAsync(noon))!.DisplayNo;
        Assert.Equal(noon, Assert.Single(await search.SearchAsync(all with { Text = noonNo, SearchIn = InvoiceSearchIn.InvoiceNumber })).Id);
        Assert.Equal(cash, Assert.Single(await search.SearchAsync(all with { Text = "tn67c", SearchIn = InvoiceSearchIn.VehicleNumber })).Id);
        Assert.Equal(cash, Assert.Single(await search.SearchAsync(all with { Status = PayState.Paid })).Id);
        Assert.Equal(2, (await search.SearchAsync(all with { Status = PayState.NotPaid })).Count);
        var range = await search.SearchAsync(all with { TimeFrom = new TimeOnly(9, 0), TimeTo = new TimeOnly(12, 0) });
        Assert.Equal(morning, Assert.Single(range).Id);
    }

    [Fact]
    public async Task ID_02_Paid_Status_Lists_The_Receipt_That_Paid_It()
    {
        var id = await Credit();
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 150 });

        var row = Assert.Single(await Svc.InvoiceSearch.SearchAsync(new InvoiceFilter(Day, Day)));
        Assert.Equal(PayState.Paid, row.State);
        var receipt = Assert.Single(await Svc.InvoiceSearch.ReceiptsForAsync(id));
        Assert.Equal((15000L, 15000L, "Sangeetha Water Supply"), (receipt.ReceiptAmountPaise, receipt.PaidToThisInvoicePaise, receipt.ReceivedFrom));
        Assert.Equal(receipt.ReceiptNo, Assert.Single(row.ReceiptNos));
    }
}
