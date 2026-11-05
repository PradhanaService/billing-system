namespace WaterBilling.Data.Entities;

// Every master table has these columns (spec: "Audit columns on all master tables").
// AppDbContext fills them automatically on save, so no screen can forget.
// CreatedBy / UpdatedBy are added in Session 6, when users exist.
public interface IAuditedEntity
{
    DateTime CreatedAtUtc { get; set; }
    DateTime UpdatedAtUtc { get; set; }

    // Rises by 1 on every save. If two people edit the same record,
    // the second save is refused (NFR-CON-05).
    int Version { get; set; }
}
