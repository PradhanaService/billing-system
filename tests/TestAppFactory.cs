using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WaterBilling.Tests;

// Starts the whole app in memory for web tests, with its own throw-away database file,
// so tests never touch the real App_Data database.
public class TestAppFactory : WebApplicationFactory<Program>
{
    public const string SeedPassword = "Seed@1234";
    public const string AdminPassword = "Admin@5678";

    private readonly string _dbFolder = Path.Combine(Path.GetTempPath(), "waterbilling-tests", Guid.NewGuid().ToString("N"));
    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private bool _adminPasswordChanged;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Path", Path.Combine(_dbFolder, "test.db"));
        builder.UseSetting("Seed:AdminPassword", SeedPassword);
        builder.UseSetting("Backup:Folder", Path.Combine(_dbFolder, "backups"));
    }

    public HttpClient NewClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    // A client logged in as "admin". The first call also does the forced password change.
    public async Task<HttpClient> AdminClientAsync()
    {
        await _setupLock.WaitAsync();
        try
        {
            var client = NewClient();
            if (!_adminPasswordChanged)
            {
                var login = await PostFormAsync(client, "/account/login", "login",
                    ("Input.UserName", "admin"), ("Input.Password", SeedPassword));
                if (login.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException("Seed login failed");
                var change = await PostFormAsync(client, "/account/change-password", "change-password",
                    ("Input.Current", SeedPassword), ("Input.New", AdminPassword), ("Input.Confirm", AdminPassword));
                if (change.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException("Password change failed");
                _adminPasswordChanged = true;
                return client;
            }
            var again = await PostFormAsync(client, "/account/login", "login",
                ("Input.UserName", "admin"), ("Input.Password", AdminPassword));
            if (again.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException("Admin login failed");
            return client;
        }
        finally { _setupLock.Release(); }
    }

    // Fills in a server-rendered Blazor form like a browser: reads the anti-forgery token
    // from the page, then posts the fields with the form's name.
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, string formName,
        params (string Name, string Value)[] fields)
    {
        var page = await client.GetStringAsync(url);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        var data = new List<KeyValuePair<string, string>>
        {
            new("_handler", formName),
            new("__RequestVerificationToken", token),
        };
        data.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();   // release the file before deleting
        if (Directory.Exists(_dbFolder)) Directory.Delete(_dbFolder, recursive: true);
    }
}
