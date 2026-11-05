namespace WaterBilling.Data.Entities;

// One recorded action (FR-ADM-03/04). Append-only: the database refuses UPDATE and DELETE
// on this table (see the triggers in the AddUsersAndActivityLog migration).
public class ActivityLog
{
    public long Id { get; set; }
    public DateTime EventAtUtc { get; set; }
    public long? UserId { get; set; }                  // null for a failed login with an unknown name
    public string UserName { get; set; } = "";         // copied, so renaming a user never changes history
    public string? Role { get; set; }
    public ActivityCategory Category { get; set; }
    public string Action { get; set; } = "";           // e.g. "SUPPLIER_CREATED"
    public string? EntityType { get; set; }            // e.g. "Supplier"
    public long? EntityId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
}
