using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;
using WaterBilling.Tests.Masters;

namespace WaterBilling.Tests.Ledger;

public class LedgerPosterTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly UserInfo _user;
    private readonly TestServices _svc;
    private readonly long _supplier;

    public LedgerPosterTests()
    {
        _user = _db.AddUser(UserRole.SalesPerson);
        _svc = new TestServices(_db, new FakeCurrentUser(_db.AddUser(UserRole.Admin)));
        _supplier = _svc.Suppliers.CreateAsync(SupplierServiceTests.Form()).Result.Value;
    }

    public void Dispose() => _db.Dispose();

    // Posts one voucher in its own transaction, like a service would.
    private async Task<Voucher> PostAsync(params LineDraft[] lines)
    {
        await using var db = _db.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var v = await _svc.Poster.PostAsync(db, new Voucher { Type = VoucherType.Journal, AmountPaise = 100 }, lines, _user);
        await tx.CommitAsync();
        return v;
    }

    private async Task<Voucher> ReverseAsync(long voucherId, string reason = "Entered twice")
    {
        await using var db = _db.NewContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var v = await _svc.Poster.ReverseAsync(db, voucherId, reason, _user);
        await tx.CommitAsync();
        return v;
    }

    private LineDraft SupplierDr(long paise) => LineDraft.Dr(SystemAccounts.SupplierReceivables, paise, "Sale", _supplier);
    private static LineDraft SalesCr(long paise) => LineDraft.Cr(SystemAccounts.WaterSales, paise, "Sale");

    [Fact]
    public async Task FR_LED_01_Unbalanced_Posting_Is_Refused()
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => PostAsync(SupplierDr(100), SalesCr(99)));
        Assert.Contains("do not balance", ex.Message);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(-100, 0)]
    public async Task FR_LED_01_Each_Line_Has_Exactly_One_Positive_Side(long dr, long cr) =>
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PostAsync(new LineDraft(SystemAccounts.Cash, dr, cr, "x"), new LineDraft(SystemAccounts.WaterSales, cr, dr, "x")));

    [Fact]
    public async Task Posting_Outside_A_Transaction_Is_A_Programming_Error()
    {
        await using var db = _db.NewContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _svc.Poster.PostAsync(db, new Voucher { Type = VoucherType.Journal, AmountPaise = 1 }, [SupplierDr(1), SalesCr(1)], _user));
    }

    [Fact]
    public async Task FR_LED_04_Running_Balance_Stored_Per_Account_And_Party()
    {
        await PostAsync(SupplierDr(15000), SalesCr(15000));
        await PostAsync(SupplierDr(10000), SalesCr(10000));
        await PostAsync(LineDraft.Dr(SystemAccounts.Cash, 5000, "Receipt"), LineDraft.Cr(SystemAccounts.SupplierReceivables, 5000, "Receipt", _supplier));

        using var ctx = _db.NewContext();
        var supplierLines = ctx.LedgerEntries.Where(e => e.SupplierId == _supplier).OrderBy(e => e.PostingSeq)
            .Select(e => e.RunningBalancePaise).ToList();
        Assert.Equal([15000L, 25000L, 20000L], supplierLines);
        Assert.Equal(-25000, await LedgerQueries.AccountBalanceAsync(ctx, SystemAccounts.WaterSales));   // credit shown negative
        Assert.Equal(20000, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, _supplier));
    }

    [Fact]
    public async Task FR_LED_07_Vehicle_Balance_Is_A_View_Of_Tagged_Lines()
    {
        var vehicle = (await _svc.Vehicles.CreateAsync(new VehicleForm { VehicleNo = "TN 38 M 3", SupplierId = _supplier, TankCapacityLitres = 12000, Rate = 150 })).Value;
        await PostAsync(LineDraft.Dr(SystemAccounts.SupplierReceivables, 15000, "Sale", _supplier, vehicle: vehicle), SalesCr(15000));
        await PostAsync(SupplierDr(7000), SalesCr(7000));   // not tied to the vehicle

        using var ctx = _db.NewContext();
        var tagged = ctx.LedgerEntries.Single(e => e.VehicleId == vehicle);
        Assert.Equal(15000, tagged.VehicleRunningBalancePaise);
        var (total, vehicles, unallocated) = await LedgerQueries.SupplierSplitAsync(ctx, _supplier);
        Assert.Equal(22000, total);
        Assert.Equal(total, vehicles + unallocated);   // FR-LED-08
    }

    [Fact]
    public async Task FR_INV_01_Numbers_Are_Gapless_Even_After_A_Failed_Save()
    {
        var first = await PostAsync(SupplierDr(100), SalesCr(100));

        // A save that fails after taking a number: the rollback gives the number back.
        await using (var db = _db.NewContext())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await _svc.Poster.PostAsync(db, new Voucher { Type = VoucherType.Journal, AmountPaise = 1 }, [SupplierDr(1), SalesCr(1)], _user);
            await tx.RollbackAsync();
        }

        var second = await PostAsync(SupplierDr(100), SalesCr(100));
        Assert.Equal(first.Number + 1, second.Number);
        Assert.Equal($"JV-{second.Number}", second.DisplayNo);
    }

    [Fact]
    public async Task FR_LED_02_Ledger_Lines_Cannot_Be_Updated_Or_Deleted()
    {
        await PostAsync(SupplierDr(100), SalesCr(100));
        using var ctx = _db.NewContext();

        var update = await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("UPDATE LedgerEntries SET DebitPaise = 1"));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM LedgerEntries"));

        Assert.Contains("cannot be changed", update.Message);
        Assert.Contains("cannot be deleted", delete.Message);
    }

    [Fact]
    public async Task FR_LED_09_Reversal_Swaps_Sides_And_Cites_Original()
    {
        var original = await PostAsync(SupplierDr(15000), SalesCr(15000));

        var reversal = await ReverseAsync(original.Id);

        using var ctx = _db.NewContext();
        var lines = ctx.LedgerEntries.Where(e => e.VoucherId == reversal.Id).OrderBy(e => e.LineNo).ToList();
        Assert.Equal(15000, lines[0].CreditPaise);   // was a debit
        Assert.Equal(15000, lines[1].DebitPaise);    // was a credit
        Assert.All(lines, l => Assert.NotNull(l.ReversesEntryId));
        Assert.All(lines, l => Assert.Contains(original.DisplayNo, l.Particulars));
        Assert.Equal(VoucherStatus.Cancelled, ctx.Vouchers.Single(v => v.Id == original.Id).Status);
        Assert.Equal(0, await LedgerQueries.PartyBalanceAsync(ctx, PartyType.Supplier, _supplier));
    }

    [Fact]
    public async Task A_Voucher_Can_Be_Reversed_Only_Once_And_Needs_A_Reason()
    {
        var original = await PostAsync(SupplierDr(100), SalesCr(100));

        await Assert.ThrowsAsync<BusinessRuleException>(() => ReverseAsync(original.Id, " "));
        var reversal = await ReverseAsync(original.Id);
        await Assert.ThrowsAsync<BusinessRuleException>(() => ReverseAsync(original.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => ReverseAsync(reversal.Id));
    }

    [Fact]
    public async Task FR_LED_10_Closed_Day_Refuses_Postings_And_Corrections_Keep_Value_Date()
    {
        var original = await PostAsync(SupplierDr(100), SalesCr(100));   // posted on 05/11/2026
        using (var ctx = _db.NewContext())
        {
            ctx.BusinessDays.Single(d => d.Date == new DateOnly(2026, 11, 5)).Status = BusinessDayStatus.Closed;
            ctx.SaveChanges();
        }

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => PostAsync(SupplierDr(1), SalesCr(1)));
        Assert.Contains("already closed", refused.Message);

        _db.Clock.Advance(TimeSpan.FromHours(3));   // 13:00 UTC = 18:30 IST: the next business day
        var reversal = await ReverseAsync(original.Id);

        Assert.Equal(new DateOnly(2026, 11, 6), reversal.VoucherDate);
        Assert.Equal(new DateOnly(2026, 11, 5), reversal.ValueDate);
        using var check = _db.NewContext();
        Assert.All(check.LedgerEntries.Where(e => e.VoucherId == reversal.Id),
            l => Assert.Equal((new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 5)), (l.EntryDate, l.ValueDate)));
    }

    [Theory]
    [InlineData(12, 29, 5)]   // 17:59 IST -> same day
    [InlineData(12, 30, 6)]   // 18:00 IST -> next day
    [InlineData(18, 29, 6)]   // 23:59 IST -> next day
    [InlineData(18, 31, 6)]   // 00:01 IST on the 6th -> 6th
    public void FR_DSP_08_Business_Day_Runs_6pm_To_6pm(int utcHour, int utcMinute, int expectedDay) =>
        Assert.Equal(new DateOnly(2026, 11, expectedDay),
            BusinessCalendar.DateFor(new DateTime(2026, 11, 5, utcHour, utcMinute, 0, DateTimeKind.Utc)));

    [Fact]
    public async Task New_Day_Opens_With_Cash_Counted_At_Last_Close()
    {
        using (var ctx = _db.NewContext())
        {
            ctx.BusinessDays.Add(new BusinessDay { Date = new DateOnly(2026, 11, 4), Status = BusinessDayStatus.Closed, ActualCashPaise = 500000, Version = 1 });
            ctx.SaveChanges();
        }

        await PostAsync(SupplierDr(100), SalesCr(100));

        using var check = _db.NewContext();
        Assert.Equal(500000, check.BusinessDays.Single(d => d.Date == new DateOnly(2026, 11, 5)).OpeningCashPaise);
    }
}
