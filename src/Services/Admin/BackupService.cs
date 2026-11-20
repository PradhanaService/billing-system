using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Admin;

// Where the database and its backups live (set in Program.cs from appsettings.json).
// BackupFolder is the default; the Admin can choose another folder on the Backup & Restore page.
public sealed record StoragePaths(string DatabaseFile, string BackupFolder, int KeepBackups);

// How a backup was taken: read from the end of its file name.
public enum BackupHow { BackupNow, Automatic, BeforeRestore, BeforeDeleteAll }

public sealed record BackupFile(string Name, long SizeBytes, DateTime CreatedUtc, BackupHow How = BackupHow.BackupNow)
{
    public string HowText => How switch
    {
        BackupHow.Automatic => "Automatic",
        BackupHow.BeforeRestore => "Safety copy (before restore)",
        BackupHow.BeforeDeleteAll => "Safety copy (before delete all)",
        _ => "Backup now",
    };
}

// Backup & Restore (Admin only, final design Nov 2026):
//  - Backup now: SQLite's online backup copies a consistent snapshot even while the app runs in WAL mode.
//  - Backup folder: chosen by the Admin (typed path, checked for writing), remembered in a small file
//    next to the database.
//  - Restore: a backup from the list or a file the Admin uploads. The file is checked first, a safety copy
//    of the current database is taken, then the backup is copied in. Everyone is signed out.
//  - Delete all data: after a safety copy and the Admin password, every record is removed except the
//    two logins. The ledger's no-delete guard is lifted only inside that one transaction.
public class BackupService(StoragePaths paths, ICurrentUser currentUser, TimeProvider clock,
    IDbContextFactory<WaterBilling.Data.AppDbContext> dbFactory)
{
    public const string RestoreWord = "RESTORE";
    public const string DeleteAllWords = "DELETE ALL DATA";

    // Tables that must contain these for a file to count as a Water Billing backup.
    private static readonly string[] RequiredTables = ["__EFMigrationsHistory", "Users", "Invoices", "LedgerEntries", "Vouchers"];
    // Delete all data keeps these tables (the logins and the database version)...
    private static readonly string[] KeptTables = ["__EFMigrationsHistory", "Users", "sqlite_sequence"];
    private const long LastSeededAccountId = 12;   // ...and the built-in ledgers (ids 1-12) and number series.

    private string SettingFile => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(paths.DatabaseFile))!, "backup-folder.txt");

    // The folder backups go to now: the Admin's choice if any, else the default from appsettings.json.
    public string Folder
    {
        get
        {
            try { if (File.Exists(SettingFile) && File.ReadAllText(SettingFile).Trim() is { Length: > 0 } chosen) return chosen; }
            catch (IOException) { }
            return paths.BackupFolder;
        }
    }

    public async Task<Result<BackupFile>> RunAsync()
    {
        var me = await currentUser.RequireAsync(Permission.BackupRun);
        var result = CreateBackup(BackupHow.BackupNow);
        if (result.Ok) await LogAsync(me, "BACKUP_CREATED", new { result.Value!.Name, result.Value.SizeBytes, Folder });
        return result;
    }

    // Startup: one automatic backup per calendar day (IST), no user involved.
    public Result<BackupFile>? RunDailyIfMissing()
    {
        var today = IndianTime.ToIst(clock.GetUtcNow().UtcDateTime).ToString("yyyyMMdd");
        return List().Any(f => f.Name.Contains($"-{today}-")) ? null : CreateBackup(BackupHow.Automatic);
    }

    public async Task<IReadOnlyList<BackupFile>> ListAsync()
    {
        await currentUser.RequireAsync(Permission.BackupRun);
        return List();
    }

    // "Use this folder": the folder must be a full path we can create and write to.
    public async Task<Result> SetFolderAsync(string? folder)
    {
        var me = await currentUser.RequireAsync(Permission.BackupRun);
        folder = folder?.Trim().Trim('"');
        if (string.IsNullOrEmpty(folder)) return Result.Fail("Type the backup folder, for example D:\\SKT-Backups.");
        if (!Path.IsPathFullyQualified(folder)) return Result.Fail("Type the full folder path, starting with the drive, for example D:\\SKT-Backups.");
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-check-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Result.Fail($"The app cannot write to {folder}. Choose another folder or check the pen drive is plugged in.");
        }
        var before = Folder;
        await File.WriteAllTextAsync(SettingFile, folder);
        await LogAsync(me, "BACKUP_FOLDER_CHANGED", new { Folder = folder }, before: new { Folder = before });
        return Result.Success();
    }

    // Restore a backup from the list (by its file name).
    public async Task<Result> RestoreFromListAsync(string name, string? confirm)
    {
        await currentUser.RequireAsync(Permission.BackupRun);
        if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || List().All(f => f.Name != name))
            return Result.Fail("That backup is not in the backup folder.");
        return await RestoreAsync(Path.Combine(Folder, name), name, confirm);
    }

    // Restore a backup file the Admin chose with Browse (uploaded by the browser).
    public async Task<Result> RestoreFromUploadAsync(Stream upload, string fileName, string? confirm)
    {
        await currentUser.RequireAsync(Permission.BackupRun);
        if (confirm?.Trim() != RestoreWord) return Result.Fail($"Type {RestoreWord} to confirm.");
        Directory.CreateDirectory(Folder);
        var temp = Path.Combine(Folder, $"upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = File.Create(temp)) await upload.CopyToAsync(file);
            return await RestoreAsync(temp, Path.GetFileName(fileName), confirm);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<Result<BackupFile>> DeleteAllDataAsync(string? adminPassword, string? confirm)
    {
        var me = await currentUser.RequireAsync(Permission.BackupRun);
        if (me.Role != UserRole.Admin) throw new NotAllowedException(Permission.BackupRun);   // the Admin login only
        if (confirm?.Trim() != DeleteAllWords) return Result<BackupFile>.Fail($"Type {DeleteAllWords} to confirm.");
        await using (var check = await dbFactory.CreateDbContextAsync())
        {
            var admin = await check.Users.SingleOrDefaultAsync(u => u.Id == me.Id);
            if (admin is null || !AuthService.VerifyPassword(admin, adminPassword)) return Result<BackupFile>.Fail("The Admin password is wrong.");
        }

        var safety = CreateBackup(BackupHow.BeforeDeleteAll);   // always first (design: "Take a backup first", always on)
        if (!safety.Ok) return Result<BackupFile>.Fail("The safety backup failed, so nothing was deleted. " + safety.Error);

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var conn = db.Database.GetDbConnection();
        List<(string Name, string Sql)> triggers = [];
        List<string> tables = [];
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
            cmd.CommandText = "SELECT type, name, sql FROM sqlite_master WHERE type IN ('table', 'trigger')";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                if (r.GetString(0) == "trigger") triggers.Add((r.GetString(1), r.GetString(2)));
                else tables.Add(r.GetString(1));
            }
        }
        await db.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = ON;");
        foreach (var t in triggers) await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER \"{t.Name}\";");
        foreach (var table in tables.Where(t => !KeptTables.Contains(t) && t is not ("Accounts" or "NumberSeries")))
        {
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{table}\";");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM sqlite_sequence WHERE name = {0};", table);   // numbering starts again from 1
        }
        await db.Database.ExecuteSqlRawAsync($"DELETE FROM Accounts WHERE Id > {LastSeededAccountId};");
        await db.Database.ExecuteSqlRawAsync("UPDATE NumberSeries SET NextNo = 1;");
        foreach (var t in triggers) await db.Database.ExecuteSqlRawAsync(t.Sql);   // the ledger guard is back
        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, me, ActivityCategory.Admin, "ALL_DATA_DELETED",
            after: new { SafetyBackup = safety.Value!.Name, Folder });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return safety;
    }

    // ---------- inside ----------

    private async Task<Result> RestoreAsync(string source, string shownName, string? confirm)
    {
        var me = await currentUser.RequireAsync(Permission.BackupRun);
        if (confirm?.Trim() != RestoreWord) return Result.Fail($"Type {RestoreWord} to confirm.");
        if (CheckBackupFile(source) is { } problem) return Result.Fail(problem);

        var safety = CreateBackup(BackupHow.BeforeRestore);
        if (!safety.Ok) return Result.Fail("The safety copy of the current database failed, so nothing was restored. " + safety.Error);

        using (var from = new SqliteConnection($"Data Source={source};Mode=ReadOnly;Pooling=False"))
        using (var to = new SqliteConnection($"Data Source={paths.DatabaseFile};Pooling=False"))
        {
            from.Open();
            to.Open();
            from.BackupDatabase(to);
        }
        SqliteConnection.ClearAllPools();

        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();                                   // an older backup is brought up to date
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
        // Everyone is signed out: every login gets a new stamp, so old cookies stop working.
        foreach (var u in await db.Users.ToListAsync()) u.SecurityStamp = Guid.NewGuid().ToString("N");
        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, me, ActivityCategory.Admin, "BACKUP_RESTORED",
            after: new { Restored = shownName, SafetyCopy = safety.Value!.Name });
        await db.SaveChangesAsync();
        return Result.Success();
    }

    // A file is restorable only if SQLite can read it, it passes the integrity check and it has our tables.
    private static string? CheckBackupFile(string file)
    {
        try
        {
            using var c = new SqliteConnection($"Data Source={file};Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            if (cmd.ExecuteScalar() as string != "ok") return "This backup file is damaged and cannot be restored.";
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            var have = new HashSet<string>();
            using (var r = cmd.ExecuteReader()) while (r.Read()) have.Add(r.GetString(0));
            return RequiredTables.All(have.Contains) ? null : "This file is not a Water Billing backup.";
        }
        catch (SqliteException)
        {
            return "This file is not a Water Billing backup.";
        }
    }

    private async Task LogAsync(UserInfo me, string action, object after, object? before = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        ActivityLogger.Add(db, clock.GetUtcNow().UtcDateTime, me, ActivityCategory.Admin, action, before: before, after: after);
        await db.SaveChangesAsync();
    }

    private IReadOnlyList<BackupFile> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return new DirectoryInfo(Folder).GetFiles("waterbilling-*.db")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupFile(f.Name, f.Length, f.CreationTimeUtc, HowOf(f.Name)))
            .ToList();
    }

    private static BackupHow HowOf(string name) =>
        name.EndsWith("-auto.db") ? BackupHow.Automatic
        : name.EndsWith("-before-restore.db") ? BackupHow.BeforeRestore
        : name.EndsWith("-before-delete-all.db") ? BackupHow.BeforeDeleteAll
        : BackupHow.BackupNow;

    private Result<BackupFile> CreateBackup(BackupHow how)
    {
        var folder = Folder;
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<BackupFile>.Fail($"The backup folder {folder} cannot be used. Choose another folder.");
        }
        var stamp = IndianTime.ToIst(clock.GetUtcNow().UtcDateTime).ToString("yyyyMMdd-HHmmss");
        var suffix = how switch { BackupHow.Automatic => "-auto", BackupHow.BeforeRestore => "-before-restore", BackupHow.BeforeDeleteAll => "-before-delete-all", _ => "" };
        var name = $"waterbilling-{stamp}{suffix}.db";
        var target = Path.Combine(folder, name);
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
        foreach (var old in List().Skip(paths.KeepBackups)) File.Delete(Path.Combine(folder, old.Name));

        var info = new FileInfo(target);
        return Result<BackupFile>.Success(new BackupFile(name, info.Length, info.CreationTimeUtc, how));
    }
}
