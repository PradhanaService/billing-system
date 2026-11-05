using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using WaterBilling.Components.Layout;

namespace WaterBilling.Tests;

// Session 2: the menu is correct and every ready link opens a real page.
public class NavMenuTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public NavMenuTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Every_Ready_Menu_Link_Opens_A_Page()
    {
        var client = _factory.CreateClient();

        foreach (var item in MenuItems.All.Where(i => i.IsReady))
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
        // Every link must stay inside the app: starts with "/" but not "//" (which would mean another website).
        Assert.All(hrefs, h => Assert.True(h.StartsWith('/') && !h.StartsWith("//"), $"Bad link: {h}"));
    }

    [Fact]
    public async Task Layout_Shows_Menu_With_Ready_And_Unready_Items()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("aria-label=\"Main menu\"", html);
        // Ready item is a link; unready item is plain greyed text, not a link.
        Assert.Contains("href=\"/\"", html);
        Assert.DoesNotContain("href=\"/vehicles\"", html);
        Assert.Contains("Vehicle Master", html);
    }
}
