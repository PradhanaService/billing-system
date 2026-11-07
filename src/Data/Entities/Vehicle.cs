namespace WaterBilling.Data.Entities;

// One tanker (Database Schema page 5). A vehicle belongs to one supplier.
public class Vehicle : IAuditedEntity, ISoftDeletable
{
    public long Id { get; set; }

    // System code such as "A13" (FR-VM-03). Never reused: vehicles are never physically deleted.
    public string Code { get; set; } = "";

    public string VehicleNo { get; set; } = "";              // "TN 38 M 3", upper case, spaces tidied
    public string NormalizedVehicleNo { get; set; } = "";    // "TN38M3": unique, so spacing differences cannot duplicate
    public string Nickname { get; set; } = "";               // "Sangeetha TN 38 M 3"

    public long SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public int TankCapacityLitres { get; set; }
    public BillingBasis BillingBasis { get; set; } = BillingBasis.Trip;

    // Trip Cost from the design. Stored for reference only; it has no ledger effect
    // (working assumption for client question 1).
    public long TripCostPaise { get; set; }

    // Rate charged on invoices: per trip (Trip basis) or per kL (Volume basis).
    public long RatePaise { get; set; }

    public string? WaterDetails { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? DriverLicenceNo { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Active;

    // "Deleted" vehicles are hidden but kept, so the code is never reused (FR-VM-03, FR-VM-08).
    public bool IsDeleted { get; set; }

    // Soft delete (see ISoftDeletable): set when deleted, cleared when restored.
    public DateTime? DeletedAtUtc { get; set; }
    public long? DeletedByUserId { get; set; }
    public long? DeletedBySalesPersonId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }

    public string UnitOfMeasure => BillingBasis == BillingBasis.Trip ? "Trip" : "kL";
}
