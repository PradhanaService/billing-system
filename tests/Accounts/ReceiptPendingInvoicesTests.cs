using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

// Module 7 (final design): a receipt pays the pending invoices the user ticks, several at once,
// in full (more than their total: the rest is an advance; less: refused, no part payments). FR-TXN-09 oldest-first stays for a receipt with nothing ticked.
public class ReceiptPendingInvoicesTests : BillingFixture
{
    private ReceiptService Rcp => Svc.Receipts;

    private async Task<long> Credit(long vehicle, decimal rate) =>
        (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = vehicle, Rate = rate, PaymentMode = InvoicePaymentMode.Credit })).Value;

    [Fact]
    public async Task RCP_01_Pending_List_Shows_Only_Unpaid_Invoices_Of_That_Party_Newest_First()
    {
        var a = await Credit(TripVehicle, 150);
        var b = await Credit(VolumeVehicle, 129.68m);
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });

        var pending = await Rcp.PendingInvoicesAsync(PartyType.Supplier, Supplier);

        Assert.Equal([b, a], pending.Select(p => p.Id));
        Assert.Equal((12968L, "A2", "TN 67 C"), (pending[0].DuePaise, pending[0].VehicleCode, pending[0].VehicleNo));
    }

    [Fact]
    public async Task RCP_02_One_Receipt_Pays_Several_Chosen_Invoices_In_Full()
    {
        var a = await Credit(TripVehicle, 150);
        var b = await Credit(VolumeVehicle, 129.68m);
        var c = await Credit(TripVehicle, 150);

        var r = await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 279.68m, InvoiceIds = [a, b] });

        Assert.True(r.Ok, r.Error);
        using var db = Db.NewContext();
        Assert.Equal(InvoiceStatus.Paid, db.Invoices.Single(i => i.Id == a).Status);
        Assert.Equal(InvoiceStatus.Paid, db.Invoices.Single(i => i.Id == b).Status);
        Assert.Equal(InvoiceStatus.Posted, db.Invoices.Single(i => i.Id == c).Status);   // not ticked: untouched although older ones are paid
        Assert.Equal([c], (await Rcp.PendingInvoicesAsync(PartyType.Supplier, Supplier)).Select(p => p.Id));
    }

    [Fact]
    public async Task RCP_03_No_Part_Payments_The_Amount_Must_Cover_The_Ticked_Invoices()
    {
        var a = await Credit(TripVehicle, 150);
        var b = await Credit(VolumeVehicle, 129.68m);

        var r = await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 200, InvoiceIds = [b, a] });

        Assert.False(r.Ok);
        Assert.Contains("Part payments are not accepted", r.Error);
        Assert.Contains("₹279.68", r.Error);
        using var db = Db.NewContext();
        Assert.All(db.Invoices, i => Assert.Equal(0, i.AllocatedPaise));
        Assert.Empty(db.Vouchers.Where(v => v.Type == VoucherType.Receipt));
    }

    [Fact]
    public async Task RCP_04_An_Invoice_Already_Paid_Or_Of_Another_Party_Is_Refused()
    {
        var a = await Credit(TripVehicle, 150);
        Assert.True((await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 150, InvoiceIds = [a] })).Ok);

        var again = await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 150, InvoiceIds = [a] });

        Assert.False(again.Ok);
        Assert.Contains("no longer pending", again.Error);
    }

    [Fact]
    public async Task RCP_05_History_Shows_Which_Invoices_Each_Receipt_Paid()
    {
        var a = await Credit(TripVehicle, 150);
        var b = await Credit(VolumeVehicle, 129.68m);
        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 279.68m, InvoiceIds = [a, b] });

        var day = Svc.Invoices.BusinessDate();
        var row = Assert.Single(await Rcp.HistoryAsync(new ReceiptFilter(day, day)));

        var nos = new[] { (await Svc.Invoices.GetAsync(b))!.DisplayNo, (await Svc.Invoices.GetAsync(a))!.DisplayNo };
        Assert.Equal(nos, row.InvoicesPaid);
        Assert.Equal(27968, row.AllocatedPaise);
    }

    [Fact]
    public async Task FR_TXN_09_Nothing_Ticked_Still_Pays_Oldest_First()
    {
        var a = await Credit(TripVehicle, 150);
        var b = await Credit(VolumeVehicle, 129.68m);

        await Rcp.CreateAsync(new ReceiptForm { PartyId = Supplier, Amount = 150 });

        using var db = Db.NewContext();
        Assert.Equal(InvoiceStatus.Paid, db.Invoices.Single(i => i.Id == a).Status);
        Assert.Equal(InvoiceStatus.Posted, db.Invoices.Single(i => i.Id == b).Status);
    }
}
