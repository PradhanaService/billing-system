using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WaterBilling.Data.Entities;

namespace WaterBilling.Data;

// The DbContext is EF Core's "connection to the database".
// Each DbSet<T> is one table. Services use it; pages never do.
public class AppDbContext : DbContext
{
    private readonly TimeProvider _clock;

    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider clock) : base(options)
    {
        _clock = clock;
    }

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    // Rules that apply to every column of a given C# type.
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite has no date-time type, so EF stores text and forgets that the time was UTC.
        // This converter marks every DateTime read back from the database as UTC again.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enums are stored as their name ("Active"), max 10 characters.
        builder.Properties<RecordStatus>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<BalanceSide>().HaveConversion<string>().HaveMaxLength(2);
    }

    // Table layout: lengths, required fields, indexes and checks.
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Supplier>(e =>
        {
            e.ToTable("Suppliers", t =>
            {
                // The database itself refuses bad values, even if a bug in the code lets one through.
                t.HasCheckConstraint("CK_Suppliers_OpeningBalance", "OpeningBalancePaise >= 0");
                t.HasCheckConstraint("CK_Suppliers_OpeningBalanceSide", "OpeningBalanceSide IN ('Dr', 'Cr')");
                t.HasCheckConstraint("CK_Suppliers_Status", "Status IN ('Active', 'Inactive')");
            });

            e.Property(s => s.Name).HasMaxLength(120).IsRequired();
            e.Property(s => s.NormalizedName).HasMaxLength(120).IsRequired();
            e.Property(s => s.ShortName).HasMaxLength(30).IsRequired();
            e.Property(s => s.ContactPerson).HasMaxLength(80);
            e.Property(s => s.Phone).HasMaxLength(15).IsRequired();
            e.Property(s => s.AltPhone).HasMaxLength(15);
            e.Property(s => s.Version).IsConcurrencyToken();

            // Spec: "Indexes: unique on lower-case name; status."
            e.HasIndex(s => s.NormalizedName).IsUnique();
            e.HasIndex(s => s.Status);
        });
    }

    // Every save goes through here, so audit columns can never be forgotten.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampChanges()
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<Supplier>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.RefreshNormalizedName();
        }

        foreach (var entry in ChangeTracker.Entries<IAuditedEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version = 1;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
                entry.Entity.Version++;   // the old value is still used in the WHERE, so a stale edit fails
            }
        }
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(
                toDb => RequireUtc(toDb),
                fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc))
        { }

        // A time with no zone (Kind = Unspecified) could be IST or UTC; guessing would shift it
        // by 5.5 hours. So we refuse it and the bug shows up straight away.
        private static DateTime RequireUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => throw new ArgumentException("DateTime saved to the database must be UTC (Kind = Utc)."),
        };
    }
}
