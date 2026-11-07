using System.Text.RegularExpressions;

namespace WaterBilling.Data.Entities;

// One row in the Suppliers table (spec: Database Schema, page 5).
// A supplier owns tanker vehicles and is billed for the water they take.
public class Supplier : IAuditedEntity, ISoftDeletable
{
    public long Id { get; set; }

    public string Name { get; set; } = "";          // "Supplier Name*", max 120

    // Name in a fixed form (spaces tidied, upper case) used only for the unique check,
    // so "Sangeetha  water" and "SANGEETHA WATER" count as the same name.
    // Set automatically by AppDbContext on save.
    public string NormalizedName { get; private set; } = "";

    public string ShortName { get; set; } = "";     // used to build vehicle nicknames, max 30
    public string? ContactPerson { get; set; }      // max 80
    public string Phone { get; set; } = "";         // "Phone No.*", max 15
    public string? AltPhone { get; set; }           // max 15
    public string? Address { get; set; }

    // Money is whole paise in a long. ₹1,250.50 is stored as 125050. The database checks it is 0 or more.
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

    // Called by AppDbContext before every save.
    public void RefreshNormalizedName() => NormalizedName = NormalizeName(Name);

    // "  Sangeetha   Water " -> "SANGEETHA WATER"
    public static string NormalizeName(string name) =>
        Regex.Replace(name.Trim(), @"\s+", " ").ToUpperInvariant();
}
