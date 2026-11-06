using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

public class PaymentServiceTests : BillingFixture
{
    private const long FuelExpense = 10;

    public PaymentServiceTests() => Me.User = Db.AddUser(UserRole.SalesPerson);

    private PaymentService Pay => Svc.Payments;

    private async Task PutCashInDrawerAsync(decimal rupees) =>
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = rupees });

    [Fact]
    public async Task P4_Expense_Payment_Posts_Expense_And_Cash_Tagged_With_Vehicle()
    {
        await PutCashInDrawerAsync(1000);

        var id = (await Pay.CreateAsync(new PaymentForm
        {
            AccountId = FuelExpense, PayTo = "Indian Oil", VehicleId = TripVehicle, Amount = 450.50m,
        })).Value;

        using var ctx = Db.NewContext();
        Assert.Equal("PV-1", ctx.Vouchers.Single(v => v.Id == id).DisplayNo);
        var expense = ctx.LedgerEntries.Single(e => e.AccountId == FuelExpense);
        Assert.Equal((45050L, (long?)TripVehicle), (expense.DebitPaise, expense.VehicleId));
        Assert.Equal(100000 - 45050, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
    }

    [Fact]
    public async Task P5_Refund_To_Supplier_Debits_The_Party()
    {
        await PutCashInDrawerAsync(500);   // supplier now has ₹500 Cr (advance)

        var result = await Pay.CreateAsync(new PaymentForm
        {
            AccountId = SystemAccounts.SupplierReceivables, PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 500,
        });

        Assert.True(result.Ok, result.Error);
        using var ctx = Db.NewContext();
        Assert.Equal(0, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, Supplier));
        Assert.Equal(0, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
    }

    [Fact]
    public async Task FR_TXN_08_Cash_Above_Drawer_Needs_Confirmation()
    {
        await PutCashInDrawerAsync(100);
        var form = new PaymentForm { AccountId = FuelExpense, Amount = 150 };

        var warned = await Pay.CreateAsync(form);
        Assert.False(warned.Ok);
        Assert.Contains("Cash in hand is only", warned.Error);

        form.ConfirmAboveCash = true;
        Assert.True((await Pay.CreateAsync(form)).Ok);
    }

    [Fact]
    public async Task Bank_Payment_Needs_Reference_But_No_Cash_Check()
    {
        var form = new PaymentForm { AccountId = FuelExpense, Amount = 99999, Mode = PaymentMode.Bank };
        Assert.False((await Pay.CreateAsync(form)).Ok);
        form.ReferenceNo = "NEFT-1";
        Assert.True((await Pay.CreateAsync(form)).Ok);
    }

    [Theory]
    [InlineData(SystemAccounts.Cash)]
    [InlineData(SystemAccounts.WaterSales)]
    [InlineData(SystemAccounts.OpeningBalanceEquity)]
    [InlineData(SystemAccounts.CashOverShort)]
    public async Task Cannot_Pay_To_System_Ledgers(long accountId) =>
        Assert.False((await Pay.CreateAsync(new PaymentForm { AccountId = accountId, Amount = 1, ConfirmAboveCash = true })).Ok);

    [Fact]
    public async Task Payment_Ledger_List_Has_Expenses_And_Refunds_Only()
    {
        var names = (await Pay.LedgersAsync()).Select(l => l.Name).ToList();

        Assert.Contains("Fuel Expense", names);
        Assert.Contains("Refund / payout to supplier", names);
        Assert.DoesNotContain("Cash in Hand", names);
        Assert.DoesNotContain("Cash Over/Short", names);
    }

    [Fact]
    public async Task FR_TXN_11_Cancel_Payment_Restores_Cash()
    {
        await PutCashInDrawerAsync(1000);
        var id = (await Pay.CreateAsync(new PaymentForm { AccountId = FuelExpense, Amount = 300 })).Value;
        var row = Assert.Single(await Pay.HistoryAsync(DateOnly.MinValue, DateOnly.MaxValue));

        Assert.True((await Pay.CancelAsync(id, "Duplicate bill", row.Version)).Ok);

        using var ctx = Db.NewContext();
        Assert.Equal(100000, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.Cash));
        Assert.Equal(VoucherStatus.Cancelled, Assert.Single(await Pay.HistoryAsync(DateOnly.MinValue, DateOnly.MaxValue)).Status);
    }

    [Fact]
    public async Task Billing_Clerk_Can_Make_Payments()
    {
        Me.User = Db.AddUser(UserRole.SalesPerson);
        Assert.True((await Pay.CreateAsync(new PaymentForm { AccountId = FuelExpense, Amount = 1, ConfirmAboveCash = true })).Ok);
    }
}

public class LedgerMasterServiceTests : BillingFixture
{
    public LedgerMasterServiceTests() => Me.User = Db.AddUser(UserRole.SalesPerson);

    [Fact]
    public async Task FR_TXN_13_Creates_Expense_Ledger_With_Unique_Name()
    {
        Assert.True((await Svc.Ledgers.CreateAsync(new LedgerForm { Name = "Tyre Replacement" })).Ok);
        Assert.False((await Svc.Ledgers.CreateAsync(new LedgerForm { Name = "tyre  replacement" })).Ok);
        Assert.False((await Svc.Ledgers.CreateAsync(new LedgerForm { Name = "Fuel expense" })).Ok);

        var row = (await Svc.Ledgers.ListAsync()).Single(l => l.Name == "Tyre Replacement");
        Assert.Equal((AccountType.Expense, false), (row.Type, row.IsSystem));
    }

    [Fact]
    public async Task FR_TXN_13_System_Ledgers_Cannot_Be_Changed_Or_Deleted()
    {
        var cash = (await Svc.Ledgers.ListAsync()).Single(l => l.Id == SystemAccounts.Cash);

        Assert.False((await Svc.Ledgers.UpdateAsync(cash.Id, new LedgerForm { Name = "Petty Cash", Version = cash.Version })).Ok);
        Assert.False((await Svc.Ledgers.DeleteAsync(cash.Id)).Ok);
    }

    [Fact]
    public async Task FR_TXN_13_Ledger_With_Entries_Cannot_Be_Deleted()
    {
        await Svc.Payments.CreateAsync(new PaymentForm { AccountId = 10, Amount = 1, ConfirmAboveCash = true });

        var result = await Svc.Ledgers.DeleteAsync(10);

        Assert.False(result.Ok);
        Assert.Contains("Inactive", result.Error);
        Assert.True((await Svc.Ledgers.DeleteAsync(11)).Ok);   // Oil Change: no entries yet
    }

    [Fact]
    public async Task FR_INV_01_Admin_Sets_Starting_Invoice_Number_Only_Before_First_Use()
    {
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.Ledgers.SetSeriesStartAsync("INVOICE", 7232));

        Me.User = Db.AddUser(UserRole.Admin);
        Assert.True((await Svc.Ledgers.SetSeriesStartAsync("INVOICE", 7232)).Ok);
        Assert.Equal("INV-7232", await Svc.Invoices.PeekNextNumberAsync());

        await Svc.Invoices.CreateAsync(new WaterBilling.Services.Billing.InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 });
        Assert.False((await Svc.Ledgers.SetSeriesStartAsync("INVOICE", 1)).Ok);
        Assert.Equal("INV-7233", await Svc.Invoices.PeekNextNumberAsync());
    }
}
