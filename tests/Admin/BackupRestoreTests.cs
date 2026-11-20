using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Admin;

// Module 10: Backup & Restore (folder choice, restore, delete all data). Uses a real database file,
// with real invoices and ledger lines made through the services.
public class BackupRestoreTests : IDisposable, IDbContextFactory<AppDbContext>
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wb-restore-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeCurrentUser _me;
    private readonly StoragePaths _paths;
    private readonly long _vehicle, _seller;
    private const string AdminPassword = "Admin@1234";

    public BackupRestoreTests()
    {
        Directory.CreateDirectory(_folder);
        _paths = new StoragePaths(Path.Combine(_folder, "live.db"), Path.Combine(_folder, "backups"), KeepBackups: 10);
        using (var db = CreateDbContext())
        {
            db.Database.Migrate();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            var admin = new User { UserName = "admin", NormalizedUserName = "ADMIN", FullName = "Admin", Role = UserRole.Admin, MustChangePassword = false };
            admin.PasswordHash = AuthService.HashPassword(admin, AdminPassword);
            var sales = new User { UserName = "sales", NormalizedUserName = "SALES", FullName = "Sales", Role = UserRole.SalesPerson };
            sales.PasswordHash = AuthService.HashPassword(sales, "Sales@1234");
            db.Users.AddRange(admin, sales);
            db.SalesPersons.Add(new SalesPerson { Name = "Ravi", NormalizedName = "RAVI" });
            db.SaveChanges();
            _me = new FakeCurrentUser(new UserInfo(admin.Id, "admin", UserRole.Admin));
            _seller = db.SalesPersons.Single().Id;
        }
        var supplier = Suppliers.CreateAsync(new SupplierForm { Name = "Kaveri Tankers", ShortName = "Kaveri", Phone = "9123456780" }).Result.Value;
        _vehicle = new VehicleService(this, _me, _clock).CreateAsync(new VehicleForm { Code = "A2", VehicleNo = "TN 67 C", SupplierId = supplier, TankCapacityLitres = 24000, Rate = 150 }).Result.Value;
        Invoice();
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_paths.DatabaseFile}").Options, _clock);

    private BackupService Svc => new(_paths, _me, _clock, this);
    private LedgerPoster Poster => new(_clock, new BusinessCalendar(_clock));
    private SupplierService Suppliers => new(this, _me, _clock, Poster);
    private void Invoice() => Assert.True(new InvoiceService(this, _me, _clock, Poster, new BusinessCalendar(_clock))
        .CreateAsync(new InvoiceForm { VehicleId = _vehicle, Rate = 150, SalesPersonId = _seller }).Result.Ok);
    private int Invoices() { using var db = CreateDbContext(); return db.Invoices.Count(); }

    [Fact]
    public async Task BKP_01_Admin_Chooses_The_Backup_Folder_And_It_Is_Checked()
    {
        var chosen = Path.Combine(_folder, "pen-drive", "SKT-Backups");
        Assert.False((await Svc.SetFolderAsync("SKT-Backups")).Ok);      // not a full path
        Assert.False((await Svc.SetFolderAsync("")).Ok);

        Assert.True((await Svc.SetFolderAsync(chosen)).Ok);

        Assert.Equal(chosen, Svc.Folder);
        var backup = await Svc.RunAsync();
        Assert.True(File.Exists(Path.Combine(chosen, backup.Value!.Name)));
        using var db = CreateDbContext();
        Assert.Contains(db.ActivityLogs, l => l.Action == "BACKUP_FOLDER_CHANGED" && l.AfterJson!.Contains("SKT-Backups"));
    }

    [Fact]
    public async Task BKP_02_Backups_Say_How_They_Were_Taken()
    {
        Svc.RunDailyIfMissing();
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Svc.RunAsync();
        var files = await Svc.ListAsync();
        Assert.Equal(["Backup now", "Automatic"], files.Select(f => f.HowText));
    }

    [Fact]
    public async Task BKP_03_Restore_From_The_List_Puts_The_Old_Data_Back_After_A_Safety_Copy()
    {
        var backup = (await Svc.RunAsync()).Value!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        Invoice();
        Assert.Equal(2, Invoices());
        string stampBefore;
        using (var db = CreateDbContext()) stampBefore = db.Users.Single(u => u.UserName == "sales").SecurityStamp;

        Assert.False((await Svc.RestoreFromListAsync(backup.Name, "restore please")).Ok);   // must type RESTORE
        Assert.False((await Svc.RestoreFromListAsync("..\\live.db", "RESTORE")).Ok);         // only files in the list
        var result = await Svc.RestoreFromListAsync(backup.Name, "RESTORE");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, Invoices());
        var files = await Svc.ListAsync();
        Assert.Contains(files, f => f.How == BackupHow.BeforeRestore);                     // the safety copy has 2 invoices
        using var check = CreateDbContext();
        Assert.NotEqual(stampBefore, check.Users.Single(u => u.UserName == "sales").SecurityStamp);   // everyone signed out
        Assert.Contains(check.ActivityLogs, l => l.Action == "BACKUP_RESTORED");
        Assert.Equal(check.LedgerEntries.Sum(e => e.DebitPaise), check.LedgerEntries.Sum(e => e.CreditPaise));
    }

    [Fact]
    public async Task BKP_04_Restore_From_A_Chosen_File_Checks_It_Is_A_Backup()
    {
        var backup = (await Svc.RunAsync()).Value!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        Invoice();

        await using (var junk = new MemoryStream("not a database"u8.ToArray()))
        {
            var bad = await Svc.RestoreFromUploadAsync(junk, "photo.db", "RESTORE");
            Assert.False(bad.Ok);
            Assert.Contains("not a Water Billing backup", bad.Error);
        }
        Assert.Equal(2, Invoices());

        await using var file = File.OpenRead(Path.Combine(Svc.Folder, backup.Name));
        Assert.True((await Svc.RestoreFromUploadAsync(file, backup.Name, "RESTORE")).Ok);
        Assert.Equal(1, Invoices());
        Assert.DoesNotContain(Directory.GetFiles(Svc.Folder), f => f.EndsWith(".tmp"));
    }

    [Fact]
    public async Task BKP_05_Delete_All_Data_Keeps_Logins_And_Backups_And_The_Ledger_Guard()
    {
        Assert.False((await Svc.DeleteAllDataAsync(AdminPassword, "delete")).Ok);
        Assert.Contains("password", (await Svc.DeleteAllDataAsync("wrong", "DELETE ALL DATA")).Error);
        Assert.Equal(1, Invoices());

        var result = await Svc.DeleteAllDataAsync(AdminPassword, "DELETE ALL DATA");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(BackupHow.BeforeDeleteAll, result.Value!.How);
        using (var db = CreateDbContext())
        {
            Assert.Empty(db.Invoices);
            Assert.Empty(db.LedgerEntries);
            Assert.Empty(db.Vouchers);
            Assert.Empty(db.Suppliers.IgnoreQueryFilters());
            Assert.Empty(db.Vehicles.IgnoreQueryFilters());
            Assert.Empty(db.SalesPersons.IgnoreQueryFilters());
            Assert.Equal(2, db.Users.Count());                                       // both logins kept
            Assert.Equal(12, db.Accounts.IgnoreQueryFilters().Count());               // built-in ledgers kept
            Assert.All(db.NumberSeries, s => Assert.Equal(1, s.NextNo));             // numbering from 1 again
            Assert.Equal("ALL_DATA_DELETED", db.ActivityLogs.Single().Action);
            // The ledger is still append-only after the reset.
            var triggers = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'trigger'").ToList();
            Assert.Equal(4, triggers.Count);
        }
        Assert.Contains(await Svc.ListAsync(), f => f.Name == result.Value.Name);  // the safety backup is kept

        // The app works from zero: a new supplier and invoice get number 1.
        using (var db = CreateDbContext())
        {
            db.SalesPersons.Add(new SalesPerson { Name = "Ravi", NormalizedName = "RAVI" });
            db.SaveChanges();
        }
        var supplier = (await Suppliers.CreateAsync(new SupplierForm { Name = "City Water", ShortName = "City", Phone = "9000000000" })).Value;
        var vehicle = (await new VehicleService(this, _me, _clock).CreateAsync(new VehicleForm { Code = "C1", VehicleNo = "TN 01 A", SupplierId = supplier, TankCapacityLitres = 9000, Rate = 100 })).Value;
        var inv = await new InvoiceService(this, _me, _clock, Poster, new BusinessCalendar(_clock)).CreateAsync(new InvoiceForm { VehicleId = vehicle, Rate = 100 });
        Assert.True(inv.Ok, inv.Error);
        using var after = CreateDbContext();
        Assert.Equal("1", after.Invoices.Single().DisplayNo);
        Assert.Throws<DbUpdateException>(() => { after.LedgerEntries.RemoveRange(after.LedgerEntries); after.SaveChanges(); });
    }

    [Fact]
    public async Task NFR_SEC_05_Sales_Login_Cannot_Restore_Or_Delete_All()
    {
        var backup = (await Svc.RunAsync()).Value!;
        _me.User = new UserInfo(2, "sales", UserRole.SalesPerson);
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.RestoreFromListAsync(backup.Name, "RESTORE"));
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.DeleteAllDataAsync("Sales@1234", "DELETE ALL DATA"));
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.SetFolderAsync(_folder));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
