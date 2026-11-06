using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Data;

// A real SQLite database that lives in memory for one test.
// It stays alive while the connection is open, and is built from the real migrations,
// so tests check the same tables, indexes, checks and triggers the app uses.
public sealed class TestDatabase : IDisposable, IDbContextFactory<AppDbContext>
{
    private readonly SqliteConnection _connection;

    // A clock the test controls. Starts at 05/11/2026 10:00 UTC (3:30 PM IST).
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();
        using var db = NewContext();
        db.Database.Migrate();
    }

    // Raised after every committed save, as in the app (Data/DataChanges.cs).
    public DataChangeNotifier Changes { get; } = new();

    // Each call gives a fresh context on the same database, like two separate screens.
    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)
            .AddInterceptors(new DataChangeInterceptor(Changes)).Options, Clock);

    // Lets services use this database exactly as they use the real one.
    public AppDbContext CreateDbContext() => NewContext();

    // Adds a user row and returns who they are, for tests that need a logged-in person.
    public UserInfo AddUser(UserRole role, string userName = "")
    {
        using var db = NewContext();
        if (userName == "") userName = role.ToString().ToLowerInvariant() + db.Users.Count();
        var user = new User { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), FullName = userName, Role = role };
        user.PasswordHash = AuthService.HashPassword(user, "Passw0rd!");
        db.Users.Add(user);
        db.SaveChanges();
        return new UserInfo(user.Id, user.UserName, role);
    }

    public void Dispose() => _connection.Dispose();
}

// Stands in for the logged-in person in service tests.
public sealed class FakeCurrentUser(UserInfo? user) : ICurrentUser
{
    public UserInfo? User { get; set; } = user;
    public Task<UserInfo?> GetAsync() => Task.FromResult(User);
}
