using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WaterBilling.Tests;

// Starts the whole app in memory for web tests, with its own throw-away database file,
// so tests never touch the real App_Data database.
public class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbFolder = Path.Combine(Path.GetTempPath(), "waterbilling-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Path", Path.Combine(_dbFolder, "test.db"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();   // release the file before deleting
        if (Directory.Exists(_dbFolder)) Directory.Delete(_dbFolder, recursive: true);
    }
}
