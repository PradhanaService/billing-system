using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Accounts;

// Cash movement for a period, read from the Cash in Hand ledger (so it always equals the ledger).
// Closing = Opening + Cash Sales + cash Receipts - cash Payments (+ closing adjustments) (FR-INV-16).
public sealed record CashSummary(long OpeningPaise, long CashSalesPaise, long CashReceiptsPaise, long CashPaymentsPaise,
    long AdjustmentsPaise, long ClosingPaise);

// One row of the day's transactions (invoices, receipts, payments).
public sealed record DayTransaction(string Kind, string DisplayNo, string Party, string Mode, long AmountPaise, bool Cancelled, long? InvoiceId);

public sealed record DayTotals(long TotalSalesPaise, long CashSalesPaise, long CreditSalesPaise, int Trips, int InvoiceCount,
    long ReceiptsPaise, long PaymentsPaise);

public sealed record DayBlocker(string Message, string Link);

public sealed record DayView(DateOnly Date, BusinessDayStatus Status, long OpeningCashPaise, CashSummary Cash, DayTotals Totals,
    IReadOnlyList<DayTransaction> Transactions, IReadOnlyList<DayBlocker> Blockers,
    long? ActualCashPaise, long? CashDifferencePaise, string? DifferenceReason, string? ClosedBy, DateTime? ClosedAtUtc, int Version);

public class DaySalesService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock,
    LedgerPoster poster, BusinessCalendar calendar)
{
    public DateOnly Today() => calendar.Today();

    // View Day Sales (FR-DAY-02): Day Opening, Daily Transactions, Day Closing.
    public async Task<DayView> GetDayAsync(DateOnly date)
    {
        await currentUser.RequireAsync(Permission.DaySalesView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var day = await db.BusinessDays.FindAsync(date);
        var cash = await CashSummaryAsync(db, date, date);
        var closedBy = day?.ClosedByUserId is { } uid ? await db.Users.Where(u => u.Id == uid).Select(u => u.FullName).SingleOrDefaultAsync() : null;
        return new DayView(date, day?.Status ?? BusinessDayStatus.Open,
            day?.OpeningCashPaise ?? cash.OpeningPaise, cash, await TotalsAsync(db, date, date),
            await TransactionsAsync(db, date), await BlockersAsync(db, date),
            day?.ActualCashPaise, day?.CashDifferencePaise, day?.DifferenceReason, closedBy, day?.ClosedAtUtc, day?.Version ?? 0);
    }

    // FR-DAY-04/05: count the cash, post any difference (P8 short / P9 over), lock the day,
    // and carry the counted cash to the next day's opening.
    public async Task<Result> CloseDayAsync(DateOnly date, decimal actualCash, string? reason, int version)
    {
        var me = await currentUser.RequireAsync(Permission.DayClose);
        if (actualCash < 0 || actualCash * 100 != decimal.Truncate(actualCash * 100))
            return Result.Fail("Enter the counted cash in rupees, with at most 2 decimals.");
        var actual = Money.FromRupees(actualCash);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (date > calendar.Today()) return Result.Fail("A future business day cannot be closed.");
        var day = await BusinessDays.GetOrCreateAsync(db, date);
        if (day.Status == BusinessDayStatus.Closed) return Result.Fail($"{IndianTime.FormatDate(date)} is already closed.");
        if (day.Version != version && version != 0) return Result.Fail(Messages.ChangedBySomeoneElse);

        var blockers = await BlockersAsync(db, date);
        if (blockers.Count > 0) return Result.Fail("Day closing is blocked: " + string.Join(" ", blockers.Select(b => b.Message)));

        var expected = (await CashSummaryAsync(db, date, date)).ClosingPaise;
        var difference = actual - expected;
        if (difference != 0 && string.IsNullOrWhiteSpace(reason))
            return Result.Fail("The counted cash differs from the closing balance. Give a reason.");

        try
        {
            if (difference != 0)
            {
                // P8 (short): Dr Cash Over/Short, Cr Cash.  P9 (over): Dr Cash, Cr Cash Over/Short.
                var amount = Math.Abs(difference);
                var text = $"Cash {(difference < 0 ? "short" : "over")} at closing {IndianTime.FormatDate(date)}: {reason!.Trim()}";
                LineDraft[] lines = difference < 0
                    ? [LineDraft.Dr(SystemAccounts.CashOverShort, amount, text), LineDraft.Cr(SystemAccounts.Cash, amount, text)]
                    : [LineDraft.Dr(SystemAccounts.Cash, amount, text), LineDraft.Cr(SystemAccounts.CashOverShort, amount, text)];
                await poster.PostAsync(db, new Voucher { Type = VoucherType.Journal, AmountPaise = amount, Narration = text }, lines, me, onDate: date);
            }

            day.ExpectedClosingCashPaise = expected;
            day.ActualCashPaise = actual;
            day.CashDifferencePaise = difference;
            day.DifferenceReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            day.Status = BusinessDayStatus.Closed;
            day.ClosedByUserId = me.Id;
            day.ClosedAtUtc = clock.GetUtcNow().UtcDateTime;

            // FR-DAY-05: the next day opens with the counted cash. (Its row may already exist if
            // sales started after 6 PM before this day was closed.)
            var next = await db.BusinessDays.Where(d => d.Date > date).OrderBy(d => d.Date).FirstOrDefaultAsync();
            if (next is not null) next.OpeningCashPaise = actual;

            ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, me, ActivityCategory.Day, "DAY_CLOSED", "BusinessDay", null,
                after: new { Date = IndianTime.FormatDate(date), ExpectedPaise = expected, ActualPaise = actual, DifferencePaise = difference },
                reason: day.DifferenceReason);
            await db.SaveChangesAsync();
        }
        catch (BusinessRuleException ex)
        {
            return Result.Fail(ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Fail(Messages.ChangedBySomeoneElse);
        }
        await tx.CommitAsync();
        return Result.Success();
    }

    // Invoice Display (FR-INV-15/16): everything in a period with Cash/Credit/Receipt/Payment totals.
    public async Task<(CashSummary Cash, DayTotals Totals, IReadOnlyList<DayTransaction> Transactions)> PeriodAsync(DateOnly fromDate, DateOnly toDate)
    {
        await currentUser.RequireAsync(Permission.DaySalesView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var list = new List<DayTransaction>();
        for (var d = fromDate; d <= toDate && list.Count < 5000; d = d.AddDays(1)) list.AddRange(await TransactionsAsync(db, d));
        return (await CashSummaryAsync(db, fromDate, toDate), await TotalsAsync(db, fromDate, toDate), list);
    }

    // ---- shared calculations ----

    public static async Task<CashSummary> CashSummaryAsync(AppDbContext db, DateOnly fromDate, DateOnly toDate)
    {
        var cashLines = db.LedgerEntries.Where(e => e.AccountId == SystemAccounts.Cash);
        var opening = await cashLines.Where(e => e.EntryDate < fromDate).SumAsync(e => e.DebitPaise - e.CreditPaise);

        // Each cash line counted under the kind of the voucher it belongs to; a reversal counts
        // under the kind of the voucher it reverses (a cancelled cash sale reduces cash sales).
        var rows = await (from e in cashLines
                          where e.EntryDate >= fromDate && e.EntryDate <= toDate
                          join v in db.Vouchers on e.VoucherId equals v.Id
                          join o in db.Vouchers on v.ReversesVoucherId equals o.Id into oj
                          from o in oj.DefaultIfEmpty()
                          select new { Kind = o != null ? o.Type : v.Type, Net = e.DebitPaise - e.CreditPaise })
                         .ToListAsync();
        long Sum(VoucherType t) => rows.Where(r => r.Kind == t).Sum(r => r.Net);
        var sales = Sum(VoucherType.Sales);
        var receipts = Sum(VoucherType.Receipt);
        var payments = -Sum(VoucherType.Payment);
        var adjustments = Sum(VoucherType.Journal);
        return new CashSummary(opening, sales, receipts, payments, adjustments, opening + sales + receipts - payments + adjustments);
    }

    private static async Task<DayTotals> TotalsAsync(AppDbContext db, DateOnly fromDate, DateOnly toDate)
    {
        var invoices = await db.Invoices.Where(i => i.InvoiceDate >= fromDate && i.InvoiceDate <= toDate && i.Status != InvoiceStatus.Cancelled)
            .Select(i => new { i.PaymentMode, i.AmountPaise, i.Trips }).ToListAsync();
        var vouchers = await db.Vouchers.Where(v => v.VoucherDate >= fromDate && v.VoucherDate <= toDate && v.Status == VoucherStatus.Posted
                && (v.Type == VoucherType.Receipt || v.Type == VoucherType.Payment))
            .Select(v => new { v.Type, v.AmountPaise }).ToListAsync();
        var cash = invoices.Where(i => i.PaymentMode == InvoicePaymentMode.Cash).Sum(i => i.AmountPaise);
        var credit = invoices.Where(i => i.PaymentMode == InvoicePaymentMode.Credit).Sum(i => i.AmountPaise);
        return new DayTotals(cash + credit, cash, credit, invoices.Sum(i => i.Trips), invoices.Count,
            vouchers.Where(v => v.Type == VoucherType.Receipt).Sum(v => v.AmountPaise),
            vouchers.Where(v => v.Type == VoucherType.Payment).Sum(v => v.AmountPaise));
    }

    private static async Task<List<DayTransaction>> TransactionsAsync(AppDbContext db, DateOnly date)
    {
        var invoices = await (from i in db.Invoices
                              where i.InvoiceDate == date
                              join v in db.Vehicles on i.VehicleId equals v.Id
                              join s in db.Suppliers on i.SupplierId equals s.Id
                              join c in db.Customers on i.CustomerId equals c.Id into cj
                              from c in cj.DefaultIfEmpty()
                              orderby i.InvoiceNo
                              select new DayTransaction("Invoice", i.DisplayNo, (c == null ? s.Name : c.Name) + " · " + v.VehicleNo,
                                  i.PaymentMode.ToString(), i.AmountPaise, i.Status == InvoiceStatus.Cancelled, i.Id)).ToListAsync();
        var vouchers = await (from v in db.Vouchers
                              where v.VoucherDate == date && (v.Type == VoucherType.Receipt || v.Type == VoucherType.Payment)
                              join s in db.Suppliers on v.SupplierId equals s.Id into sj
                              from s in sj.DefaultIfEmpty()
                              join c in db.Customers on v.CustomerId equals c.Id into cj
                              from c in cj.DefaultIfEmpty()
                              join a in db.Accounts on v.PurposeAccountId equals a.Id into aj
                              from a in aj.DefaultIfEmpty()
                              orderby v.Type, v.Number
                              select new DayTransaction(v.Type == VoucherType.Receipt ? "Receipt" : "Payment", v.DisplayNo,
                                  s != null ? s.Name : c != null ? c.Name : (a != null ? a.Name : "") + (v.PayTo != null ? " · " + v.PayTo : ""),
                                  v.Mode!.Value.ToString(), v.AmountPaise, v.Status == VoucherStatus.Cancelled, null)).ToListAsync();
        return [.. invoices, .. vouchers];
    }

    // FR-DAY-03: what stops this day being closed. Days close in order, so an earlier open
    // day that has entries blocks. (Meter fill blockers apply only if meters are added later.)
    private static async Task<List<DayBlocker>> BlockersAsync(AppDbContext db, DateOnly date)
    {
        var earlier = await db.BusinessDays.Where(d => d.Date < date && d.Status == BusinessDayStatus.Open)
            .OrderBy(d => d.Date).Select(d => d.Date).ToListAsync();
        return earlier.Select(d => new DayBlocker($"{IndianTime.FormatDate(d)} is not closed yet. Close it first.",
            $"/day-sales?date={d:yyyy-MM-dd}")).ToList();
    }
}
