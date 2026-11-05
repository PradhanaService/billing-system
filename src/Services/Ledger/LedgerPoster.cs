using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Ledger;

// One line to post. Exactly one of Debit or Credit is above zero.
public sealed record LineDraft(long AccountId, long DebitPaise, long CreditPaise, string Particulars,
    long? SupplierId = null, long? CustomerId = null, long? VehicleId = null)
{
    public static LineDraft Dr(long account, long paise, string particulars, long? supplier = null, long? customer = null, long? vehicle = null) =>
        new(account, paise, 0, particulars, supplier, customer, vehicle);

    public static LineDraft Cr(long account, long paise, string particulars, long? supplier = null, long? customer = null, long? vehicle = null) =>
        new(account, 0, paise, particulars, supplier, customer, vehicle);
}

// The accounting engine. Every money event goes through PostAsync or ReverseAsync.
// It saves its lines straight away (so the next posting sees the new balances) but never
// commits: the caller's transaction does. So the business record (invoice, receipt...) and
// its ledger lines are kept together or not at all (FR-INV-07).
public class LedgerPoster(TimeProvider clock, BusinessCalendar calendar)
{
    private static readonly Dictionary<VoucherType, string> SeriesFor = new()
    {
        [VoucherType.Sales] = "INVOICE",
        [VoucherType.Receipt] = "RECEIPT",
        [VoucherType.Payment] = "PAYMENT",
        [VoucherType.Journal] = "JOURNAL",
        [VoucherType.Reversal] = "REVERSAL",
    };

    // Fills in number, dates and lines on the voucher and adds it to the context.
    // The caller must already be inside a transaction.
    public async Task<Voucher> PostAsync(AppDbContext db, Voucher voucher, IReadOnlyList<LineDraft> lines, UserInfo user)
    {
        RequireTransaction(db);
        CheckBalanced(lines);

        var today = await BusinessDays.RequireOpenAsync(db, calendar.Today());
        voucher.VoucherDate = today;
        voucher.ValueDate = today;
        await AssignNumberAsync(db, voucher);
        voucher.CreatedByUserId = user.Id;
        voucher.CreatedAtUtc = clock.GetUtcNow().UtcDateTime;

        await AddLinesAsync(db, voucher, lines.Select(l => (l, (long?)null)).ToList(), today, today, user);
        db.Vouchers.Add(voucher);
        await db.SaveChangesAsync();   // inside the caller's transaction: nothing is final until it commits
        return voucher;
    }

    // P11 / FR-LED-09: a new voucher with every line's Dr and Cr swapped. It is entered on today's
    // open business day but keeps the original Value Date, so no closed day is touched (FR-LED-10).
    public async Task<Voucher> ReverseAsync(AppDbContext db, long voucherId, string reason, UserInfo user)
    {
        RequireTransaction(db);
        if (string.IsNullOrWhiteSpace(reason)) throw new BusinessRuleException("A reason is required to cancel.");

        var original = await db.Vouchers.Include(v => v.Lines).SingleOrDefaultAsync(v => v.Id == voucherId)
            ?? throw new BusinessRuleException("Voucher not found.");
        if (original.Status == VoucherStatus.Cancelled) throw new BusinessRuleException($"{original.DisplayNo} is already cancelled.");
        if (original.Type == VoucherType.Reversal) throw new BusinessRuleException("A reversal cannot itself be reversed.");

        var today = await BusinessDays.RequireOpenAsync(db, calendar.Today());
        var reversal = new Voucher
        {
            Type = VoucherType.Reversal,
            PartyType = original.PartyType,
            SupplierId = original.SupplierId,
            CustomerId = original.CustomerId,
            VehicleId = original.VehicleId,
            PurposeAccountId = original.PurposeAccountId,
            Mode = original.Mode,
            AmountPaise = original.AmountPaise,
            ReversesVoucherId = original.Id,
            Narration = Trim($"Reversal of {original.DisplayNo}: {reason.Trim()}", 300),
            VoucherDate = today,
            ValueDate = original.ValueDate,
            CreatedByUserId = user.Id,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        await AssignNumberAsync(db, reversal);

        var swapped = original.Lines.OrderBy(l => l.LineNo)
            .Select(l => (new LineDraft(l.AccountId, l.CreditPaise, l.DebitPaise,
                Trim($"Reversal of {original.DisplayNo}: {l.Particulars}", 200), l.SupplierId, l.CustomerId, l.VehicleId), (long?)l.Id))
            .ToList();
        await AddLinesAsync(db, reversal, swapped, today, original.ValueDate, user);

        original.Status = VoucherStatus.Cancelled;
        original.CancelReason = Trim(reason.Trim(), 300);
        db.Vouchers.Add(reversal);
        await db.SaveChangesAsync();
        return reversal;
    }

    // FR-LED-01: total debit must equal total credit, and each line has exactly one side.
    public static void CheckBalanced(IReadOnlyList<LineDraft> lines)
    {
        if (lines.Count < 2) throw new BusinessRuleException("A posting needs at least two lines.");
        foreach (var l in lines)
        {
            var oneSide = (l.DebitPaise > 0 && l.CreditPaise == 0) || (l.CreditPaise > 0 && l.DebitPaise == 0);
            if (!oneSide) throw new BusinessRuleException("Each ledger line must have either a debit or a credit above zero.");
            if (l.SupplierId is not null && l.CustomerId is not null) throw new BusinessRuleException("A line can carry only one party.");
        }
        if (lines.Sum(l => l.DebitPaise) != lines.Sum(l => l.CreditPaise))
            throw new BusinessRuleException("Debits and credits do not balance.");
    }

    private async Task AddLinesAsync(AppDbContext db, Voucher voucher, List<(LineDraft Line, long? Reverses)> lines,
        DateOnly entryDate, DateOnly valueDate, UserInfo user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var nextSeq = (await db.LedgerEntries.MaxAsync(e => (long?)e.PostingSeq) ?? 0) + 1;

        // Balances already moved by earlier lines of this same voucher (not yet saved).
        var partyBalances = new Dictionary<(long, long?, long?), long>();
        var vehicleBalances = new Dictionary<(long, long?, long?, long), long>();

        var lineNo = 1;
        foreach (var (l, reverses) in lines)
        {
            var key = (l.AccountId, l.SupplierId, l.CustomerId);
            if (!partyBalances.TryGetValue(key, out var balance))
                balance = await LedgerQueries.LastRunningBalanceAsync(db, l.AccountId, l.SupplierId, l.CustomerId);
            balance += l.DebitPaise - l.CreditPaise;
            partyBalances[key] = balance;

            long? vehicleBalance = null;
            if (l.VehicleId is { } vehicleId)
            {
                var vkey = (l.AccountId, l.SupplierId, l.CustomerId, vehicleId);
                if (!vehicleBalances.TryGetValue(vkey, out var vb))
                    vb = await LedgerQueries.LastVehicleRunningBalanceAsync(db, l.AccountId, l.SupplierId, l.CustomerId, vehicleId);
                vb += l.DebitPaise - l.CreditPaise;
                vehicleBalances[vkey] = vb;
                vehicleBalance = vb;
            }

            voucher.Lines.Add(new LedgerEntry
            {
                PostingSeq = nextSeq++,
                LineNo = lineNo++,
                AccountId = l.AccountId,
                SupplierId = l.SupplierId,
                CustomerId = l.CustomerId,
                VehicleId = l.VehicleId,
                EntryDate = entryDate,
                ValueDate = valueDate,
                Particulars = Trim(l.Particulars, 200),
                ReferenceNo = voucher.DisplayNo,
                DebitPaise = l.DebitPaise,
                CreditPaise = l.CreditPaise,
                RunningBalancePaise = balance,
                VehicleRunningBalancePaise = vehicleBalance,
                ReversesEntryId = reverses,
                CreatedByUserId = user.Id,
                CreatedAtUtc = now,
            });
        }
    }

    private static async Task AssignNumberAsync(AppDbContext db, Voucher voucher)
    {
        var series = await db.NumberSeries.SingleAsync(s => s.Code == SeriesFor[voucher.Type]);
        voucher.Number = series.NextNo;
        voucher.DisplayNo = series.Prefix + series.NextNo;
        series.NextNo++;
    }

    // Safety net: posting outside a transaction could save half a voucher.
    private static void RequireTransaction(AppDbContext db)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Ledger postings must run inside a database transaction.");
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
