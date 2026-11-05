using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WaterBilling.Data;

namespace WaterBilling.Tests.Ledger;

// NFR-CON-03/04: two saves must never interleave, or numbers and running balances could clash.
// SQLite allows one writer; EF's transactions start with BEGIN IMMEDIATE, which takes the
// write lock at the start. This test proves that on a real database file.
public class ConcurrencyTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"wb-lock-{Guid.NewGuid():N}.db");

    private AppDbContext Open(int timeoutSeconds) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_file};Default Timeout={timeoutSeconds}").Options,
            new FakeTimeProvider());

    [Fact]
    public async Task NFR_CON_03_Second_Writer_Waits_For_The_First()
    {
        using (var setup = Open(5)) setup.Database.Migrate();

        using var first = Open(5);
        using var second = Open(1);
        await using var tx1 = await first.Database.BeginTransactionAsync();

        // The first transaction holds the write lock from its very start,
        // so the second cannot even begin until the first commits or rolls back.
        var ex = await Assert.ThrowsAsync<SqliteException>(() => second.Database.BeginTransactionAsync());
        Assert.Equal(5, ex.SqliteErrorCode);   // SQLITE_BUSY

        await tx1.CommitAsync();
        await using var tx2 = await second.Database.BeginTransactionAsync();   // now it can
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _file, _file + "-wal", _file + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }
}
