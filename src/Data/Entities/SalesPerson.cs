namespace WaterBilling.Data.Entities;

// A name in the invoice "Created by" list. Several sales people share one Sales login,
// so each invoice records who made it. The Admin adds and removes names.
// A name used on invoices is made Inactive (off the list) instead of deleted, so old invoices keep it.
public class SalesPerson : IAuditedEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "";              // max 80
    public string NormalizedName { get; set; } = "";    // for the unique check, ignoring case and spaces
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}
