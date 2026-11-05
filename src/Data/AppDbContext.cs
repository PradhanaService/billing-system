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
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<BusinessDay> BusinessDays => Set<BusinessDay>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<ReceiptAllocation> ReceiptAllocations => Set<ReceiptAllocation>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    // Rules that apply to every column of a given C# type.
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite has no date-time type, so EF stores text and forgets that the time was UTC.
        // This converter marks every DateTime read back from the database as UTC again.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enums are stored as their name ("Active"), max 10 characters.
        builder.Properties<RecordStatus>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<BalanceSide>().HaveConversion<string>().HaveMaxLength(2);
        builder.Properties<VehicleStatus>().HaveConversion<string>().HaveMaxLength(12);
        builder.Properties<BillingBasis>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<AccountType>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<VoucherType>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<VoucherStatus>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<PaymentMode>().HaveConversion<string>().HaveMaxLength(6);
        builder.Properties<PartyType>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<BusinessDayStatus>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<InvoicePaymentMode>().HaveConversion<string>().HaveMaxLength(6);
        builder.Properties<InvoiceStatus>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<RateSource>().HaveConversion<string>().HaveMaxLength(10);
        builder.Properties<UserRole>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<ActivityCategory>().HaveConversion<string>().HaveMaxLength(10);
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
            e.Property(s => s.Address).HasMaxLength(500);
            e.Property(s => s.Version).IsConcurrencyToken();

            // Spec: "Indexes: unique on lower-case name; status."
            e.HasIndex(s => s.NormalizedName).IsUnique();
            e.HasIndex(s => s.Status);
        });

        model.Entity<Customer>(e =>
        {
            e.ToTable("Customers", t =>
            {
                t.HasCheckConstraint("CK_Customers_OpeningBalance", "OpeningBalancePaise >= 0");
                t.HasCheckConstraint("CK_Customers_OpeningBalanceSide", "OpeningBalanceSide IN ('Dr', 'Cr')");
                t.HasCheckConstraint("CK_Customers_Status", "Status IN ('Active', 'Inactive')");
            });
            e.Property(c => c.Name).HasMaxLength(120).IsRequired();
            e.Property(c => c.NormalizedName).HasMaxLength(120).IsRequired();
            e.Property(c => c.Phone).HasMaxLength(15).IsRequired();
            e.Property(c => c.Address).HasMaxLength(500);
            e.Property(c => c.Version).IsConcurrencyToken();
            e.HasIndex(c => c.NormalizedName).IsUnique();
        });

        model.Entity<Vehicle>(e =>
        {
            e.ToTable("Vehicles", t =>
            {
                t.HasCheckConstraint("CK_Vehicles_Capacity", "TankCapacityLitres > 0");
                t.HasCheckConstraint("CK_Vehicles_Rate", "RatePaise > 0");
                t.HasCheckConstraint("CK_Vehicles_TripCost", "TripCostPaise >= 0");
                t.HasCheckConstraint("CK_Vehicles_Status", "Status IN ('Active', 'Inactive', 'Maintenance')");
                t.HasCheckConstraint("CK_Vehicles_Basis", "BillingBasis IN ('Trip', 'Volume')");
            });
            e.Property(v => v.Code).HasMaxLength(10).IsRequired();
            e.Property(v => v.VehicleNo).HasMaxLength(20).IsRequired();
            e.Property(v => v.NormalizedVehicleNo).HasMaxLength(20).IsRequired();
            e.Property(v => v.Nickname).HasMaxLength(80).IsRequired();
            e.Property(v => v.WaterDetails).HasMaxLength(120);
            e.Property(v => v.DriverName).HasMaxLength(80);
            e.Property(v => v.DriverPhone).HasMaxLength(15);
            e.Property(v => v.DriverLicenceNo).HasMaxLength(25);
            e.Property(v => v.Version).IsConcurrencyToken();
            e.Ignore(v => v.UnitOfMeasure);
            // Restrict: a supplier with vehicles can never be deleted from under them.
            e.HasOne(v => v.Supplier).WithMany().HasForeignKey(v => v.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => v.Code).IsUnique();
            e.HasIndex(v => v.NormalizedVehicleNo).IsUnique();
            e.HasIndex(v => new { v.SupplierId, v.Status });
        });

        ConfigureLedger(model);

        model.Entity<Invoice>(e =>
        {
            e.ToTable("Invoices", t =>
            {
                t.HasCheckConstraint("CK_Invoices_Amount", "AmountPaise > 0 AND RatePaise > 0");
                t.HasCheckConstraint("CK_Invoices_Trips", "Trips >= 1 AND VolumeTenths >= 0");
                t.HasCheckConstraint("CK_Invoices_Allocated", "AllocatedPaise >= 0 AND AllocatedPaise <= AmountPaise");
                // Exactly the right party for Bill To (Database Schema page 9).
                t.HasCheckConstraint("CK_Invoices_BillTo",
                    "(BillToType = 'Supplier' AND CustomerId IS NULL) OR (BillToType = 'Customer' AND CustomerId IS NOT NULL)");
            });
            e.Property(i => i.DisplayNo).HasMaxLength(20).IsRequired();
            e.Property(i => i.Material).HasMaxLength(120).IsRequired();
            e.Property(i => i.Narration).HasMaxLength(300);
            e.Property(i => i.CancelReason).HasMaxLength(300);
            e.Property(i => i.Version).IsConcurrencyToken();
            e.Ignore(i => i.OutstandingPaise);
            e.Ignore(i => i.Measurement);
            e.HasIndex(i => i.InvoiceNo).IsUnique();
            e.HasIndex(i => i.VoucherId).IsUnique();
            e.HasIndex(i => new { i.InvoiceDate, i.Status });
            e.HasIndex(i => new { i.SupplierId, i.InvoiceDate });
            e.HasIndex(i => new { i.CustomerId, i.InvoiceDate });
            e.HasIndex(i => new { i.VehicleId, i.InvoiceDate });
            e.HasOne<Vehicle>().WithMany().HasForeignKey(i => i.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Voucher>().WithMany().HasForeignKey(i => i.VoucherId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Voucher>().WithMany().HasForeignKey(i => i.ReversalVoucherId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ReceiptAllocation>(e =>
        {
            e.ToTable("ReceiptAllocations", t => t.HasCheckConstraint("CK_Allocations_Amount", "AmountPaise > 0"));
            e.HasIndex(a => a.VoucherId);
            e.HasIndex(a => new { a.InvoiceId, a.IsActive });
            e.HasOne<Voucher>().WithMany().HasForeignKey(a => a.VoucherId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Invoice>().WithMany().HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(a => a.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<User>(e =>
        {
            e.Property(u => u.UserName).HasMaxLength(50).IsRequired();
            e.Property(u => u.NormalizedUserName).HasMaxLength(50).IsRequired();
            e.Property(u => u.FullName).HasMaxLength(100).IsRequired();
            e.Property(u => u.Phone).HasMaxLength(15);
            e.Property(u => u.SecurityStamp).HasMaxLength(32).IsRequired();
            e.Property(u => u.Version).IsConcurrencyToken();
            e.HasIndex(u => u.NormalizedUserName).IsUnique();
        });

        model.Entity<ActivityLog>(e =>
        {
            e.Property(a => a.UserName).HasMaxLength(50).IsRequired();
            e.Property(a => a.Role).HasMaxLength(20);
            e.Property(a => a.Action).HasMaxLength(50).IsRequired();
            e.Property(a => a.EntityType).HasMaxLength(30);
            e.HasIndex(a => a.EventAtUtc);
            e.HasIndex(a => new { a.EntityType, a.EntityId });
        });
    }

    private static void ConfigureLedger(ModelBuilder model)
    {
        var seedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Account Seed(long id, string name, AccountType type, string? code) => new()
        {
            Id = id, Name = name, NormalizedName = Supplier.NormalizeName(name), Type = type, SystemCode = code,
            IsSystem = code is not null, CreatedAtUtc = seedTime, UpdatedAtUtc = seedTime, Version = 1,
        };

        model.Entity<Account>(e =>
        {
            e.ToTable("Accounts", t => t.HasCheckConstraint("CK_Accounts_Status", "Status IN ('Active', 'Inactive')"));
            e.Property(a => a.Name).HasMaxLength(80).IsRequired();
            e.Property(a => a.NormalizedName).HasMaxLength(80).IsRequired();
            e.Property(a => a.SystemCode).HasMaxLength(20);
            e.Property(a => a.Version).IsConcurrencyToken();
            e.HasIndex(a => a.NormalizedName).IsUnique();
            e.HasIndex(a => a.SystemCode).IsUnique();
            // Starting data (Database Schema section 5).
            e.HasData(
                Seed(SystemAccounts.Cash, "Cash in Hand", AccountType.Asset, "CASH"),
                Seed(SystemAccounts.Bank, "Bank", AccountType.Asset, "BANK"),
                Seed(SystemAccounts.Upi, "UPI Clearing", AccountType.Asset, "UPI"),
                Seed(SystemAccounts.SupplierReceivables, "Trade Receivables - Suppliers", AccountType.Asset, "AR_SUPPLIERS"),
                Seed(SystemAccounts.CustomerReceivables, "Trade Receivables - Customers", AccountType.Asset, "AR_CUSTOMERS"),
                Seed(SystemAccounts.WaterSales, "Water Sales", AccountType.Income, "SALES"),
                Seed(SystemAccounts.CashOverShort, "Cash Over/Short", AccountType.Expense, "CASH_OVER_SHORT"),
                Seed(SystemAccounts.OpeningBalanceEquity, "Opening Balance Equity", AccountType.Equity, "OPENING_EQUITY"),
                Seed(9, "Vehicle Maintenance", AccountType.Expense, null),
                Seed(10, "Fuel Expense", AccountType.Expense, null),
                Seed(11, "Oil Change", AccountType.Expense, null),
                Seed(12, "Others", AccountType.Expense, null));
        });

        model.Entity<NumberSeries>(e =>
        {
            e.HasKey(n => n.Code);
            e.Property(n => n.Code).HasMaxLength(10);
            e.Property(n => n.Prefix).HasMaxLength(6).IsRequired();
            e.ToTable(t => t.HasCheckConstraint("CK_NumberSeries_NextNo", "NextNo >= 1"));
            e.HasData(
                new NumberSeries { Code = "INVOICE", Prefix = "INV-", NextNo = 1 },
                new NumberSeries { Code = "RECEIPT", Prefix = "RV-", NextNo = 1 },
                new NumberSeries { Code = "PAYMENT", Prefix = "PV-", NextNo = 1 },
                new NumberSeries { Code = "JOURNAL", Prefix = "JV-", NextNo = 1 },
                new NumberSeries { Code = "REVERSAL", Prefix = "REV-", NextNo = 1 });
        });

        model.Entity<BusinessDay>(e =>
        {
            e.HasKey(d => d.Date);
            e.Property(d => d.DifferenceReason).HasMaxLength(300);
            e.Property(d => d.Version).IsConcurrencyToken();
            e.ToTable(t => t.HasCheckConstraint("CK_BusinessDays_Status", "Status IN ('Open', 'Closed')"));
        });

        model.Entity<Voucher>(e =>
        {
            e.ToTable("Vouchers", t =>
            {
                t.HasCheckConstraint("CK_Vouchers_Amount", "AmountPaise > 0");
                t.HasCheckConstraint("CK_Vouchers_OneParty", "SupplierId IS NULL OR CustomerId IS NULL");
            });
            e.Property(v => v.DisplayNo).HasMaxLength(20).IsRequired();
            e.Property(v => v.ReferenceNo).HasMaxLength(40);
            e.Property(v => v.PayTo).HasMaxLength(120);
            e.Property(v => v.Narration).HasMaxLength(300);
            e.Property(v => v.CancelReason).HasMaxLength(300);
            e.Property(v => v.Version).IsConcurrencyToken();
            e.HasIndex(v => new { v.Type, v.Number }).IsUnique();
            e.HasIndex(v => v.DisplayNo).IsUnique();
            e.HasIndex(v => v.ReversesVoucherId).IsUnique();   // a voucher can be reversed only once
            e.HasIndex(v => new { v.ValueDate, v.Type });
            e.HasIndex(v => new { v.SupplierId, v.VoucherDate });
            e.HasIndex(v => new { v.CustomerId, v.VoucherDate });
            e.HasOne<Supplier>().WithMany().HasForeignKey(v => v.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(v => v.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(v => v.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Account>().WithMany().HasForeignKey(v => v.PurposeAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(v => v.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Voucher>().WithMany().HasForeignKey(v => v.ReversesVoucherId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(v => v.Lines).WithOne().HasForeignKey(l => l.VoucherId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<LedgerEntry>(e =>
        {
            e.ToTable("LedgerEntries", t =>
            {
                // Exactly one side above zero; never negative.
                t.HasCheckConstraint("CK_Ledger_OneSide",
                    "(DebitPaise > 0 AND CreditPaise = 0) OR (CreditPaise > 0 AND DebitPaise = 0)");
                t.HasCheckConstraint("CK_Ledger_OneParty", "SupplierId IS NULL OR CustomerId IS NULL");
            });
            e.Property(l => l.Particulars).HasMaxLength(200).IsRequired();
            e.Property(l => l.ReferenceNo).HasMaxLength(40).IsRequired();
            e.HasIndex(l => l.PostingSeq).IsUnique();
            e.HasIndex(l => new { l.VoucherId, l.LineNo }).IsUnique();
            e.HasIndex(l => l.ReversesEntryId).IsUnique();
            // Statement indexes (Database Schema page 11).
            e.HasIndex(l => new { l.AccountId, l.SupplierId, l.CustomerId, l.PostingSeq });
            e.HasIndex(l => new { l.VehicleId, l.PostingSeq });
            e.HasIndex(l => new { l.AccountId, l.EntryDate, l.PostingSeq });
            e.HasIndex(l => new { l.ValueDate, l.AccountId });
            e.HasOne<Account>().WithMany().HasForeignKey(l => l.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(l => l.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(l => l.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(l => l.VehicleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(l => l.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LedgerEntry>().WithMany().HasForeignKey(l => l.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
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
        foreach (var entry in ChangeTracker.Entries<Customer>())
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

        // Business days and vouchers have a Version but no created/updated columns.
        foreach (var entry in ChangeTracker.Entries<BusinessDay>())
            if (entry.State == EntityState.Modified) entry.Entity.Version++;
        foreach (var entry in ChangeTracker.Entries<Voucher>())
            if (entry.State == EntityState.Modified) entry.Entity.Version++;
        foreach (var entry in ChangeTracker.Entries<Invoice>())
            if (entry.State == EntityState.Modified) entry.Entity.Version++;
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
