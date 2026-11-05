namespace WaterBilling.Data.Entities;

// A person who can log in. Users are deactivated, never deleted (FR-ADM-01).
public class User : IAuditedEntity
{
    public long Id { get; set; }
    public string UserName { get; set; } = "";              // max 50
    public string NormalizedUserName { get; set; } = "";    // upper case, for the unique check
    public string FullName { get; set; } = "";              // max 100
    public string? Phone { get; set; }

    // Never the password itself: a slow one-way hash (PBKDF2, from ASP.NET Core Identity).
    public string PasswordHash { get; set; } = "";

    public UserRole Role { get; set; }
    public bool MustChangePassword { get; set; } = true;    // FR-ADM-02

    // Changes whenever the password, role or status changes.
    // Logged-in sessions carry the old value and are signed out at their next check.
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime? LastLoginAtUtc { get; set; }
    public RecordStatus Status { get; set; } = RecordStatus.Active;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int Version { get; set; }
}
