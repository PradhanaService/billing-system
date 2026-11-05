// Program.cs is the entry point. It does two jobs:
//   1. Register services (things other code can ask for, e.g. a database later).
//   2. Build the request pipeline (what happens to every browser request, in order).
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WaterBilling;
using WaterBilling.Data;

var builder = WebApplication.CreateBuilder(args);

// ---- 1. Services ----
// Blazor Server: pages render on the server and update the browser over a live connection.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The clock. Code asks for TimeProvider instead of calling DateTime.Now, so tests can fix the time.
builder.Services.AddSingleton(TimeProvider.System);

// Database: one SQLite file. Path comes from appsettings.json ("Database:Path"),
// relative to the app folder unless it is a full path.
var dbPath = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["Database:Path"] ?? "App_Data/waterbilling.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    ForeignKeys = true,          // the database enforces links between tables
}.ToString();
builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

var app = builder.Build();

// Create the database file, or bring it up to date, before the first request.
using (var scope = app.Services.CreateScope())
{
    using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    db.Database.Migrate();
    // WAL mode: readers do not wait for the writer, and a crash mid-save cannot corrupt the file.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// ---- 2. Request pipeline (order matters) ----
if (!app.Environment.IsDevelopment())
{
    // In production, never show error details to the user. Show the friendly /Error page instead.
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // HSTS tells the browser "only ever use HTTPS for this site".
    app.UseHsts();
}

// Basic security headers on every response.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";      // browser must not guess file types
    headers["X-Frame-Options"] = "DENY";                // page cannot be embedded in another site (clickjacking)
    headers["Referrer-Policy"] = "no-referrer";         // do not leak our URLs to other sites
    // Content Security Policy: the browser may only load scripts, styles, images and
    // connections from this app itself. Blocks injected scripts from running.
    // 'unsafe-inline' for styles only: Blazor's reconnect dialog uses inline styles.
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; connect-src 'self'; object-src 'none'; " +
        "base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    await next();
});

// Unknown URL (404): show our friendly "Page not found" page, keeping the 404 status code.
app.UseStatusCodePagesWithReExecute("/not-found");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();   // blocks forged form posts from other websites

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Makes the Program class visible to the test project, so tests can start the app in memory.
public partial class Program { }
