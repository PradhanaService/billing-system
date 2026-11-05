using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Data.Entities;

namespace WaterBilling.Tests.Data;

// Session 3: the Suppliers table enforces its rules even without the service layer.
public class SupplierTableTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static Supplier NewSupplier(string name = "Sangeetha Water Supply") => new()
    {
        Name = name,
        ShortName = "Sangeetha",
        Phone = "9876543210",
    };

    [Fact]
    public void Migrations_Create_The_Suppliers_Table()
    {
        using var ctx = _db.NewContext();

        Assert.Empty(ctx.Database.GetPendingMigrations());
        Assert.Equal(0, ctx.Suppliers.Count());
    }

    [Theory]
    [InlineData("SANGEETHA WATER SUPPLY")]
    [InlineData("  sangeetha   water supply ")]
    public void FR_VM_01_Supplier_Name_Is_Unique_Ignoring_Case_And_Spaces(string duplicate)
    {
        using (var ctx = _db.NewContext())
        {
            ctx.Suppliers.Add(NewSupplier());
            ctx.SaveChanges();
        }

        using var ctx2 = _db.NewContext();
        ctx2.Suppliers.Add(NewSupplier(duplicate));

        var ex = Assert.Throws<DbUpdateException>(() => ctx2.SaveChanges());
        Assert.Contains("UNIQUE", ex.InnerException!.Message);
    }

    [Fact]
    public void Money_Is_Stored_Exactly_As_Paise()
    {
        // ₹92,23,37,20,36,85,477.58 is near the top of a long; any floating-point step would lose paise.
        const long hugePaise = 9_223_372_036_854_775_758;
        using (var ctx = _db.NewContext())
        {
            var s = NewSupplier();
            s.OpeningBalancePaise = hugePaise;
            ctx.Suppliers.Add(s);
            ctx.SaveChanges();
        }

        using var read = _db.NewContext();
        Assert.Equal(hugePaise, read.Suppliers.Single().OpeningBalancePaise);
    }

    [Fact]
    public void Database_Refuses_Negative_Opening_Balance()
    {
        using var ctx = _db.NewContext();
        var s = NewSupplier();
        s.OpeningBalancePaise = -1;
        ctx.Suppliers.Add(s);

        var ex = Assert.Throws<DbUpdateException>(() => ctx.SaveChanges());
        Assert.Contains("CHECK", ex.InnerException!.Message);
    }

    [Fact]
    public void Audit_Columns_Are_Filled_In_Utc_And_Version_Rises()
    {
        using (var ctx = _db.NewContext())
        {
            ctx.Suppliers.Add(NewSupplier());
            ctx.SaveChanges();
        }

        _db.Clock.Advance(TimeSpan.FromHours(1));
        using (var ctx = _db.NewContext())
        {
            var s = ctx.Suppliers.Single();
            s.Phone = "9000000000";
            ctx.SaveChanges();
        }

        using var read = _db.NewContext();
        var saved = read.Suppliers.Single();
        Assert.Equal(DateTimeKind.Utc, saved.CreatedAtUtc.Kind);
        Assert.Equal(new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc), saved.CreatedAtUtc);
        Assert.Equal(new DateTime(2026, 11, 5, 11, 0, 0, DateTimeKind.Utc), saved.UpdatedAtUtc);
        Assert.Equal(2, saved.Version);
        Assert.Equal("SANGEETHA WATER SUPPLY", saved.NormalizedName);
    }

    [Fact]
    public void NFR_CON_05_Second_Editor_Of_Same_Supplier_Is_Refused()
    {
        using (var ctx = _db.NewContext())
        {
            ctx.Suppliers.Add(NewSupplier());
            ctx.SaveChanges();
        }

        // Two screens open the same supplier.
        using var screenA = _db.NewContext();
        using var screenB = _db.NewContext();
        var a = screenA.Suppliers.Single();
        var b = screenB.Suppliers.Single();

        a.Phone = "9111111111";
        screenA.SaveChanges();            // first save wins

        b.Phone = "9222222222";
        Assert.Throws<DbUpdateConcurrencyException>(() => screenB.SaveChanges());
    }

    [Fact]
    public void Saving_A_Time_Without_Zone_Is_Refused()
    {
        using var ctx = _db.NewContext();
        var s = NewSupplier();
        ctx.Suppliers.Add(s);
        ctx.SaveChanges();

        // Force an unzoned time past the automatic stamping.
        ctx.Entry(s).Property(x => x.CreatedAtUtc).CurrentValue = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.ThrowsAny<Exception>(() => ctx.SaveChanges());
    }
}
