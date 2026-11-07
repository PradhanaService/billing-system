namespace WaterBilling.Data.Entities;

// A record that is never removed straight away. "Delete" only marks it (soft delete):
// it disappears from every list but can be restored from the Recovery page for 30 days.
// After that the Recovery clean-up removes it for good (RecoveryService.KeepDays).
public interface ISoftDeletable
{
    DateTime? DeletedAtUtc { get; set; }    // null = not deleted
    long? DeletedByUserId { get; set; }     // the login that deleted it
}
