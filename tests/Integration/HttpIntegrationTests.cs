using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Integration;

// Integration tests over real HTTP: the whole app (pipeline, login cookies, role rules, PDF routes,
// database file) started in memory by TestAppFactory. Each test class gets its own database file.
public class HttpIntegrationTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _app;

    public HttpIntegrationTests(TestAppFactory app) => _app = app;

    // Runs a service inside the app, as the given user (as a PDF route does).
    private async Task<T> AsUserAsync<T>(string userName, Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync())
        {
            var user = await db.Users.SingleAsync(u => u.UserName == userName);
            scope.ServiceProvider.GetRequiredService<CurrentUser>().UseHttpUser(UserInfo.ToPrincipal(user));
        }
        return await work(scope.ServiceProvider);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string user, string password) =>
        await TestAppFactory.PostFormAsync(client, "/account/login", "login", ("Input.UserName", user), ("Input.Password", password));

    // The Admin sets the Sales password on Password Management, adds a sales person and some trade.
    private async Task<(long Supplier, long Vehicle, long Invoice)> SetUpShopAsync()
    {
        await _app.AdminClientAsync();
        return await AsUserAsync("admin", async sp =>
        {
            var users = sp.GetRequiredService<UserService>();
            if ((await users.LoginsAsync())[1].Id is null)
                Assert.True((await users.SetPasswordAsync(UserRole.SalesPerson, "Sales#2026", "Sales#2026")).Ok);
            var people = sp.GetRequiredService<SalesPersonService>();
            if ((await people.ListActiveAsync()).Count == 0) await people.AddAsync(new SalesPersonForm { Name = "Ravi" });
            var suppliers = sp.GetRequiredService<SupplierService>();
            var existing = (await suppliers.SearchAsync("Kaveri", StatusFilter.All)).FirstOrDefault();
            var supplier = existing?.Id ?? (await suppliers.CreateAsync(new SupplierForm { Name = "Kaveri Tankers", ShortName = "Kaveri", Phone = "9123456780" })).Value;
            var vehicles = sp.GetRequiredService<VehicleService>();
            var vehicle = (await vehicles.SearchAsync("A2", StatusFilter.All, supplier)).FirstOrDefault()?.Id
                ?? (await vehicles.CreateAsync(new VehicleForm { Code = "A2", VehicleNo = "TN 67 C", SupplierId = supplier, TankCapacityLitres = 24000, Rate = 129.68m })).Value;
            var invoice = await sp.GetRequiredService<InvoiceService>().CreateAsync(new InvoiceForm { VehicleId = vehicle, Rate = 129.68m });
            Assert.True(invoice.Ok, invoice.Error);
            return (supplier, vehicle, invoice.Value);
        });
    }

    [Fact]
    public async Task INT_10_Sales_Login_Set_By_Admin_Works_And_Reaches_Every_Business_Page()
    {
        var (supplier, _, invoice) = await SetUpShopAsync();
        var sales = _app.NewClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(sales, "sales", "Sales#2026")).StatusCode);

        foreach (var url in new[] { "/", "/suppliers", $"/suppliers/{supplier}/vehicles", "/vehicles", "/ledgers", "/invoices/new",
                                    "/invoices", $"/invoices/{invoice}", $"/invoices/{invoice}/edit", "/receipts", "/payments", "/reports", "/recovery" })
            Assert.True((await sales.GetAsync(url)).StatusCode == HttpStatusCode.OK, url);
        foreach (var url in new[] { "/admin/users", "/admin/activity-log", "/admin/backup", "/admin/sales-persons" })
            Assert.Equal(HttpStatusCode.NotFound, (await sales.GetAsync(url)).StatusCode);
        // Removed screens are gone for everyone.
        Assert.Equal(HttpStatusCode.NotFound, (await sales.GetAsync("/statements")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await sales.GetAsync("/customers")).StatusCode);
    }

    [Fact]
    public async Task INT_11_Trip_Report_Pdf_For_Each_Shape_And_Role()
    {
        var (supplier, vehicle, _) = await SetUpShopAsync();
        var admin = await _app.AdminClientAsync();
        var sales = _app.NewClient();
        await LoginAsync(sales, "sales", "Sales#2026");
        var range = $"from=2026-01-01&to=2026-12-31";

        foreach (var client in new[] { admin, sales })
            foreach (var query in new[] { range, $"{range}&supplierId={supplier}", $"{range}&vehicleId={vehicle}" })
            {
                var pdf = await client.GetAsync($"/pdf/trip-report?{query}");
                Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
                Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
                var bytes = await pdf.Content.ReadAsByteArrayAsync();
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/pdf/trip-report?from=2026-12-31&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync($"/pdf/trip-report?{range}")).StatusCode);   // not signed in
    }

    [Fact]
    public async Task INT_12_Changing_The_Sales_Password_Signs_Out_The_Open_Sales_Session()
    {
        await SetUpShopAsync();
        var sales = _app.NewClient();
        await LoginAsync(sales, "sales", "Sales#2026");
        Assert.Equal(HttpStatusCode.OK, (await sales.GetAsync("/")).StatusCode);

        await AsUserAsync("admin", async sp => (await sp.GetRequiredService<UserService>().SetPasswordAsync(UserRole.SalesPerson, "Next#2026", "Next#2026")).Ok);

        Assert.Equal(HttpStatusCode.Redirect, (await sales.GetAsync("/")).StatusCode);               // old cookie no longer works
        var again = _app.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(again, "sales", "Sales#2026")).StatusCode);   // old password refused
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(again, "sales", "Next#2026")).StatusCode);
        await AsUserAsync("admin", async sp => (await sp.GetRequiredService<UserService>().SetPasswordAsync(UserRole.SalesPerson, "Sales#2026", "Sales#2026")).Ok);
    }

    [Fact]
    public async Task INT_13_Backup_Then_Restore_Puts_Data_Back_And_Signs_Everyone_Out()
    {
        await SetUpShopAsync();
        var admin = await _app.AdminClientAsync();
        var backup = await AsUserAsync("admin", sp => sp.GetRequiredService<BackupService>().RunAsync());
        Assert.True(backup.Ok, backup.Error);
        int Invoices() { using var s = _app.Services.CreateScope(); using var db = s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext(); return db.Invoices.Count(); }
        var before = Invoices();
        await SetUpShopAsync();                     // one more invoice after the backup
        Assert.Equal(before + 1, Invoices());
        admin = await _app.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/backup")).StatusCode);
        await Task.Delay(1100);                      // the safety copy gets its own second-stamped name

        var restore = await AsUserAsync("admin", sp => sp.GetRequiredService<BackupService>().RestoreFromListAsync(backup.Value!.Name, "RESTORE"));

        Assert.True(restore.Ok, restore.Error);
        Assert.Equal(before, Invoices());
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/admin/backup")).StatusCode);   // signed out
        Assert.Equal(HttpStatusCode.OK, (await _app.NewClient().GetAsync("/account/login?restored=1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await _app.AdminClientAsync()).GetAsync("/admin/backup")).StatusCode);   // signs in again
    }
}
