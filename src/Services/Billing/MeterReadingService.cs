using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Billing;

// Correcting the meter readings of a saved invoice (client requirement, Nov 2026).
//
// Each meter's readings form a chain: one trip's End reading is the next trip's Start reading.
// So a correction on one invoice also corrects its neighbours on the same meter:
//   - a changed End reading becomes the Start reading of the NEXT invoice that used that meter;
//   - a changed Start reading becomes the End reading of the PREVIOUS invoice that used that meter;
//   - a meter taken off an invoice joins its previous and next invoices together.
// Trip order = invoice number, where a corrected copy (Edit) takes the place of the invoice it replaced.
// Cancelled invoices are not part of the chain. Readings never change any amount (billing is per trip),
// so nothing is posted to the ledger; every changed invoice is recorded in the Activity Log.

public sealed class MeterEdit
{
    public long? EditedBySalesPersonId { get; set; }   // Sales login: who is editing (Admin login: "Admin")
    public decimal? Meter1Start { get; set; }
    public decimal? Meter1End { get; set; }
    public decimal? Meter2Start { get; set; }
    public decimal? Meter2End { get; set; }
    public decimal? Meter3Start { get; set; }
    public decimal? Meter3End { get; set; }
    public string? Reason { get; set; }
    public int Version { get; set; }
}

// One reading that changed on ANOTHER invoice because of the correction, for the screen message.
public sealed record MeterChange(string InvoiceNo, int Meter, string Reading, long? Before, long? After)
{
    public string Text => $"invoice {InvoiceNo}, Meter {Meter} {Reading}: {Show(Before)} → {Show(After)}";
    private static string Show(long? t) => t is { } v ? Volume.Format(v) : "none";
}

// Reading and writing one meter's readings on an invoice, and the order of trips.
public static class MeterChain
{
    public static long? Start(Invoice i, int m) => m switch { 1 => i.Meter1StartTenths, 2 => i.Meter2StartTenths, _ => i.Meter3StartTenths };
    public static long? End(Invoice i, int m) => m switch { 1 => i.Meter1EndTenths, 2 => i.Meter2EndTenths, _ => i.Meter3EndTenths };

    public static void SetStart(Invoice i, int m, long? v)
    {
        if (m == 1) i.Meter1StartTenths = v; else if (m == 2) i.Meter2StartTenths = v; else i.Meter3StartTenths = v;
    }

    public static void SetEnd(Invoice i, int m, long? v)
    {
        if (m == 1) i.Meter1EndTenths = v; else if (m == 2) i.Meter2EndTenths = v; else i.Meter3EndTenths = v;
    }

    public static long VolumeOf(Invoice i) =>
        Enumerable.Range(1, 3).Sum(m => End(i, m) - Start(i, m) ?? 0);

    // Trip order of every invoice: (number of the first invoice of its edit chain, own number).
    // A corrected copy keeps the place of the invoice it replaced.
    public static async Task<Dictionary<long, (long Trip, long No)>> OrderAsync(AppDbContext db)
    {
        var all = await db.Invoices.Select(i => new { i.Id, i.InvoiceNo, i.ReplacesInvoiceId }).ToListAsync();
        var byId = all.ToDictionary(i => i.Id);
        var order = new Dictionary<long, (long, long)>();
        foreach (var i in all)
        {
            var root = i;
            var guard = 0;
            while (root.ReplacesInvoiceId is { } previous && byId.TryGetValue(previous, out var p) && guard++ < 1000) root = p;
            order[i.Id] = (root.InvoiceNo, i.InvoiceNo);
        }
        return order;
    }
}

public class MeterReadingService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<Result<IReadOnlyList<MeterChange>>> UpdateAsync(long invoiceId, MeterEdit edit)
    {
        var me = await currentUser.RequireAsync(Permission.InvoiceCancel);   // same right as Edit
        if (string.IsNullOrWhiteSpace(edit.Reason)) return Fail("Give a reason for changing the meter readings.");
        var typed = TripDetails.Meters(edit.Meter1Start, edit.Meter1End, edit.Meter2Start, edit.Meter2End, edit.Meter3Start, edit.Meter3End);
        if (!typed.Ok) return Fail(typed.Error!);
        var reason = edit.Reason.Trim();

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var target = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId);
        if (target is null) return Fail("Invoice not found.");
        if (target.Version != edit.Version) return Fail(Messages.ChangedBySomeoneElse);
        if (target.Status == InvoiceStatus.Cancelled) return Fail($"Invoice {target.DisplayNo} is cancelled; its meter readings cannot be changed.");

        string editor = InvoiceService.AdminName;
        if (me.Role != UserRole.Admin)
        {
            var person = await db.SalesPersons.SingleOrDefaultAsync(p => p.Id == edit.EditedBySalesPersonId && p.Status == RecordStatus.Active);
            if (person is null) return Fail("Choose which salesperson is editing.");
            editor = person.Name;
        }

        var wanted = typed.Value!;
        long? NewStart(int m) => m switch { 1 => wanted.Start1, 2 => wanted.Start2, _ => wanted.Start3 };
        long? NewEnd(int m) => m switch { 1 => wanted.End1, 2 => wanted.End2, _ => wanted.End3 };
        if (Enumerable.Range(1, 3).All(m => NewStart(m) == MeterChain.Start(target, m) && NewEnd(m) == MeterChain.End(target, m)))
            return Fail("Nothing was changed.");

        var order = await MeterChain.OrderAsync(db);
        var key = order[target.Id];
        var others = await db.Invoices.Where(i => i.Id != target.Id && i.Status != InvoiceStatus.Cancelled).ToListAsync();
        var before = Snapshot(target);
        var changes = new List<MeterChange>();
        var touched = new Dictionary<long, (Invoice Invoice, object Before)>();

        void Touch(Invoice i) { if (!touched.ContainsKey(i.Id)) touched[i.Id] = (i, Snapshot(i)); }

        for (var m = 1; m <= 3; m++)
        {
            var (oldStart, oldEnd, newStart, newEnd) = (MeterChain.Start(target, m), MeterChain.End(target, m), NewStart(m), NewEnd(m));
            if (oldStart == newStart && oldEnd == newEnd) continue;

            var chain = others.Where(i => MeterChain.Start(i, m) is not null && MeterChain.End(i, m) is not null).OrderBy(i => order[i.Id]).ToList();
            var prev = chain.LastOrDefault(i => order[i.Id].CompareTo(key) < 0);
            var next = chain.FirstOrDefault(i => order[i.Id].CompareTo(key) > 0);

            if (newStart is { } s && newEnd is { } e)
            {
                // The previous trip on this meter ended where this one starts.
                if (prev is not null && s != oldStart && MeterChain.End(prev, m) != s)
                {
                    if (s < MeterChain.Start(prev, m))
                        return Fail($"Meter {m}: the start reading {Volume.Format(s)} is below the start reading of invoice {prev.DisplayNo} ({Volume.Format(MeterChain.Start(prev, m)!.Value)}).");
                    Touch(prev);
                    changes.Add(new(prev.DisplayNo, m, "end", MeterChain.End(prev, m), s));
                    MeterChain.SetEnd(prev, m, s);
                }
                // The next trip on this meter starts where this one ends.
                if (next is not null && e != oldEnd && MeterChain.Start(next, m) != e)
                {
                    if (e > MeterChain.End(next, m))
                        return Fail($"Meter {m}: the end reading {Volume.Format(e)} is above the end reading of invoice {next.DisplayNo} ({Volume.Format(MeterChain.End(next, m)!.Value)}).");
                    Touch(next);
                    changes.Add(new(next.DisplayNo, m, "start", MeterChain.Start(next, m), e));
                    MeterChain.SetStart(next, m, e);
                }
            }
            else if (prev is not null && next is not null && MeterChain.Start(next, m) != MeterChain.End(prev, m))
            {
                // The meter is taken off this invoice: the next trip now starts where the previous one ended.
                var joined = MeterChain.End(prev, m)!.Value;
                if (joined > MeterChain.End(next, m))
                    return Fail($"Meter {m} cannot be taken off: invoice {next.DisplayNo} would end below its start.");
                Touch(next);
                changes.Add(new(next.DisplayNo, m, "start", MeterChain.Start(next, m), joined));
                MeterChain.SetStart(next, m, joined);
            }

            MeterChain.SetStart(target, m, newStart);
            MeterChain.SetEnd(target, m, newEnd);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        target.VolumeTenths = MeterChain.VolumeOf(target);
        target.Version++;
        ActivityLogger.Add(db, now, me, ActivityCategory.Billing, "INVOICE_METERS_CHANGED", nameof(Invoice), target.Id,
            before, Snapshot(target), reason, doneBy: editor);
        foreach (var (inv, old) in touched.Values)
        {
            inv.VolumeTenths = MeterChain.VolumeOf(inv);
            inv.Version++;
            ActivityLogger.Add(db, now, me, ActivityCategory.Billing, "INVOICE_METERS_ADJUSTED", nameof(Invoice), inv.Id,
                old, Snapshot(inv), $"Follows the meter correction on invoice {target.DisplayNo}: {reason}", doneBy: editor);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<IReadOnlyList<MeterChange>>.Success(changes);
    }

    private static Result<IReadOnlyList<MeterChange>> Fail(string error) => Result<IReadOnlyList<MeterChange>>.Fail(error);

    private static object Snapshot(Invoice i) => new
    {
        i.DisplayNo,
        Meter1 = Pair(i.Meter1StartTenths, i.Meter1EndTenths),
        Meter2 = Pair(i.Meter2StartTenths, i.Meter2EndTenths),
        Meter3 = Pair(i.Meter3StartTenths, i.Meter3EndTenths),
    };

    private static string? Pair(long? s, long? e) => s is null && e is null ? null : $"{(s is { } a ? Volume.Format(a) : "-")} to {(e is { } b ? Volume.Format(b) : "-")}";
}
