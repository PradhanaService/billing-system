using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Admin;

// Backups need a real database file, so this test does not use the in-memory database.
public class BackupServiceTests : IDisposable, IDbContextFactory<AppDbContext>
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wb-backup-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCurrentUser _me = new(new UserInfo(1, "admin", UserRole.Admin));   // backups are Admin only
    private readonly StoragePaths _paths;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _paths = new StoragePaths(Path.Combine(_folder, "live.db"), Path.Combine(_folder, "backups"), KeepBackups: 3);
        using var db = CreateDbContext();
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Suppliers.Add(new Supplier { Name = "Sangeetha Water Supply", ShortName = "S", Phone = "9876543210" });
        db.SaveChanges();
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_paths.DatabaseFile}").Options, _clock);

    private BackupService Svc => new(_paths, _me, _clock, this);

    [Fact]
    public async Task Backup_Is_A_Complete_Checked_Copy_And_Is_Logged()
    {
        var result = await Svc.RunAsync();

        Assert.True(result.Ok, result.Error);
        Assert.Equal("waterbilling-20261105-153000.db", result.Value!.Name);
        using (var copy = new SqliteConnection($"Data Source={Path.Combine(_paths.BackupFolder, result.Value.Name)};Mode=ReadOnly;Pooling=False"))
        {
            copy.Open();
            using var cmd = copy.CreateCommand();
            cmd.CommandText = "SELECT Name FROM Suppliers";
            Assert.Equal("Sangeetha Water Supply", cmd.ExecuteScalar());
        }
        using var db = CreateDbContext();
        Assert.Contains(db.ActivityLogs, l => l.Action == "BACKUP_CREATED");
        // One self-contained file: no WAL companions left beside it.
        Assert.Single(Directory.GetFiles(_paths.BackupFolder));
    }

    [Fact]
    public async Task Keeps_Only_The_Newest_Backups()
    {
        for (var i = 0; i < 5; i++)
        {
            await Svc.RunAsync();
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var files = await Svc.ListAsync();
        Assert.Equal(3, files.Count);
        Assert.Equal("waterbilling-20261105-153400.db", files[0].Name);
    }

    [Fact]
    public void Daily_Backup_Runs_Once_Per_Day()
    {
        Assert.NotNull(Svc.RunDailyIfMissing());
        _clock.Advance(TimeSpan.FromHours(2));
        Assert.Null(Svc.RunDailyIfMissing());
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.NotNull(Svc.RunDailyIfMissing());
    }

    [Fact]
    public async Task Billing_Clerk_Cannot_Run_Backups()
    {
        _me.User = new UserInfo(2, "clerk", UserRole.SalesPerson);
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.RunAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
