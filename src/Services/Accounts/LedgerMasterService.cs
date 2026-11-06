using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

// Ledger Master rules: users may create, rename or deactivate expense ledgers; system ledgers are
// fixed; a ledger with entries cannot be deleted. Also the Admin-only "first invoice number" setting,
// allowed only before a series is used, so invoice numbers stay without gaps.

public sealed record LedgerRow(long Id, string Name, AccountType Type, bool IsSystem, RecordStatus Status, long BalancePaise, bool HasEntries, int Version);

public sealed record SeriesRow(string Code, string Prefix, long NextNo, bool CanChange);

public sealed class LedgerForm
{
    [Required(ErrorMessage = "Ledger name is required."), StringLength(80)]
    public string Name { get; set; } = "";
    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public int Version { get; set; }
}

// Ledger Master (FR-TXN-13): create expense ledgers. System ledgers cannot be changed;
// ledgers with entries cannot be deleted.
public class LedgerMasterService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<IReadOnlyList<LedgerRow>> ListAsync()
    {
        await currentUser.RequireAsync(Permission.LedgerManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var accounts = await db.Accounts.OrderBy(a => a.Type).ThenBy(a => a.Name).ToListAsync();
        // Account totals across all parties: sum of debits minus credits.
        var totals = await db.LedgerEntries.GroupBy(e => e.AccountId)
            .Select(g => new { g.Key, Balance = g.Sum(e => e.DebitPaise - e.CreditPaise) })
            .ToDictionaryAsync(x => x.Key, x => x.Balance);
        return accounts.Select(a => new LedgerRow(a.Id, a.Name, a.Type, a.IsSystem, a.Status,
            totals.GetValueOrDefault(a.Id), totals.ContainsKey(a.Id), a.Version)).ToList();
    }

    public async Task<Result<long>> CreateAsync(LedgerForm form)
    {
        var me = await currentUser.RequireAsync(Permission.LedgerManage);
        if (FormValidator.FirstError(form) is { } error) return Result<long>.Fail(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var name = PartyRules.Tidy(form.Name);
        var key = Supplier.NormalizeName(name);
        if (await db.Accounts.AnyAsync(a => a.NormalizedName == key)) return Result<long>.Fail("A ledger with this name already exists.");

        // Only expense ledgers can be created (e.g. Vehicle Maintenance, Fuel Expense).
        var account = new Account { Name = name, NormalizedName = key, Type = AccountType.Expense, Status = form.Status };
        await using var tx = await db.Database.BeginTransactionAsync();
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "LEDGER_CREATED", nameof(Account), account.Id, after: new { account.Name });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(account.Id);
    }

    public async Task<Result> UpdateAsync(long id, LedgerForm form)
    {
        var me = await currentUser.RequireAsync(Permission.LedgerManage);
        if (FormValidator.FirstError(form) is { } error) return Result.Fail(error);
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(id);
        if (account is null) return Result.Fail("Ledger not found.");
        if (account.IsSystem) return Result.Fail("System ledgers cannot be changed.");
        if (account.Version != form.Version) return Result.Fail(Messages.ChangedBySomeoneElse);
        var name = PartyRules.Tidy(form.Name);
        var key = Supplier.NormalizeName(name);
        if (await db.Accounts.AnyAsync(a => a.NormalizedName == key && a.Id != id)) return Result.Fail("A ledger with this name already exists.");

        var before = new { account.Name, Status = account.Status.ToString() };
        account.Name = name;
        account.NormalizedName = key;
        account.Status = form.Status;
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "LEDGER_UPDATED", nameof(Account), id, before,
            new { account.Name, Status = account.Status.ToString() });
        try { await db.SaveChangesAsync(); return Result.Success(); }
        catch (DbUpdateConcurrencyException) { return Result.Fail(Messages.ChangedBySomeoneElse); }
    }

    public async Task<Result> DeleteAsync(long id)
    {
        var me = await currentUser.RequireAsync(Permission.LedgerManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var account = await db.Accounts.FindAsync(id);
        if (account is null) return Result.Fail("Ledger not found.");
        if (account.IsSystem) return Result.Fail("System ledgers cannot be deleted.");
        if (await db.LedgerEntries.AnyAsync(e => e.AccountId == id) || await db.Vouchers.AnyAsync(v => v.PurposeAccountId == id))
            return Result.Fail("This ledger has entries, so it cannot be deleted. Set it to Inactive instead.");
        db.Accounts.Remove(account);
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "LEDGER_DELETED", nameof(Account), id, before: new { account.Name });
        await db.SaveChangesAsync();
        return Result.Success();
    }

    // FR-INV-01 "starting from the agreed number": the Admin may set where a series starts,
    // but only before it is first used, so numbers stay gapless.
    public async Task<IReadOnlyList<SeriesRow>> SeriesAsync()
    {
        await currentUser.RequireAsync(Permission.LedgerManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var used = await db.Vouchers.Select(v => v.Type).Distinct().ToListAsync();
        var series = await db.NumberSeries.OrderBy(s => s.Code).ToListAsync();
        return series.Select(s => new SeriesRow(s.Code, s.Prefix, s.NextNo, !used.Contains(TypeOf(s.Code)))).ToList();
    }

    public async Task<Result> SetSeriesStartAsync(string code, long start)
    {
        var me = await currentUser.RequireAsync(Permission.UsersManage);   // Admin only
        if (start < 1 || start > 999_999_999) return Result.Fail("Starting number must be between 1 and 99,99,99,999.");
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var series = await db.NumberSeries.SingleOrDefaultAsync(s => s.Code == code);
        if (series is null) return Result.Fail("Unknown series.");
        if (await db.Vouchers.AnyAsync(v => v.Type == TypeOf(code)))
            return Result.Fail("This series is already in use; its numbers cannot be changed.");
        var before = series.NextNo;
        series.NextNo = start;
        ActivityLogger.Add(db, Now, me, ActivityCategory.Admin, "SERIES_START_SET", "NumberSeries", null,
            new { Code = code, NextNo = before }, new { Code = code, NextNo = start });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result.Success();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static VoucherType TypeOf(string code) => code switch
    {
        "INVOICE" => VoucherType.Sales,
        "RECEIPT" => VoucherType.Receipt,
        "PAYMENT" => VoucherType.Payment,
        "JOURNAL" => VoucherType.Journal,
        _ => VoucherType.Reversal,
    };
}
