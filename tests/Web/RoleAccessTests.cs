using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WaterBilling.Components.Layout;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Web;

// Test round 1 (security): for every role and every menu page, the server allows exactly
// what the role table allows, over real HTTP. Hidden menu items are not the protection.
public class RoleAccessTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public RoleAccessTests(TestAppFactory factory) => _factory = factory;

    private async Task<HttpClient> LoginAsAsync(UserRole role)
    {
        var userName = "t-" + role.ToString().ToLowerInvariant();
        using (var scope = _factory.Services.CreateScope())
        {
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            if (!await db.Users.AnyAsync(u => u.UserName == userName))
            {
                var user = new User { UserName = userName, NormalizedUserName = userName.ToUpperInvariant(), FullName = userName, Role = role, MustChangePassword = false };
                user.PasswordHash = AuthService.HashPassword(user, "Passw0rd!");
                db.Users.Add(user);
                await db.SaveChangesAsync();
            }
        }
        var client = _factory.NewClient();
        var login = await TestAppFactory.PostFormAsync(client, "/account/login", "login", ("Input.UserName", userName), ("Input.Password", "Passw0rd!"));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    public static IEnumerable<object[]> RolesAndPages() =>
        from role in Enum.GetValues<UserRole>()
        from item in MenuItems.All
        select new object[] { role, item.Href };

    [Theory]
    [MemberData(nameof(RolesAndPages))]
    public async Task NFR_SEC_05_Server_Allows_Only_What_The_Role_Table_Allows(UserRole role, string href)
    {
        var client = await LoginAsAsync(role);
        var permission = MenuItems.All.Single(i => i.Href == href).Permission;

        var response = await client.GetAsync(href);

        if (RolePermissions.Has(role, permission))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            // Admin-only pages simply do not exist for other roles: "Page not found".
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("Page not found", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(UserRole.DispatchOperator)]
    [InlineData(UserRole.BillingClerk)]
    [InlineData(UserRole.Accountant)]
    public async Task Non_Admin_Sees_No_Gateway_To_Backup_Logs_Or_Users(UserRole role)
    {
        var client = await LoginAsAsync(role);

        foreach (var page in new[] { "/", "/vehicles", "/invoices/new", "/receipts", "/payments", "/reports" })
        {
            var html = await client.GetStringAsync(page);
            Assert.DoesNotContain("/admin/", html);
            Assert.DoesNotContain("Activity Log", html);
            Assert.DoesNotContain("Backup", html);
        }
        foreach (var hidden in new[] { "/admin/backup", "/admin/activity-log", "/admin/users", "/admin/anything" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(hidden)).StatusCode);
    }

    [Fact]
    public async Task Admin_Sees_Backup_Logs_And_Users()
    {
        var client = await LoginAsAsync(UserRole.Admin);
        var html = await client.GetStringAsync("/");
        Assert.Contains("/admin/backup", html);
        Assert.Contains("/admin/activity-log", html);
        Assert.Contains("/admin/users", html);
    }

    [Fact]
    public async Task Html_Typed_Into_A_Name_Is_Shown_As_Text_Not_Run()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            db.Suppliers.Add(new Supplier { Name = "<script>alert('x')</script>", ShortName = "X", Phone = "9876543210" });
            await db.SaveChangesAsync();
        }
        var client = await LoginAsAsync(UserRole.Admin);

        var html = await client.GetStringAsync("/suppliers");

        Assert.DoesNotContain("<script>alert('x')</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Bad_Report_Parameters_Are_Refused_Not_Crashing()
    {
        var client = await LoginAsAsync(UserRole.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/pdf/report?kind=Nope&from=2026-01-01&to=2026-12-31")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/pdf/report?kind=CashBook&from=2026-12-31&to=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/pdf/report?kind=CashBook&from=bad&to=2026-01-01")).StatusCode);
    }
}
