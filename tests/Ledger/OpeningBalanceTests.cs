using WaterBilling.Data.Entities;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Ledger;

public class OpeningBalanceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly TestServices _svc;

    public OpeningBalanceTests() => _svc = new TestServices(_db, new FakeCurrentUser(_db.AddUser(UserRole.Admin)));

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task FR_VM_12_P10_Dr_Opening_Balance_Posts_Once()
    {
        var id = (await _svc.Suppliers.CreateAsync(new SupplierForm
        {
            Name = "Sangeetha Water Supply", ShortName = "Sangeetha", Phone = "9876543210", OpeningBalance = 12500.50m,
        })).Value;

        using var ctx = _db.NewContext();
        var voucher = Assert.Single(ctx.Vouchers);
        Assert.Equal(VoucherType.Journal, voucher.Type);
        var lines = ctx.LedgerEntries.OrderBy(e => e.LineNo).ToList();
        Assert.Equal((SystemAccounts.SupplierReceivables, 1250050L, (long?)id), (lines[0].AccountId, lines[0].DebitPaise, lines[0].SupplierId));
        Assert.Equal((SystemAccounts.OpeningBalanceEquity, 1250050L), (lines[1].AccountId, lines[1].CreditPaise));
        Assert.Equal(1250050, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, id));
    }

    [Fact]
    public async Task FR_VM_12_P10_Cr_Opening_Balance_Is_Reversed_Sides()
    {
        var id = (await _svc.Customers.CreateAsync(new CustomerForm
        {
            Name = "Meena Constructions", Phone = "9876543210", OpeningBalance = 300, OpeningBalanceSide = BalanceSide.Cr,
        })).Value;

        using var ctx = _db.NewContext();
        Assert.Equal(-30000, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Customer, id));
        Assert.Equal(30000, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.OpeningBalanceEquity));
    }

    [Fact]
    public async Task Zero_Opening_Balance_Posts_Nothing()
    {
        await _svc.Customers.CreateAsync(new CustomerForm { Name = "Meena Constructions", Phone = "9876543210" });

        using var ctx = _db.NewContext();
        Assert.Empty(ctx.Vouchers);
    }

    [Fact]
    public async Task FR_VM_08_Party_With_Opening_Entry_Cannot_Be_Deleted()
    {
        var id = (await _svc.Customers.CreateAsync(new CustomerForm { Name = "Meena Constructions", Phone = "9876543210", OpeningBalance = 1 })).Value;

        Assert.False((await _svc.Customers.DeleteAsync(id)).Ok);
    }

    [Fact]
    public async Task Party_Creation_Fails_Cleanly_When_The_Day_Is_Closed()
    {
        using (var ctx = _db.NewContext())
        {
            ctx.BusinessDays.Add(new BusinessDay { Date = new DateOnly(2026, 11, 5), Status = BusinessDayStatus.Closed, Version = 1 });
            ctx.SaveChanges();
        }

        var result = await _svc.Suppliers.CreateAsync(new SupplierForm { Name = "X Water", ShortName = "X", Phone = "9876543210", OpeningBalance = 10 });

        Assert.False(result.Ok);
        using var check = _db.NewContext();
        Assert.Empty(check.Suppliers);   // nothing half-saved
    }
}
