using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Billing;

// Meter readings (client rules, Nov 2026).
//
// Each meter's readings form a chain: one trip's End reading is the next trip's Start reading.
//   - The Start reading is never typed: it is the End reading of the previous trip on that meter.
//     It is typed only for a meter's very first trip (there is no previous reading).
//   - Only End readings are typed or corrected. A corrected End becomes the Start of the next trip
//     on that meter. A wrong Start is fixed by correcting the previous trip's End.
//   - An empty End means the meter was not used on that trip (its Start is cleared too); the next
//     trip then starts where the previous one ended.
//   - Meter replaced or reset (the dial starts again): only the Admin may type a new Start, with a reason.
// Trip order = invoice number, where a corrected copy (Edit) takes the place of the invoice it replaced.
// Cancelled invoices are not part of the chain. Readings never change any amount (billing is per trip),
// so nothing is posted to the ledger; every changed invoice is recorded in the Activity Log.

public sealed class MeterEdit
{
    public long? EditedBySalesPersonId { get; set; }   // Sales login: who is editing (Admin login: "Admin")
    public decimal? Meter1Start { get; set; }          // only used for a meter's first trip, or when MeterReplaced
    public decimal? Meter1End { get; set; }
    public decimal? Meter2Start { get; set; }
    public decimal? Meter2End { get; set; }
    public decimal? Meter3Start { get; set; }
    public decimal? Meter3End { get; set; }
    public bool MeterReplaced { get; set; }            // Admin only: the Start readings are taken as typed
    public string? Reason { get; set; }
    public int Version { get; set; }

    public decimal? Start(int m) => m switch { 1 => Meter1Start, 2 => Meter2Start, _ => Meter3Start };
    public decimal? End(int m) => m switch { 1 => Meter1End, 2 => Meter2End, _ => Meter3End };
}

// One reading that changed on ANOTHER invoice because of a correction, for the screen message.
public sealed record MeterChange(string InvoiceNo, int Meter, string Reading, long? Before, long? After)
{
    public string Text => $"invoice {InvoiceNo}, Meter {Meter} {Reading}: {Show(Before)} → {Show(After)}";
    private static string Show(long? t) => t is { } v ? Volume.Format(v) : "none";
}

public sealed record MeterOutcome(bool Changed, IReadOnlyList<MeterChange> Others);

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

    public static long VolumeOf(Invoice i) => Enumerable.Range(1, 3).Sum(m => End(i, m) - Start(i, m) ?? 0);

    // Trip order of every invoice: (number of the first invoice of its edit chain, own number).
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

    // The last trip on each meter (index 1..3): the reading a new invoice starts from, and its invoice.
    public static async Task<(long? End, string? InvoiceNo)[]> LastEndsAsync(AppDbContext db)
    {
        var order = await OrderAsync(db);
        var active = await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled
                && (i.Meter1EndTenths != null || i.Meter2EndTenths != null || i.Meter3EndTenths != null)).ToListAsync();
        var result = new (long? End, string? InvoiceNo)[4];
        for (var m = 1; m <= 3; m++)
        {
            var last = active.Where(i => End(i, m) is not null).OrderBy(i => order[i.Id]).LastOrDefault();
            result[m] = (last is null ? null : End(last, m), last?.DisplayNo);
        }
        return result;
    }

    // A typed reading in kL (one decimal) as tenths; null when empty.
    public static Result<long?> Tenths(decimal? kl, int m)
    {
        if (kl is null) return Result<long?>.Success(null);
        if (kl < 0) return Result<long?>.Fail($"Meter {m}: readings cannot be negative.");
        try { return Result<long?>.Success(Volume.FromKilolitres(kl.Value)); }
        catch (ArgumentException) { return Result<long?>.Fail($"Meter {m}: readings have one decimal place at most (e.g. 28451.2)."); }
    }

    public static string LockedStart(int m, string? previousNo) =>
        $"Meter {m}: the start reading cannot be changed. It is the end reading of the previous trip"
        + (previousNo is null ? "" : $" (invoice {previousNo})") + "; correct that invoice's End reading instead.";
}

public class MeterReadingService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    // "Edit meter readings" on a saved invoice (Sales Invoice screen).
    public async Task<Result<IReadOnlyList<MeterChange>>> UpdateAsync(long invoiceId, MeterEdit edit)
    {
        var me = await currentUser.RequireAsync(Permission.InvoiceCancel);   // same right as Edit
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var target = await db.Invoices.SingleOrDefaultAsync(i => i.Id == invoiceId);
        if (target is null) return Fail("Invoice not found.");
        if (target.Version != edit.Version) return Fail(Messages.ChangedBySomeoneElse);
        if (target.Status == InvoiceStatus.Cancelled) return Fail($"Invoice {target.DisplayNo} is cancelled; its meter readings cannot be changed.");
        var editor = await EditorAsync(db, me, edit.EditedBySalesPersonId);
        if (editor is null) return Fail("Choose which salesperson is editing.");

        var outcome = await ApplyAsync(db, target, edit, me, editor, clock.GetUtcNow().UtcDateTime);
        if (!outcome.Ok) return Fail(outcome.Error!);
        if (!outcome.Value!.Changed) return Fail("Nothing was changed.");
        target.Version++;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<IReadOnlyList<MeterChange>>.Success(outcome.Value.Others);
    }

    // Who is editing: Admin login = "Admin"; Sales login = an active name from Sales Persons (else null).
    public static async Task<string?> EditorAsync(AppDbContext db, UserInfo me, long? salesPersonId)
    {
        if (me.Role == UserRole.Admin) return InvoiceService.AdminName;
        return await db.SalesPersons.Where(p => p.Id == salesPersonId && p.Status == RecordStatus.Active).Select(p => p.Name).SingleOrDefaultAsync();
    }

    // The rule itself, on an invoice already loaded in db (used here and by Invoice Edit, in the caller's
    // transaction). Changes the target and its neighbours and adds the Activity Log rows; the caller saves.
    public static async Task<Result<MeterOutcome>> ApplyAsync(AppDbContext db, Invoice target, MeterEdit edit, UserInfo me, string editor, DateTime now)
    {
        static Result<MeterOutcome> No(string e) => Result<MeterOutcome>.Fail(e);
        if (edit.MeterReplaced && me.Role != UserRole.Admin) return No("Only the Admin can set a new start reading for a replaced or reset meter.");

        var order = await MeterChain.OrderAsync(db);
        var key = order[target.Id];
        var others = await db.Invoices.Where(i => i.Id != target.Id && i.Status != InvoiceStatus.Cancelled).ToListAsync();
        var before = Snapshot(target);
        var changes = new List<MeterChange>();
        var touched = new Dictionary<long, (Invoice Invoice, object Before)>();
        void Touch(Invoice i) { if (!touched.ContainsKey(i.Id)) touched[i.Id] = (i, Snapshot(i)); }
        var changed = false;

        for (var m = 1; m <= 3; m++)
        {
            var end = MeterChain.Tenths(edit.End(m), m);
            if (!end.Ok) return No(end.Error!);
            var typedStart = MeterChain.Tenths(edit.Start(m), m);
            if (!typedStart.Ok) return No(typedStart.Error!);
            var (e, typed) = (end.Value, typedStart.Value);
            var (oldS, oldE) = (MeterChain.Start(target, m), MeterChain.End(target, m));

            var chain = others.Where(i => MeterChain.Start(i, m) is not null && MeterChain.End(i, m) is not null).OrderBy(i => order[i.Id]).ToList();
            var prev = chain.LastOrDefault(i => order[i.Id].CompareTo(key) < 0);
            var next = chain.FirstOrDefault(i => order[i.Id].CompareTo(key) > 0);

            // The Start reading this trip must have.
            long? s;
            if (e is null) s = null;                                         // meter not used on this trip
            else if (edit.MeterReplaced && typed is not null) s = typed;     // Admin: replaced or reset meter
            else if (oldS is not null)
            {
                if (typed is not null && typed != oldS) return No(MeterChain.LockedStart(m, prev?.DisplayNo));
                s = oldS;
            }
            else if (prev is not null)                                       // meter newly added to this trip
            {
                if (typed is not null && typed != MeterChain.End(prev, m)) return No(MeterChain.LockedStart(m, prev.DisplayNo));
                s = MeterChain.End(prev, m);
            }
            else
            {
                if (typed is null) return No($"Meter {m}: type the start reading. This is the meter's first trip.");
                s = typed;
            }

            if (s == oldS && e == oldE) continue;
            if (e is { } en && s is { } st && en < st)
                return No($"Meter {m}: the end reading cannot be lower than the start reading ({Volume.Format(st)}).");

            if (e is { } newEnd && e != oldE && next is not null && MeterChain.Start(next, m) != newEnd)
            {
                // The next trip on this meter starts where this one now ends.
                if (newEnd > MeterChain.End(next, m))
                    return No($"Meter {m}: the end reading {Volume.Format(newEnd)} is above the end reading of invoice {next.DisplayNo} ({Volume.Format(MeterChain.End(next, m)!.Value)}).");
                Touch(next);
                changes.Add(new(next.DisplayNo, m, "start", MeterChain.Start(next, m), newEnd));
                MeterChain.SetStart(next, m, newEnd);
            }
            else if (e is null && oldE is not null && prev is not null && next is not null && MeterChain.Start(next, m) != MeterChain.End(prev, m))
            {
                // The meter is taken off this trip: the next trip now starts where the previous one ended.
                var joined = MeterChain.End(prev, m)!.Value;
                if (joined > MeterChain.End(next, m))
                    return No($"Meter {m} cannot be taken off: invoice {next.DisplayNo} would end below its start.");
                Touch(next);
                changes.Add(new(next.DisplayNo, m, "start", MeterChain.Start(next, m), joined));
                MeterChain.SetStart(next, m, joined);
            }

            MeterChain.SetStart(target, m, s);
            MeterChain.SetEnd(target, m, e);
            changed = true;
        }

        if (!changed) return Result<MeterOutcome>.Success(new(false, []));
        if (string.IsNullOrWhiteSpace(edit.Reason)) return No("Give a reason for changing the meter readings.");
        var reason = edit.Reason.Trim();

        target.VolumeTenths = MeterChain.VolumeOf(target);
        ActivityLogger.Add(db, now, me, ActivityCategory.Billing, edit.MeterReplaced ? "INVOICE_METER_REPLACED" : "INVOICE_METERS_CHANGED",
            nameof(Invoice), target.Id, before, Snapshot(target), reason, doneBy: editor);
        foreach (var (inv, old) in touched.Values)
        {
            inv.VolumeTenths = MeterChain.VolumeOf(inv);
            inv.Version++;
            ActivityLogger.Add(db, now, me, ActivityCategory.Billing, "INVOICE_METERS_ADJUSTED", nameof(Invoice), inv.Id,
                old, Snapshot(inv), $"Follows the meter correction on invoice {target.DisplayNo}: {reason}", doneBy: editor);
        }
        return Result<MeterOutcome>.Success(new(true, changes));
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
