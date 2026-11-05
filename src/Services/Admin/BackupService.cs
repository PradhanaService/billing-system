using Microsoft.Data.Sqlite;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Admin;

// Where the database and its backups live (set in Program.cs from appsettings.json).
public sealed record StoragePaths(string DatabaseFile, string BackupFolder, int KeepBackups);

public sealed record BackupFile(string Name, long SizeBytes, DateTime CreatedUtc);

// "Backup now" (CLAUDE.md section 4). Uses SQLite's online backup, which copies a consistent
// snapshot even while the app is running in WAL mode. A plain file copy could miss the -wal file.
public class BackupService(StoragePaths paths, ICurrentUser currentUser, TimeProvider clock,
    Microsoft.EntityFrameworkCore.IDbContextFactory<WaterBilling.Data.AppDbContext> dbFactory)
{
    public async Task<Result<BackupFile>> RunAsync()
    {
        var me = await currentUser.RequireAsync(Permission.BackupRun);
        var result = CreateBackup();
        if (result.Ok)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, me, ActivityCategory.Admin, "BACKUP_CREATED",
                after: new { result.Value!.Name, result.Value.SizeBytes });
            await db.SaveChangesAsync();
        }
        return result;
    }

    // Startup: one automatic backup per calendar day (IST), no user involved.
    public Result<BackupFile>? RunDailyIfMissing()
    {
        var today = IndianTime.ToIst(clock.GetUtcNow().UtcDateTime).ToString("yyyyMMdd");
        return List().Any(f => f.Name.Contains($"-{today}-")) ? null : CreateBackup();
    }

    public async Task<IReadOnlyList<BackupFile>> ListAsync()
    {
        await currentUser.RequireAsync(Permission.BackupRun);
        return List();
    }

    private IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(paths.BackupFolder)) return [];
        return new DirectoryInfo(paths.BackupFolder).GetFiles("waterbilling-*.db")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupFile(f.Name, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    private Result<BackupFile> CreateBackup()
    {
        Directory.CreateDirectory(paths.BackupFolder);
        var stamp = IndianTime.ToIst(clock.GetUtcNow().UtcDateTime).ToString("yyyyMMdd-HHmmss");
        var name = $"waterbilling-{stamp}.db";
        var target = Path.Combine(paths.BackupFolder, name);
        if (File.Exists(target)) return Result<BackupFile>.Fail("A backup was taken this second. Try again.");

        using (var source = new SqliteConnection($"Data Source={paths.DatabaseFile}"))
        using (var destination = new SqliteConnection($"Data Source={target};Pooling=False"))
        {
            source.Open();
            destination.Open();
            source.BackupDatabase(destination);
            // The copy inherits WAL mode. Switch it back so the backup is one single file
            // (no -wal / -shm companions) that can be copied to a USB drive as it is.
            using var cmd = destination.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=DELETE;";
            cmd.ExecuteNonQuery();
        }

        // Check the copy is readable before calling it a backup.
        using (var check = new SqliteConnection($"Data Source={target};Mode=ReadOnly;Pooling=False"))
        {
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            if (cmd.ExecuteScalar() as string != "ok")
            {
                File.Delete(target);
                return Result<BackupFile>.Fail("The backup copy failed its integrity check and was removed.");
            }
        }

        // Keep the newest N backups (default 60): enough for about two months of daily copies.
        foreach (var old in List().Skip(paths.KeepBackups)) File.Delete(Path.Combine(paths.BackupFolder, old.Name));

        var info = new FileInfo(target);
        return Result<BackupFile>.Success(new BackupFile(name, info.Length, info.CreationTimeUtc));
    }
}
