using System.Net;
using WaterBilling.Components.Layout;

namespace WaterBilling.Tests.Design;

// Module 1 of the final design (Chathams Blue & Pampas): colours, no water-drop logo, the new menu.
public class ThemeAndMenuTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;
    public ThemeAndMenuTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UI_01_Stylesheet_Uses_The_Final_Palette()
    {
        var css = await _factory.CreateClient().GetStringAsync("/app.css");

        Assert.Contains("--accent: #10497A", css);   // Chathams Blue
        Assert.Contains("--tint: #F4F3EE", css);     // Pampas
        Assert.DoesNotContain("#0F4C75", css);        // the old Royal Blue is gone
    }

    [Fact]
    public async Task UI_02_No_Water_Drop_Logo_On_Any_Layout()
    {
        const string drop = "M12 3s7 7.5 7 12a7 7 0 0 1-14 0c0-4.5 7-12 7-12z";
        var admin = await _factory.AdminClientAsync();

        Assert.DoesNotContain(drop, await admin.GetStringAsync("/"));
        Assert.DoesNotContain(drop, await _factory.CreateClient().GetStringAsync("/account/login"));
    }

    [Fact]
    public void UI_03_Menu_Matches_The_Design()
    {
        var titles = MenuItems.All.Select(i => i.Title).ToList();

        Assert.Equal(["Dashboard", "Supplier Master", "Vehicle Master", "Ledger Master", "Sales Invoice", "Invoice Display",
                      "Receipt", "Payment", "Reports", "Password Management", "Sales Persons", "Activity Log",
                      "Backup & Restore", "Recycle Bin"], titles);
    }

    [Theory]
    [InlineData("/statements")]
    [InlineData("/customers")]
    public async Task UI_04_Removed_Screens_Answer_Page_Not_Found(string url)
    {
        var admin = await _factory.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task UI_05_Renamed_Pages_Show_Their_New_Titles()
    {
        var admin = await _factory.AdminClientAsync();

        Assert.Contains("Password Management", await admin.GetStringAsync("/admin/users"));
        Assert.Contains("Backup &amp; Restore", await admin.GetStringAsync("/admin/backup"));
        Assert.Contains("Recycle Bin", await admin.GetStringAsync("/recovery"));
    }
}
