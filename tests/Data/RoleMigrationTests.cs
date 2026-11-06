using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Time.Testing;
using WaterBilling.Data;
using WaterBilling.Data.Entities;

namespace WaterBilling.Tests.Data;

// A shop database from before the two-role change still holds the old role names.
// Upgrading it must turn them into Sales Person, so nobody is locked out.
public class RoleMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public RoleMigrationTests() => _connection.Open();

    private AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, new FakeTimeProvider());

    [Fact]
    public void Old_Roles_Become_Sales_Person_On_Upgrade()
    {
        using (var db = Context())
        {
            db.GetService<IMigrator>().Migrate("20261105131423_AddReceiptAllocations");   // the database as it was before this change
            foreach (var (name, role) in new[] { ("op", "DispatchOperator"), ("clerk", "BillingClerk"), ("acct", "Accountant"), ("boss", "Admin") })
            {
                db.Database.ExecuteSqlRaw(
                    "INSERT INTO Users (UserName, NormalizedUserName, FullName, PasswordHash, Role, MustChangePassword, SecurityStamp, Status, CreatedAtUtc, UpdatedAtUtc, Version) " +
                    "VALUES ({0}, {1}, {0}, 'x', {2}, 0, 'stamp', 'Active', '2026-01-01 00:00:00', '2026-01-01 00:00:00', 1)",
                    name, name.ToUpperInvariant(), role);
            }
        }

        using (var db = Context()) db.Database.Migrate();   // upgrade to the latest

        using var check = Context();
        var roles = check.Users.OrderBy(u => u.UserName).Select(u => new { u.UserName, u.Role }).ToList();
        Assert.Equal(UserRole.SalesPerson, roles.Single(r => r.UserName == "op").Role);
        Assert.Equal(UserRole.SalesPerson, roles.Single(r => r.UserName == "clerk").Role);
        Assert.Equal(UserRole.SalesPerson, roles.Single(r => r.UserName == "acct").Role);
        Assert.Equal(UserRole.Admin, roles.Single(r => r.UserName == "boss").Role);
    }

    public void Dispose() => _connection.Dispose();
}
