using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Recovery;

// Recovery: nothing is removed straight away. "Delete" (suppliers, customers, vehicles, ledgers,
// sales person names) only marks the record; it is hidden everywhere but can be restored here.
// After 30 days the clean-up (RecoveryCleanupJob) removes it for good. Admin and Sales can both
// restore. Every delete, restore and final removal is written to the activity log.

public enum RecoveryKind { Supplier, Customer, Vehicle, Ledger, SalesPerson }

// One deleted record. RemovedForGoodAtUtc = when the clean-up will remove it (deleted + 30 days).
public sealed record DeletedItemRow(RecoveryKind Kind, long Id, string Name, string Details,
    DateTime DeletedAtUtc, string DeletedBy, DateTime RemovedForGoodAtUtc);

public class RecoveryService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    // How long a deleted record can still be restored.
    public const int KeepDays = 30;

    // Used by every Delete: hide the record and note who deleted it and when.
    public static void MarkDeleted(ISoftDeletable record, UserInfo me, DateTime nowUtc)
    {
        record.DeletedAtUtc = nowUtc;
        record.DeletedByUserId = me.Id;
    }

    // Everything deleted in the last 30 days, newest first.
    public async Task<IReadOnlyList<DeletedItemRow>> ListAsync(RecoveryKind? kind = null)
    {
        await currentUser.RequireAsync(Permission.RecoveryManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = Now.AddDays(-KeepDays);

        // (kind, id, name, details, deleted at, deleted by user id)
        var items = new List<(RecoveryKind Kind, long Id, string Name, string Details, DateTime At, long? By)>();
        if (kind is null or RecoveryKind.Supplier)
            items.AddRange((await db.Suppliers.IgnoreQueryFilters().Where(x => x.DeletedAtUtc > since)
                .Select(x => new { x.Id, x.Name, x.Phone, x.DeletedAtUtc, x.DeletedByUserId }).ToListAsync())
                .Select(x => (RecoveryKind.Supplier, x.Id, x.Name, $"Phone {x.Phone}", x.DeletedAtUtc!.Value, x.DeletedByUserId)));
        if (kind is null or RecoveryKind.Customer)
            items.AddRange((await db.Customers.IgnoreQueryFilters().Where(x => x.DeletedAtUtc > since)
                .Select(x => new { x.Id, x.Name, x.Phone, x.DeletedAtUtc, x.DeletedByUserId }).ToListAsync())
                .Select(x => (RecoveryKind.Customer, x.Id, x.Name, $"Phone {x.Phone}", x.DeletedAtUtc!.Value, x.DeletedByUserId)));
        if (kind is null or RecoveryKind.Vehicle)
            items.AddRange((await db.Vehicles.IgnoreQueryFilters().Where(x => x.IsDeleted && x.DeletedAtUtc > since)
                .Select(x => new { x.Id, x.VehicleNo, x.Code, Supplier = x.Supplier!.Name, x.DeletedAtUtc, x.DeletedByUserId }).ToListAsync())
                .Select(x => (RecoveryKind.Vehicle, x.Id, x.VehicleNo, $"Code {x.Code}, {x.Supplier}", x.DeletedAtUtc!.Value, x.DeletedByUserId)));
        if (kind is null or RecoveryKind.Ledger)
            items.AddRange((await db.Accounts.IgnoreQueryFilters().Where(x => x.DeletedAtUtc > since)
                .Select(x => new { x.Id, x.Name, x.DeletedAtUtc, x.DeletedByUserId }).ToListAsync())
                .Select(x => (RecoveryKind.Ledger, x.Id, x.Name, "Expense ledger", x.DeletedAtUtc!.Value, x.DeletedByUserId)));
        if (kind is null or RecoveryKind.SalesPerson)
            items.AddRange((await db.SalesPersons.IgnoreQueryFilters().Where(x => x.DeletedAtUtc > since)
                .Select(x => new { x.Id, x.Name, x.DeletedAtUtc, x.DeletedByUserId }).ToListAsync())
                .Select(x => (RecoveryKind.SalesPerson, x.Id, x.Name, "Invoice \"Created by\" name", x.DeletedAtUtc!.Value, x.DeletedByUserId)));

        var users = await db.Users.ToDictionaryAsync(u => u.Id, u => u.UserName);
        return items.OrderByDescending(i => i.At)
            .Select(i => new DeletedItemRow(i.Kind, i.Id, i.Name, i.Details, i.At,
                i.By is { } by && users.TryGetValue(by, out var name) ? name : "-", i.At.AddDays(KeepDays)))
            .ToList();
    }

    // Brings a deleted record back exactly as it was, unless its name or number is in use again.
    // On success the value is the message to show.
    public async Task<Result<string>> RestoreAsync(RecoveryKind kind, long id)
    {
        var me = await currentUser.RequireAsync(Permission.RecoveryManage);
        await using var db = await dbFactory.CreateDbContextAsync();
        var since = Now.AddDays(-KeepDays);
        const string gone = "This item is no longer in Recovery (restored already, or older than 30 days).";

        ISoftDeletable? record;
        string name;
        var note = "";
        switch (kind)
        {
            case RecoveryKind.Supplier:
                var s = await db.Suppliers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc > since);
                if (s is null) return Result<string>.Fail(gone);
                if (await db.Suppliers.AnyAsync(x => x.NormalizedName == Supplier.NormalizeName(s.Name)))
                    return Result<string>.Fail($"A supplier named {s.Name} exists again. Rename or delete it first.");
                (record, name) = (s, s.Name);
                break;
            case RecoveryKind.Customer:
                var c = await db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc > since);
                if (c is null) return Result<string>.Fail(gone);
                if (await db.Customers.AnyAsync(x => x.NormalizedName == Supplier.NormalizeName(c.Name)))
                    return Result<string>.Fail($"A customer named {c.Name} exists again. Rename or delete it first.");
                (record, name) = (c, c.Name);
                break;
            case RecoveryKind.Vehicle:
                var v = await db.Vehicles.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.IsDeleted && x.DeletedAtUtc > since);
                if (v is null) return Result<string>.Fail(gone);
                if (!await db.Suppliers.AnyAsync(x => x.Id == v.SupplierId))
                    return Result<string>.Fail("This vehicle's supplier is deleted. Restore the supplier first.");
                var number = VehicleService.NormalizeVehicleNo(v.VehicleNo);
                if (await db.Vehicles.AnyAsync(x => x.NormalizedVehicleNo == number && x.Id != id))
                    return Result<string>.Fail($"Another vehicle now has the number {v.VehicleNo}.");
                v.IsDeleted = false;
                v.NormalizedVehicleNo = number;   // was freed when deleted
                // Vehicles deleted before Recovery existed were also made Inactive at the time.
                if (v.Status != VehicleStatus.Active) note = $" It is {v.Status}: change it in Vehicle Master if needed.";
                (record, name) = (v, v.VehicleNo);
                break;
            case RecoveryKind.Ledger:
                var a = await db.Accounts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc > since);
                if (a is null) return Result<string>.Fail(gone);
                if (await db.Accounts.AnyAsync(x => x.NormalizedName == a.NormalizedName))
                    return Result<string>.Fail($"A ledger named {a.Name} exists again. Rename or delete it first.");
                (record, name) = (a, a.Name);
                break;
            default:
                var p = await db.SalesPersons.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAtUtc > since);
                if (p is null) return Result<string>.Fail(gone);
                if (await db.SalesPersons.AnyAsync(x => x.NormalizedName == p.NormalizedName))
                    return Result<string>.Fail($"{p.Name} is already in the list again.");
                p.Status = RecordStatus.Active;
                (record, name) = (p, p.Name);
                break;
        }

        var deletedAt = record.DeletedAtUtc;
        record.DeletedAtUtc = null;
        record.DeletedByUserId = null;
        ActivityLogger.Add(db, Now, me, Category(kind), $"{Code(kind)}_RESTORED", EntityType(kind), id,
            before: new { Name = name, DeletedAtUtc = deletedAt }, after: new { Name = name });
        await db.SaveChangesAsync();
        return Result<string>.Success($"{name} restored.{note}");
    }

    // Removes for good what was deleted more than 30 days ago. Run by RecoveryCleanupJob, not by a person,
    // so the log shows "System". Returns how many records were removed.
    public async Task<int> RemoveExpiredAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var before = Now.AddDays(-KeepDays);
        var removed = 0;

        void Log(RecoveryKind kind, long id, object snapshot)
        {
            ActivityLogger.Add(db, Now, null, Category(kind), $"{Code(kind)}_REMOVED_FOR_GOOD", EntityType(kind), id,
                before: snapshot, userNameIfUnknown: "System");
            removed++;
        }

        foreach (var v in await db.Vehicles.IgnoreQueryFilters().Where(x => x.IsDeleted && x.DeletedAtUtc <= before).ToListAsync())
        {
            db.RetiredVehicleCodes.Add(new RetiredVehicleCode { Code = v.Code, RetiredAtUtc = Now });   // code never reused
            db.Vehicles.Remove(v);
            Log(RecoveryKind.Vehicle, v.Id, new { v.Code, v.VehicleNo });
        }
        foreach (var s in await db.Suppliers.IgnoreQueryFilters().Where(x => x.DeletedAtUtc <= before
                     && !db.Vehicles.IgnoreQueryFilters().Any(v => v.SupplierId == x.Id && !(v.IsDeleted && v.DeletedAtUtc <= before))).ToListAsync())
        {
            db.Suppliers.Remove(s);
            Log(RecoveryKind.Supplier, s.Id, new { s.Name, s.Phone });
        }
        foreach (var c in await db.Customers.IgnoreQueryFilters().Where(x => x.DeletedAtUtc <= before).ToListAsync())
        {
            db.Customers.Remove(c);
            Log(RecoveryKind.Customer, c.Id, new { c.Name, c.Phone });
        }
        foreach (var a in await db.Accounts.IgnoreQueryFilters().Where(x => x.DeletedAtUtc <= before).ToListAsync())
        {
            db.Accounts.Remove(a);
            Log(RecoveryKind.Ledger, a.Id, new { a.Name });
        }
        // A name used on invoices is kept (hidden) so those invoices still show who made them.
        foreach (var p in await db.SalesPersons.IgnoreQueryFilters().Where(x => x.DeletedAtUtc <= before
                     && !db.Invoices.Any(i => i.SalesPersonId == x.Id)).ToListAsync())
        {
            db.SalesPersons.Remove(p);
            Log(RecoveryKind.SalesPerson, p.Id, new { p.Name });
        }

        // Vehicles first: a supplier can only go once its vehicles are gone from the table.
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return removed;
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // Names used in the activity log, matching the existing "..._DELETED" entries.
    private static string Code(RecoveryKind kind) => kind switch
    {
        RecoveryKind.SalesPerson => "SALES_PERSON",
        RecoveryKind.Ledger => "LEDGER",
        _ => kind.ToString().ToUpperInvariant(),
    };

    private static string EntityType(RecoveryKind kind) => kind switch
    {
        RecoveryKind.Ledger => nameof(Account),
        _ => kind.ToString(),
    };

    private static ActivityCategory Category(RecoveryKind kind) =>
        kind == RecoveryKind.SalesPerson ? ActivityCategory.Admin : ActivityCategory.Master;
}
