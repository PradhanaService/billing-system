using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Masters;

public sealed record SupplierRow(long Id, string Name, string ShortName, string? ContactPerson, string Phone,
    RecordStatus Status, int VehicleCount);

// Supplier Master form (FR-VM-01). Opening balance can only be entered when creating.
public sealed class SupplierForm
{
    [Required(ErrorMessage = "Supplier Name is required."), StringLength(120)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Short name is required."), StringLength(30)]
    public string ShortName { get; set; } = "";

    [StringLength(80)] public string? ContactPerson { get; set; }

    [Required(ErrorMessage = "Phone No. is required."), Phone] public string Phone { get; set; } = "";

    [Phone] public string? AltPhone { get; set; }

    [StringLength(500)] public string? Address { get; set; }

    [Rupees] public decimal OpeningBalance { get; set; }
    public BalanceSide OpeningBalanceSide { get; set; } = BalanceSide.Dr;

    public RecordStatus Status { get; set; } = RecordStatus.Active;
    public int Version { get; set; }
}

public class SupplierService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<IReadOnlyList<SupplierRow>> SearchAsync(string? text, StatusFilter filter)
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Suppliers.AsQueryable();
        if (filter != StatusFilter.All)
        {
            var status = filter == StatusFilter.Active ? RecordStatus.Active : RecordStatus.Inactive;
            query = query.Where(s => s.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            // EF turns this into a parameterised SQL LIKE, so typed text cannot inject SQL.
            var t = $"%{text.Trim()}%";
            query = query.Where(s => EF.Functions.Like(s.Name, t) || EF.Functions.Like(s.ShortName, t)
                || EF.Functions.Like(s.Phone, t) || (s.ContactPerson != null && EF.Functions.Like(s.ContactPerson, t)));
        }
        return await query.OrderBy(s => s.Name)
            .Select(s => new SupplierRow(s.Id, s.Name, s.ShortName, s.ContactPerson, s.Phone, s.Status,
                0))   // vehicle count arrives in Module 6
            .ToListAsync();
    }

    // For drop-downs on other screens: active suppliers only.
    public async Task<IReadOnlyList<(long Id, string Name)>> ListActiveAsync()
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Suppliers.Where(s => s.Status == RecordStatus.Active).OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name }).ToListAsync();
        return rows.Select(r => (r.Id, r.Name)).ToList();
    }

    public async Task<SupplierForm?> GetAsync(long id)
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var s = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        return s is null ? null : new SupplierForm
        {
            Name = s.Name, ShortName = s.ShortName, ContactPerson = s.ContactPerson, Phone = s.Phone,
            AltPhone = s.AltPhone, Address = s.Address, OpeningBalance = Money.ToRupees(s.OpeningBalancePaise),
            OpeningBalanceSide = s.OpeningBalanceSide, Status = s.Status, Version = s.Version,
        };
    }

    public async Task<Result<long>> CreateAsync(SupplierForm form)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        if (FormValidator.FirstError(form) is { } error) return Result<long>.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var supplier = new Supplier { OpeningBalancePaise = Money.FromRupees(form.OpeningBalance), OpeningBalanceSide = form.OpeningBalanceSide };
        Apply(supplier, form);
        if (await NameTakenAsync(db, supplier.Name, null)) return Result<long>.Fail("A supplier with this name already exists.");

        await using var tx = await db.Database.BeginTransactionAsync();
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "SUPPLIER_CREATED", nameof(Supplier), supplier.Id,
            after: Snapshot(supplier));
        await OnPartyCreatedAsync(db, supplier, me);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(supplier.Id);
    }

    public async Task<Result> UpdateAsync(long id, SupplierForm form)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        if (FormValidator.FirstError(form) is { } error) return Result.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return Result.Fail("Supplier not found.");
        if (supplier.Version != form.Version) return Result.Fail(Messages.ChangedBySomeoneElse);
        if (await NameTakenAsync(db, PartyRules.Tidy(form.Name), id)) return Result.Fail("A supplier with this name already exists.");

        var before = Snapshot(supplier);
        Apply(supplier, form);   // opening balance is not copied: it is fixed after creation (FR-VM-12)
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "SUPPLIER_UPDATED", nameof(Supplier), id, before, Snapshot(supplier));
        return await SaveAsync(db);
    }

    // FR-VM-08: delete only a supplier with no history; otherwise the screen offers Inactive.
    public async Task<Result> DeleteAsync(long id)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        await using var db = await dbFactory.CreateDbContextAsync();
        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return Result.Fail("Supplier not found.");
        if (await MasterHistory.SupplierHasHistoryAsync(db, id))
            return Result.Fail("This supplier has vehicles or transactions, so it cannot be deleted. Set it to Inactive instead.");

        db.Suppliers.Remove(supplier);
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "SUPPLIER_DELETED", nameof(Supplier), id, before: Snapshot(supplier));
        return await SaveAsync(db);
    }

    // Hook for Module 7: posts the opening balance to the ledger inside the same transaction.
    protected virtual Task OnPartyCreatedAsync(AppDbContext db, Supplier supplier, UserInfo me) => Task.CompletedTask;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static void Apply(Supplier s, SupplierForm f)
    {
        s.Name = PartyRules.Tidy(f.Name);
        s.ShortName = PartyRules.Tidy(f.ShortName);
        s.ContactPerson = PartyRules.TidyOrNull(f.ContactPerson);
        s.Phone = PartyRules.CleanPhone(f.Phone);
        s.AltPhone = string.IsNullOrWhiteSpace(f.AltPhone) ? null : PartyRules.CleanPhone(f.AltPhone);
        s.Address = PartyRules.TidyOrNull(f.Address);
        s.Status = f.Status;
    }

    private static Task<bool> NameTakenAsync(AppDbContext db, string name, long? exceptId)
    {
        var key = Supplier.NormalizeName(name);
        return db.Suppliers.AnyAsync(s => s.NormalizedName == key && s.Id != exceptId);
    }

    private static object Snapshot(Supplier s) => new
    {
        s.Name, s.ShortName, s.ContactPerson, s.Phone, s.AltPhone, s.Address,
        s.OpeningBalancePaise, OpeningBalanceSide = s.OpeningBalanceSide.ToString(), Status = s.Status.ToString(),
    };

    private static async Task<Result> SaveAsync(AppDbContext db)
    {
        try { await db.SaveChangesAsync(); return Result.Success(); }
        catch (DbUpdateConcurrencyException) { return Result.Fail(Messages.ChangedBySomeoneElse); }
    }
}
