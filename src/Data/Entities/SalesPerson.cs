namespace WaterBilling.Data.Entities;

// A name in the invoice "Created by" list. Several sales people share one Sales login,
// so each invoice records who made it. The Admin adds and removes names.
// Removing a name soft-deletes it (Recovery, 30 days). A name used on invoices is never removed for good,
// so old invoices keep it.
public class SalesPerson : IAuditedEntity, ISoftDeletable
{
    public long Id { get; set; }
    public string Name { get; set; } = "";              // max 80
    public string NormalizedName { get; set; } = "";    // for the unique check, ignoring case and spaces
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    // Soft delete (see ISoftDeletable): set when deleted, cleared when restored.
    public DateTime? DeletedAtUtc { get; set; }
    public long? DeletedByUserId { get; set; }
    public long? DeletedBySalesPersonId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}
