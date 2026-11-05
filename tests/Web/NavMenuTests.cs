using System.Net;
using WaterBilling.Components.Layout;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Web;

// The menu is correct and every ready link opens a real page.
public class NavMenuTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public NavMenuTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Every_Ready_Menu_Link_Opens_A_Page_For_Admin()
    {
        var client = await _factory.AdminClientAsync();

        foreach (var item in MenuItems.All)
        {
            var response = await client.GetAsync(item.Href);
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Menu item '{item.Title}' ({item.Href}) returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public void Menu_Links_Are_Unique_And_Local()
    {
        var hrefs = MenuItems.All.Select(i => i.Href).ToList();

        Assert.Equal(hrefs.Count, hrefs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(hrefs, h => Assert.True(h.StartsWith('/') && !h.StartsWith("//"), $"Bad link: {h}"));
    }

    [Fact]
    public void Menu_Shows_Only_Items_The_Role_May_Use()
    {
        var operatorUser = new UserInfo(1, "op", UserRole.DispatchOperator);
        var titles = MenuItems.For(operatorUser).Select(i => i.Title).ToList();

        Assert.Contains("Sales Invoice", titles);
        Assert.Contains("Receipt", titles);
        Assert.Contains("Payment", titles);
        Assert.Contains("Ledger Master", titles);
        Assert.DoesNotContain("Users & Roles", titles);
        Assert.DoesNotContain("Activity Log", titles);
        Assert.DoesNotContain("Backup", titles);
        Assert.Empty(MenuItems.For(null));
    }

    [Fact]
    public async Task Layout_Shows_Menu_And_Sign_Out()
    {
        var client = await _factory.AdminClientAsync();

        var html = await client.GetStringAsync("/");

        Assert.Contains("aria-label=\"Main menu\"", html);
        Assert.Contains("Sign out", html);
        Assert.Contains("href=\"/admin/users\"", html);
    }
}
