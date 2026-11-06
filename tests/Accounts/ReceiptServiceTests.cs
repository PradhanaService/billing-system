using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

public class ReceiptServiceTests : BillingFixture
{
    private ReceiptService Rcp => Svc.Receipts;

    private async Task<long> CreditInvoiceAsync(long vehicle, decimal volume = 0) =>
        (await Svc.Invoices.CreateAsync(new InvoiceForm
        {
            SalesPersonId = Seller,
            VehicleId = vehicle, Rate = vehicle == TripVehicle ? 150 : 12.35m, VolumeKl = volume,
        })).Value;

    private ReceiptForm Form(decimal amount, PaymentMode mode = PaymentMode.Cash, string? reference = null) =>
        new() { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = amount, Mode = mode, ReferenceNo = reference };

    [Fact]
    public async Task FR_TXN_06_P3_Receipt_Posts_Cash_And_Party()
    {
        var id = (await Rcp.CreateAsync(Form(500))).Value;

        using var ctx = Db.NewContext();
        var v = ctx.Vouchers.Single(x => x.Id == id);
        Assert.Equal("RV-1", v.DisplayNo);
        Assert.Equal(50000, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        Assert.Equal(-50000, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
    }

    [Theory]
    [InlineData(PaymentMode.Bank)]
    [InlineData(PaymentMode.Upi)]
    public async Task FR_TXN_03_Reference_Required_For_Bank_And_Upi(PaymentMode mode)
    {
        Assert.False((await Rcp.CreateAsync(Form(100, mode))).Ok);
        Assert.True((await Rcp.CreateAsync(Form(100, mode, "UTR123"))).Ok);

        using var ctx = Db.NewContext();
        Assert.Equal(10000, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.ForMode(mode)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task FR_TXN_05_Amount_Must_Be_Above_Zero(decimal amount) =>
        Assert.False((await Rcp.CreateAsync(Form(amount))).Ok);

    [Fact]
    public async Task FR_TXN_09_Allocates_Oldest_First_And_Keeps_Remainder_Unallocated()
    {
        var inv1 = await CreditInvoiceAsync(TripVehicle);                 // 150.00
        var inv2 = await CreditInvoiceAsync(VolumeVehicle, 10);           // 123.50
        var inv3 = await CreditInvoiceAsync(TripVehicle);                 // 150.00

        var preview = await Rcp.PreviewAllocationAsync(PartyType.Supplier, Supplier, 20000);
        Assert.Equal([15000L, 5000L], preview.Select(p => p.AllocatedPaise));

        await Rcp.CreateAsync(Form(200));                                 // pays inv1 fully, inv2 by 50.00
        await Rcp.CreateAsync(Form(300));                                 // 73.50 to inv2, 150 to inv3, 76.50 on account

        using var ctx = Db.NewContext();
        var statuses = ctx.Invoices.OrderBy(i => i.InvoiceNo).Select(i => new { i.Status, i.AllocatedPaise }).ToList();
        Assert.All(statuses, s => Assert.Equal(InvoiceStatus.Paid, s.Status));
        Assert.Equal(-7650, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
        var (total, vehicles, unallocated) = await LedgerQueries.SupplierSplitAsync(ctx, Supplier);
        Assert.Equal(0, vehicles);                // each vehicle is fully paid
        Assert.Equal(-7650, unallocated);         // the advance sits on the supplier, not a vehicle
        Assert.Equal(total, vehicles + unallocated);   // FR-LED-08
    }

    [Fact]
    public async Task FR_TXN_10_Part_Payment_Moves_Vehicle_Balance()
    {
        await CreditInvoiceAsync(TripVehicle);
        await Rcp.CreateAsync(Form(100));

        using var ctx = Db.NewContext();
        Assert.Equal(5000, await LedgerQueries.LastVehicleRunningBalanceAsync(ctx, SystemAccounts.SupplierReceivables, Supplier, null, TripVehicle));
        Assert.Equal(InvoiceStatus.PartPaid, ctx.Invoices.Single().Status);
    }

    [Fact]
    public async Task Cash_Invoices_Are_Not_Allocated_Again()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });

        Assert.Empty(await Rcp.PreviewAllocationAsync(PartyType.Supplier, Supplier, 10000));
    }

    [Fact]
    public async Task Customer_Receipt_Pays_Customer_Invoices_Only()
    {
        await CreditInvoiceAsync(TripVehicle);   // supplier's invoice
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, BillToType = PartyType.Customer, CustomerId = Customer });

        var preview = await Rcp.PreviewAllocationAsync(PartyType.Customer, Customer, 100000);

        Assert.Equal("INV-2", Assert.Single(preview).InvoiceNo);
    }

    [Fact]
    public async Task Dispatch_Operator_Can_Take_Any_Mode()
    {
        Me.User = Db.AddUser(UserRole.SalesPerson);

        Assert.True((await Rcp.CreateAsync(Form(100))).Ok);
        Assert.True((await Rcp.CreateAsync(Form(100, PaymentMode.Upi, "UTR1"))).Ok);
    }

    [Fact]
    public async Task FR_TXN_11_Cancel_Reverses_And_Frees_The_Invoices()
    {
        var invoiceId = await CreditInvoiceAsync(TripVehicle);
        var receiptId = (await Rcp.CreateAsync(Form(150))).Value;

        // The invoice cannot be cancelled while the receipt pays it.
        var inv = (await Svc.Invoices.GetAsync(invoiceId))!;
        var blocked = await Svc.Invoices.CancelAsync(invoiceId, "wrong", inv.Version);
        Assert.False(blocked.Ok);
        Assert.Contains("RV-1", blocked.Error);

        Me.User = Db.AddUser(UserRole.SalesPerson);
        var row = Assert.Single(await Rcp.HistoryAsync(new ReceiptFilter(DateOnly.MinValue, DateOnly.MaxValue)));
        Assert.False((await Rcp.CancelAsync(receiptId, "", row.Version)).Ok);
        Assert.True((await Rcp.CancelAsync(receiptId, "Cheque bounced", row.Version)).Ok);

        using var ctx = Db.NewContext();
        Assert.Equal((InvoiceStatus.Posted, 0L), (ctx.Invoices.Single().Status, ctx.Invoices.Single().AllocatedPaise));
        Assert.Equal(15000, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
        Assert.Equal(0, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        Assert.All(ctx.ReceiptAllocations, a => Assert.False(a.IsActive));

        // Now the invoice can be cancelled.
        Me.User = Db.AddUser(UserRole.SalesPerson);
        inv = (await Svc.Invoices.GetAsync(invoiceId))!;
        Assert.True((await Svc.Invoices.CancelAsync(invoiceId, "wrong", inv.Version)).Ok);
    }

    [Fact]
    public async Task Billing_Clerk_Can_Cancel_Receipts()
    {
        var id = (await Rcp.CreateAsync(Form(100))).Value;
        var row = Assert.Single(await Rcp.HistoryAsync(new ReceiptFilter(DateOnly.MinValue, DateOnly.MaxValue)));
        Assert.True((await Rcp.CancelAsync(id, "x", row.Version)).Ok);
    }

    [Fact]
    public async Task FR_TXN_12_History_Filters_By_Mode_And_Status()
    {
        await Rcp.CreateAsync(Form(100));
        await Rcp.CreateAsync(Form(200, PaymentMode.Upi, "UTR9"));

        var all = new ReceiptFilter(DateOnly.MinValue, DateOnly.MaxValue);
        Assert.Equal(2, (await Rcp.HistoryAsync(all)).Count);
        Assert.Equal("RV-2", Assert.Single(await Rcp.HistoryAsync(all with { Mode = PaymentMode.Upi })).DisplayNo);
        Assert.Empty(await Rcp.HistoryAsync(all with { Status = VoucherStatus.Cancelled }));
        Assert.Equal(2, (await Rcp.HistoryAsync(all with { PartyId = Supplier })).Count);
    }

    [Fact]
    public async Task Ledger_Stays_Balanced_After_Many_Operations()
    {
        await CreditInvoiceAsync(TripVehicle);
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });
        var r = (await Rcp.CreateAsync(Form(99.99m))).Value;
        Me.User = Db.AddUser(UserRole.Admin);
        await Rcp.CancelAsync(r, "test", 1);

        using var ctx = Db.NewContext();
        Assert.Equal(ctx.LedgerEntries.Sum(e => e.DebitPaise), ctx.LedgerEntries.Sum(e => e.CreditPaise));
        foreach (var g in ctx.LedgerEntries.ToList().GroupBy(e => e.VoucherId))
            Assert.Equal(g.Sum(e => e.DebitPaise), g.Sum(e => e.CreditPaise));
    }
}
