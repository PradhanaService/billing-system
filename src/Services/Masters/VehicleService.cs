using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Recovery;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Masters;

// Vehicle Master rules: system code proposed and never reused, vehicle number stored in capitals
// and unique however it is spaced, nickname defaults to supplier short name + number, capacity and
// rate above zero, and only Active vehicles of Active suppliers can be invoiced.

public sealed record VehicleRow(long Id, string Code, string VehicleNo, string Nickname, string SupplierName,
    int TankCapacityLitres, BillingBasis BillingBasis, long RatePaise, VehicleStatus Status, bool SupplierActive);

// What the invoice screen needs about a vehicle (FR-INV-02).
public sealed record VehicleForBilling(long Id, string Code, string VehicleNo, string Nickname,
    long SupplierId, string SupplierName, BillingBasis BillingBasis, long RatePaise, int TankCapacityLitres);

// Vehicle Master form (FR-VM-02).
public sealed class VehicleForm
{
    // Proposed by the system; only an Admin may type another (FR-VM-03).
    [StringLength(10)] public string? Code { get; set; }

    [Required(ErrorMessage = "Vehicle No. is required."), StringLength(20)]
    public string VehicleNo { get; set; } = "";

    [StringLength(80)] public string? Nickname { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "Choose a supplier.")]
    public long SupplierId { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Capacity must be between 1 and 10,00,000 litres.")]
    public int TankCapacityLitres { get; set; }

    public BillingBasis BillingBasis { get; set; } = BillingBasis.Trip;

    [Rupees] public decimal TripCost { get; set; }

    [Rupees(AllowZero = false)] public decimal Rate { get; set; }

    [StringLength(120)] public string? WaterDetails { get; set; }
    [StringLength(80)] public string? DriverName { get; set; }
    [Phone] public string? DriverPhone { get; set; }
    [StringLength(25)] public string? DriverLicenceNo { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Active;
    public int Version { get; set; }
}

public partial class VehicleService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser, TimeProvider clock)
{
    // "TN 38 M 3", "TN 67 C", "TN01AB1234". Short forms are allowed (FR-VM-04).
    [GeneratedRegex(@"^[A-Z]{2} ?[0-9]{1,2}( ?[A-Z]{1,3})?( ?[0-9]{1,4})?$")] private static partial Regex VehicleNoPattern();
    [GeneratedRegex(@"^[A-Z][0-9]{1,4}$")] private static partial Regex CodePattern();

    public static string TidyVehicleNo(string? text) => PartyRules.Tidy(text).ToUpperInvariant();
    public static string NormalizeVehicleNo(string text) => TidyVehicleNo(text).Replace(" ", "");

    public async Task<IReadOnlyList<VehicleRow>> SearchAsync(string? text, StatusFilter filter, long? supplierId = null)
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Vehicles.Where(v => !v.IsDeleted);
        if (filter == StatusFilter.Active) query = query.Where(v => v.Status == VehicleStatus.Active);
        if (filter == StatusFilter.Inactive) query = query.Where(v => v.Status != VehicleStatus.Active);
        if (supplierId is { } sid) query = query.Where(v => v.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(text))
        {
            // FR-VM-10: match vehicle number (with or without spaces), code, nickname or supplier.
            var t = $"%{text.Trim()}%";
            var compact = $"%{text.Trim().Replace(" ", "")}%";
            query = query.Where(v => EF.Functions.Like(v.NormalizedVehicleNo, compact) || EF.Functions.Like(v.Code, t)
                || EF.Functions.Like(v.Nickname, t) || EF.Functions.Like(v.Supplier!.Name, t));
        }
        return await query.OrderBy(v => v.Code.Length).ThenBy(v => v.Code)
            .Select(v => new VehicleRow(v.Id, v.Code, v.VehicleNo, v.Nickname, v.Supplier!.Name, v.TankCapacityLitres,
                v.BillingBasis, v.RatePaise, v.Status, v.Supplier.Status == RecordStatus.Active))
            .ToListAsync();
    }

    // Vehicles that can be invoiced right now (FR-VM-09): vehicle Active and supplier Active.
    public async Task<IReadOnlyList<VehicleForBilling>> ListBillableAsync()
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await Billable(db).OrderBy(v => v.Code.Length).ThenBy(v => v.Code)
            .Select(v => new VehicleForBilling(v.Id, v.Code, v.VehicleNo, v.Nickname, v.SupplierId, v.Supplier!.Name,
                v.BillingBasis, v.RatePaise, v.TankCapacityLitres))
            .ToListAsync();
    }

    public static IQueryable<Vehicle> Billable(AppDbContext db) =>
        db.Vehicles.Where(v => !v.IsDeleted && v.Status == VehicleStatus.Active && v.Supplier!.Status == RecordStatus.Active);

    public async Task<string> ProposeCodeAsync()
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        return await NextCodeAsync(db);
    }

    public async Task<VehicleForm?> GetAsync(long id)
    {
        await currentUser.RequireAsync(Permission.MastersView);
        await using var db = await dbFactory.CreateDbContextAsync();
        var v = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        return v is null ? null : new VehicleForm
        {
            Code = v.Code, VehicleNo = v.VehicleNo, Nickname = v.Nickname, SupplierId = v.SupplierId,
            TankCapacityLitres = v.TankCapacityLitres, BillingBasis = v.BillingBasis,
            TripCost = Money.ToRupees(v.TripCostPaise), Rate = Money.ToRupees(v.RatePaise),
            WaterDetails = v.WaterDetails, DriverName = v.DriverName, DriverPhone = v.DriverPhone,
            DriverLicenceNo = v.DriverLicenceNo, Status = v.Status, Version = v.Version,
        };
    }

    public async Task<Result<long>> CreateAsync(VehicleForm form)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        if (Check(form) is { } error) return Result<long>.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();

        var supplier = await db.Suppliers.FindAsync(form.SupplierId);
        if (supplier is null) return Result<long>.Fail("Supplier not found.");
        if (supplier.Status != RecordStatus.Active) return Result<long>.Fail("The supplier is inactive. Activate it first.");

        // Only an Admin may choose the code; everyone else gets the proposal.
        var code = me.Has(Permission.VehicleCodeOverride) && !string.IsNullOrWhiteSpace(form.Code)
            ? form.Code.Trim().ToUpperInvariant()
            : await NextCodeAsync(db);
        if (!CodePattern().IsMatch(code)) return Result<long>.Fail("Vehicle code must be one letter and 1 to 4 digits, e.g. A13.");
        if (await CodesEverUsed(db).AnyAsync(c => c == code)) return Result<long>.Fail($"Vehicle code {code} is already used (codes are never reused).");

        var vehicle = new Vehicle { Code = code, SupplierId = supplier.Id };
        Apply(vehicle, form, supplier.ShortName);
        if (await NumberTakenAsync(db, vehicle.NormalizedVehicleNo, null))
            return Result<long>.Fail("A vehicle with this number already exists.");

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync();
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "VEHICLE_CREATED", nameof(Vehicle), vehicle.Id, after: Snapshot(vehicle));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Result<long>.Success(vehicle.Id);
    }

    public async Task<Result> UpdateAsync(long id, VehicleForm form)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        if (Check(form) is { } error) return Result.Fail(error);

        await using var db = await dbFactory.CreateDbContextAsync();
        var vehicle = await db.Vehicles.Include(v => v.Supplier).SingleOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
        if (vehicle is null) return Result.Fail("Vehicle not found.");
        if (vehicle.Version != form.Version) return Result.Fail(Messages.ChangedBySomeoneElse);
        if (form.SupplierId != vehicle.SupplierId)
            return Result.Fail("A vehicle cannot move to another supplier in this version.");

        var before = Snapshot(vehicle);
        var oldRate = vehicle.RatePaise;
        Apply(vehicle, form, vehicle.Supplier!.ShortName);   // code never changes after creation
        if (await NumberTakenAsync(db, vehicle.NormalizedVehicleNo, id))
            return Result.Fail("A vehicle with this number already exists.");

        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "VEHICLE_UPDATED", nameof(Vehicle), id, before, Snapshot(vehicle));
        if (oldRate != vehicle.RatePaise)
        {
            // A separate entry so rate changes can be listed on their own (FR-RPT-05).
            ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "VEHICLE_RATE_CHANGED", nameof(Vehicle), id,
                new { RatePaise = oldRate }, new { vehicle.RatePaise });
        }
        try { await db.SaveChangesAsync(); return Result.Success(); }
        catch (DbUpdateConcurrencyException) { return Result.Fail(Messages.ChangedBySomeoneElse); }
    }

    // FR-VM-08: only a vehicle with no history. It is hidden, not removed, so its code stays taken.
    // Soft delete: the record is hidden, not removed. It can be restored from Recovery for 30 days.
    // deletedBy: the sales person chosen in "Deleted by" (Sales login); ignored on the Admin login.
    public async Task<Result> DeleteAsync(long id, long? deletedBy = null)
    {
        var me = await currentUser.RequireAsync(Permission.MastersEdit);
        await using var db = await dbFactory.CreateDbContextAsync();
        var who = await RecoveryService.DeletedByAsync(db, me, deletedBy);
        if (!who.Ok) return Result.Fail(who.Error!);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Id == id && !v.IsDeleted);
        if (vehicle is null) return Result.Fail("Vehicle not found.");
        if (await MasterHistory.VehicleHasHistoryAsync(db, id))
            return Result.Fail("This vehicle has transactions, so it cannot be deleted. Set it to Inactive instead.");

        vehicle.IsDeleted = true;
        RecoveryService.MarkDeleted(vehicle, me, Now, deletedBy);
        // Free the number for a new vehicle; the code stays reserved.
        vehicle.NormalizedVehicleNo = $"#{vehicle.Id}";
        ActivityLogger.Add(db, Now, me, ActivityCategory.Master, "VEHICLE_DELETED", nameof(Vehicle), id, before: Snapshot(vehicle), reason: $"Deleted by {who.Value}");
        await db.SaveChangesAsync();
        return Result.Success();
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static IQueryable<string> CodesEverUsed(AppDbContext db) =>
        db.Vehicles.Select(v => v.Code).Concat(db.RetiredVehicleCodes.Select(r => r.Code));

    private static string? Check(VehicleForm form)
    {
        if (FormValidator.FirstError(form) is { } error) return error;
        if (!VehicleNoPattern().IsMatch(TidyVehicleNo(form.VehicleNo)))
            return "Vehicle No. should look like TN 38 M 3 or TN 67 C.";
        return null;
    }

    // Next free "A" code: A1, A2 ... A9999, then B1. Counts hidden vehicles and the codes of vehicles
    // removed for good (RetiredVehicleCodes) too, so codes are never reused.
    private static async Task<string> NextCodeAsync(AppDbContext db)
    {
        var codes = await CodesEverUsed(db).ToListAsync();
        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            var max = codes.Where(c => c.Length > 1 && c[0] == letter && int.TryParse(c[1..], out _))
                .Select(c => int.Parse(c[1..])).DefaultIfEmpty(0).Max();
            if (max < 9999) return $"{letter}{max + 1}";
        }
        throw new InvalidOperationException("All vehicle codes are used.");
    }

    private static Task<bool> NumberTakenAsync(AppDbContext db, string normalized, long? exceptId) =>
        db.Vehicles.AnyAsync(v => v.NormalizedVehicleNo == normalized && v.Id != exceptId);

    private static void Apply(Vehicle v, VehicleForm f, string supplierShortName)
    {
        v.VehicleNo = TidyVehicleNo(f.VehicleNo);
        v.NormalizedVehicleNo = NormalizeVehicleNo(f.VehicleNo);
        // FR-VM-05: nickname defaults to supplier short name + plate.
        v.Nickname = string.IsNullOrWhiteSpace(f.Nickname) ? $"{supplierShortName} {v.VehicleNo}" : PartyRules.Tidy(f.Nickname);
        v.TankCapacityLitres = f.TankCapacityLitres;
        v.BillingBasis = f.BillingBasis;
        v.TripCostPaise = Money.FromRupees(f.TripCost);
        v.RatePaise = Money.FromRupees(f.Rate);
        v.WaterDetails = PartyRules.TidyOrNull(f.WaterDetails);
        v.DriverName = PartyRules.TidyOrNull(f.DriverName);
        v.DriverPhone = string.IsNullOrWhiteSpace(f.DriverPhone) ? null : PartyRules.CleanPhone(f.DriverPhone);
        v.DriverLicenceNo = PartyRules.TidyOrNull(f.DriverLicenceNo)?.ToUpperInvariant();
        v.Status = f.Status;
    }

    private static object Snapshot(Vehicle v) => new
    {
        v.Code, v.VehicleNo, v.Nickname, v.SupplierId, v.TankCapacityLitres, BillingBasis = v.BillingBasis.ToString(),
        v.TripCostPaise, v.RatePaise, v.WaterDetails, v.DriverName, v.DriverPhone, v.DriverLicenceNo,
        Status = v.Status.ToString(), v.IsDeleted,
    };
}
