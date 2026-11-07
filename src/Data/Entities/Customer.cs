namespace WaterBilling.Data.Entities;

// A direct billing party, e.g. Meena Constructions (FR-VM-11, Database Schema page 13).
// An invoice can be billed to a customer instead of the vehicle's supplier.
public class Customer : IAuditedEntity, ISoftDeletable
{
    public long Id { get; set; }
    public string Name { get; set; } = "";                 // max 120
    public string NormalizedName { get; private set; } = "";
    public string Phone { get; set; } = "";                // max 15
    public string? Address { get; set; }
    public long OpeningBalancePaise { get; set; }
    public BalanceSide OpeningBalanceSide { get; set; } = BalanceSide.Dr;
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    // Soft delete (see ISoftDeletable): set when deleted, cleared when restored.
    public DateTime? DeletedAtUtc { get; set; }
    public long? DeletedByUserId { get; set; }
    public long? DeletedBySalesPersonId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }

    public void RefreshNormalizedName() => NormalizedName = Supplier.NormalizeName(Name);
}
