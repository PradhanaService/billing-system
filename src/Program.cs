// Program.cs is the entry point. It does two jobs:
//   1. Register services (things other code can ask for).
//   2. Build the request pipeline (what happens to every browser request, in order).
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WaterBilling;
using WaterBilling.Data;
using WaterBilling.Services;
using WaterBilling.Services.Security;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // When installed as a Windows service, files are found next to the .exe, not in C:\Windows\System32.
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
        ? AppContext.BaseDirectory : null,
});

// Installed on the shop PC it runs as a Windows service, so it starts with the computer.
// (Does nothing when started with "dotnet run".)
builder.Host.UseWindowsService(options => options.ServiceName = "WaterBilling");

// ---- 1. Services ----
// Blazor Server: pages render on the server and update the browser over a live connection.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The clock. Code asks for TimeProvider instead of calling DateTime.Now, so tests can fix the time.
builder.Services.AddSingleton(TimeProvider.System);

// Database: one SQLite file. Path comes from appsettings.json ("Database:Path"),
// relative to the app folder unless it is a full path.
// %ProgramData% and similar are expanded, so the installed app keeps data outside Program Files.
var dbPath = Path.Combine(builder.Environment.ContentRootPath,
    Environment.ExpandEnvironmentVariables(builder.Configuration["Database:Path"] ?? "App_Data/waterbilling.db"));
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    ForeignKeys = true,          // the database enforces links between tables
}.ToString();
// Every committed save tells open screens to refresh (see Data/DataChanges.cs).
builder.Services.AddSingleton<DataChangeNotifier>();
builder.Services.AddSingleton<DataChangeInterceptor>();
builder.Services.AddDbContextFactory<AppDbContext>((sp, options) => options
    .UseSqlite(connectionString)
    .AddInterceptors(sp.GetRequiredService<DataChangeInterceptor>()));

// Keys that protect the login cookie and form tokens. Kept next to the database so logins survive
// a restart of the service; on Windows they are encrypted with the machine's DPAPI key.
var keysFolder = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(dbPath)!, "keys"));
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("WaterBilling").PersistKeysToFileSystem(keysFolder);
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);

// Login: a cookie that expires after 30 minutes without activity (NFR-SEC-04).
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.AccessDeniedPath = "/account/denied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Cookie.Name = "wb.auth";
        options.Cookie.HttpOnly = true;                       // scripts cannot read it
        options.Cookie.SameSite = SameSiteMode.Strict;        // not sent from other sites
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // On every request: is this login still valid? (user active, password/role unchanged)
        options.Events.OnValidatePrincipal = async context =>
        {
            var auth = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
            if (context.Principal is null || !await auth.IsStillValidAsync(context.Principal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync();
            }
        };
    });

// One policy per permission, e.g. [Authorize(Policy = "MastersEdit")].
builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Enum.GetValues<Permission>())
    {
        options.AddPolicy(permission.ToString(), policy =>
            policy.RequireAuthenticatedUser().RequireAssertion(ctx =>
                UserInfo.FromPrincipal(ctx.User)?.Has(permission) == true));
    }
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthStateProvider>();

// Our services. "Scoped" = one per browser connection (or per HTTP request).
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddSingleton<WaterBilling.Services.Ledger.BusinessCalendar>();
builder.Services.AddScoped<WaterBilling.Services.Ledger.LedgerPoster>();
builder.Services.AddScoped<WaterBilling.Services.Masters.SupplierService>();
builder.Services.AddScoped<WaterBilling.Services.Masters.CustomerService>();
builder.Services.AddScoped<WaterBilling.Services.Masters.VehicleService>();
builder.Services.AddScoped<WaterBilling.Services.Billing.InvoiceService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.ReceiptService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.PaymentService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.LedgerMasterService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.StatementService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.OutstandingService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.DaySalesService>();
builder.Services.AddScoped<WaterBilling.Services.Accounts.DashboardService>();
builder.Services.AddScoped<WaterBilling.Services.Reports.ReportService>();
builder.Services.AddScoped<WaterBilling.Services.Admin.ActivityLogService>();
builder.Services.AddScoped<WaterBilling.Services.Admin.BackupService>();
builder.Services.AddSingleton(new WaterBilling.Services.Admin.StoragePaths(
    dbPath,
    Path.Combine(builder.Environment.ContentRootPath,
        Environment.ExpandEnvironmentVariables(builder.Configuration["Backup:Folder"] ?? "App_Data/Backups")),
    int.TryParse(builder.Configuration["Backup:Keep"], out var keep) && keep > 0 ? keep : 60));

// Company details printed on PDFs. QuestPDF is free under its Community licence for
// businesses with under USD 1M annual revenue.
builder.Services.Configure<WaterBilling.Services.Common.CompanyInfo>(builder.Configuration.GetSection("Company"));
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var app = builder.Build();

// Create the database file, or bring it up to date, before the first request.
using (var scope = app.Services.CreateScope())
{
    using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    db.Database.Migrate();
    // WAL mode: readers do not wait for the writer, and a crash mid-save cannot corrupt the file.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    // First start only: create the "admin" user. The password must be changed at first login.
    var initialPassword = app.Configuration["Seed:AdminPassword"] ?? "Admin@123";
    await scope.ServiceProvider.GetRequiredService<AuthService>().EnsureAdminExistsAsync(initialPassword);

    // One automatic backup per day, taken when the app starts.
    if (!bool.TryParse(app.Configuration["Backup:Automatic"], out var automatic) || automatic)
        scope.ServiceProvider.GetRequiredService<WaterBilling.Services.Admin.BackupService>().RunDailyIfMissing();
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

app.UseAuthentication();   // reads the login cookie

// Admin-only areas (Users, Activity Log, Backup) do not exist for anyone else:
// a logged-in non-admin gets "Page not found", not "Not allowed", so there is no gateway to them.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/admin")
        && context.User.Identity?.IsAuthenticated == true
        && UserInfo.FromPrincipal(context.User)?.Role != WaterBilling.Data.Entities.UserRole.Admin)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseAuthorization();    // checks [Authorize] on pages and endpoints

// A user with a temporary password may only use the change-password page (FR-ADM-02).
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (context.User.HasClaim(UserInfo.MustChangeClaim, "true")
        && !path.StartsWithSegments("/account")
        && !path.StartsWithSegments("/_framework")
        && !path.StartsWithSegments("/not-found"))
    {
        context.Response.Redirect("/account/change-password");
        return;
    }
    await next();
});

app.UseAntiforgery();   // blocks forged form posts from other websites

// Logout is a POST (with antiforgery token), so another site cannot log the user out with a link.
app.MapPost("/account/logout", async (HttpContext context, AuthService auth, [FromForm] string? returnUrl) =>
{
    if (UserInfo.FromPrincipal(context.User) is { } user) await auth.LogLogoutAsync(user);
    await context.SignOutAsync();
    return Results.Redirect("/account/login");
});

// PDF downloads are normal HTTP requests, so they carry their own permission check.
app.MapGet("/pdf/invoice/{id:long}", async (long id, HttpContext context, CurrentUser currentUser,
    WaterBilling.Services.Billing.InvoiceService invoices,
    Microsoft.Extensions.Options.IOptions<WaterBilling.Services.Common.CompanyInfo> company, TimeProvider clock) =>
{
    currentUser.UseHttpUser(context.User);
    var invoice = await invoices.GetAsync(id);
    if (invoice is null) return Results.NotFound();
    var pdf = WaterBilling.Components.Pdf.InvoicePdf.Generate(invoice, company.Value, clock.GetUtcNow().UtcDateTime);
    return Results.File(pdf, "application/pdf");   // no file name: the browser shows it inline
}).RequireAuthorization(nameof(Permission.InvoiceView));

app.MapGet("/pdf/statement", async (HttpContext context, CurrentUser currentUser,
    WaterBilling.Services.Accounts.StatementService statements,
    Microsoft.Extensions.Options.IOptions<WaterBilling.Services.Common.CompanyInfo> company, TimeProvider clock,
    WaterBilling.Services.Accounts.StatementKind kind, long id, DateOnly from, DateOnly to, bool valueDate = false) =>
{
    currentUser.UseHttpUser(context.User);
    var statement = await statements.GetAsync(new(kind, id, from, to, valueDate));
    if (statement is null) return Results.NotFound();
    var table = WaterBilling.Services.Accounts.StatementService.ToTable(statement);
    return Results.File(WaterBilling.Components.Pdf.ReportPdf.Generate(table, company.Value, clock.GetUtcNow().UtcDateTime), "application/pdf");
}).RequireAuthorization(nameof(Permission.StatementsView));

app.MapGet("/pdf/report", async (HttpContext context, CurrentUser currentUser,
    WaterBilling.Services.Reports.ReportService reports,
    Microsoft.Extensions.Options.IOptions<WaterBilling.Services.Common.CompanyInfo> company, TimeProvider clock,
    WaterBilling.Services.Reports.ReportKind kind, DateOnly from, DateOnly to, long? supplierId, long? vehicleId) =>
{
    currentUser.UseHttpUser(context.User);
    if (to < from) return Results.BadRequest("The To date is before the From date.");
    var table = await reports.BuildAsync(kind, new(from, to, supplierId, vehicleId));
    return Results.File(WaterBilling.Components.Pdf.ReportPdf.Generate(table, company.Value, clock.GetUtcNow().UtcDateTime), "application/pdf");
}).RequireAuthorization(nameof(Permission.ReportsView));

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Makes the Program class visible to the test project, so tests can start the app in memory.
public partial class Program { }
