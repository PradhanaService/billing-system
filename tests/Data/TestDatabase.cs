using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WaterBilling.Data;

namespace WaterBilling.Tests.Data;

// A real SQLite database that lives in memory for one test.
// It stays alive while the connection is open, and is built from the real migrations,
// so tests check the same tables, indexes and checks the app uses.
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    // A clock the test controls. Starts at 05/11/2026 10:00 UTC.
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        _connection.Open();
        using var db = NewContext();
        db.Database.Migrate();
    }

    // Each call gives a fresh context on the same database, like two separate screens.
    public AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, Clock);

    public void Dispose() => _connection.Dispose();
}
