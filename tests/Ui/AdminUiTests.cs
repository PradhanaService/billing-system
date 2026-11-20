using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WaterBilling.Data.Entities;
using WaterBilling.Pages.Admin;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Ui;

// Module 10 screens: Password Management, Activity Log, Backup & Restore (Admin login).
public class AdminUiTests : UiTest
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "wb-ui-" + Guid.NewGuid().ToString("N"));

    public AdminUiTests() : base(UserRole.Admin)
    {
        Services.AddTransient<UserService>();
        Services.AddSingleton(new StoragePaths(Path.Combine(_folder, "live.db"), Path.Combine(_folder, "backups"), 30));
        Services.AddTransient<BackupService>();
    }

    [Fact]
    public void PWD_10_Two_Login_Cards_And_A_Password_Change()
    {
        Db.AddUser(UserRole.SalesPerson, "sales");
        var page = RenderComponent<Users>();
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".login-card").Count));
        Assert.Contains("Change Admin password", page.Markup);
        Assert.Contains("Change Sales Person password", page.Markup);
        Assert.DoesNotContain("Add user", page.Markup);

        page.Find("#new-SalesPerson").Input("Shop#2026");
        page.Find("#confirm-SalesPerson").Input("Shop#2026");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Change Sales Person password").Click();

        page.WaitForAssertion(() => Assert.Contains("Sales Person password changed. The new password works from the next sign-in.", page.Markup));
        Assert.Equal("", page.Find("#new-SalesPerson").GetAttribute("value"));
    }

    [Fact]
    public void PWD_11_Show_Password_Button_Reveals_The_Text()
    {
        var page = RenderComponent<Users>();
        page.WaitForAssertion(() => page.Find("#new-Admin"));
        Assert.Equal("password", page.Find("#new-Admin").GetAttribute("type"));
        page.Find("#card-Admin button[aria-label='Show password']").Click();
        Assert.Equal("text", page.Find("#new-Admin").GetAttribute("type"));
    }

    [Fact]
    public async Task AL_10_Activity_Log_Has_User_Dropdown_And_Done_By_Column()
    {
        var ravi = Db.AddSalesPerson("Ravi");
        Db.AddSalesPerson("Kumar");
        var (_, vehicle) = await SupplierWithVehicleAsync(rate: 150);
        Me.User = Db.AddUser(UserRole.SalesPerson);
        await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = vehicle, Rate = 150, SalesPersonId = ravi });
        Me.User = Db.AddUser(UserRole.Admin, "admin");

        var page = RenderComponent<WaterBilling.Pages.Admin.ActivityLog>();
        page.WaitForAssertion(() => Assert.Contains("INVOICE_CREATED", page.Markup));
        Assert.Equal(["All users", "Admin", "Kumar", "Ravi"], page.FindAll("#user option").Select(o => o.TextContent));
        Assert.Contains("Done by", page.Find("thead").TextContent);
        Assert.DoesNotContain("<th>Role</th>", page.Markup);
        var row = page.FindAll("tbody tr").First(r => r.TextContent.Contains("INVOICE_CREATED"));
        Assert.Contains("Ravi", row.Children[1].TextContent);
        Assert.Contains("Sales login", row.Children[1].TextContent);

        page.Find("#user").Change("Kumar");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Search").Click();
        page.WaitForAssertion(() => Assert.DoesNotContain("INVOICE_CREATED", page.Find("tbody").TextContent));
    }

    [Fact]
    public void BKP_10_Page_Has_Backup_Restore_Folder_And_Delete_All()
    {
        var page = RenderComponent<Backup>();
        page.WaitForAssertion(() => Assert.Equal(Path.Combine(_folder, "backups"), page.Find("#bkdir").GetAttribute("value")));
        Assert.Contains("Backup now", page.Markup);
        Assert.Contains("Restore database", page.Markup);
        Assert.Contains("Delete all data…", page.Markup);
        Assert.Contains("No backups yet.", page.Markup);
    }

    [Fact]
    public void BKP_11_Choose_Folder_Checks_The_Path()
    {
        var page = RenderComponent<Backup>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Choose folder")).Click();
        page.Find("#bksel").Change("Backups");
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Use this folder").Click();
        page.WaitForAssertion(() => Assert.Contains("Type the full folder path", page.Markup));

        var chosen = Path.Combine(_folder, "pen");
        page.Find("#bksel").Change(chosen);
        page.FindAll("button").Single(b => b.TextContent.Trim() == "Use this folder").Click();
        page.WaitForAssertion(() => Assert.Equal(chosen, page.Find("#bkdir").GetAttribute("value")));
        Assert.Contains($"Backups now go to {chosen}.", page.Markup);
    }

    [Fact]
    public void BKP_12_Restore_Pop_Up_Has_Browse_Only_And_Needs_RESTORE()
    {
        var page = RenderComponent<Backup>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Restore database")).Click();
        Assert.Single(page.FindAll("input[type=file]"));
        Assert.Empty(page.FindAll(".modal input[type=radio]"));
        var restore = page.FindAll(".modal button").Single(b => b.TextContent.Trim() == "Restore");
        Assert.True(restore.HasAttribute("disabled"));
        page.Find("#rbconf").Input("RESTORE");
        Assert.True(page.FindAll(".modal button").Single(b => b.TextContent.Trim() == "Restore").HasAttribute("disabled"));   // still no file
        Assert.Contains("safety copy", page.Find(".modal").TextContent);
    }

    [Fact]
    public void BKP_13_Delete_All_Needs_The_Words_And_The_Admin_Password()
    {
        var page = RenderComponent<Backup>();
        page.FindAll("button").Single(b => b.TextContent.Contains("Delete all data…")).Click();
        Assert.True(Button().HasAttribute("disabled"));
        Assert.Contains("Kept: the Admin and Sales logins", page.Markup);

        page.Find("#rsp").Input("wrong");
        page.Find("#rsc").Input("DELETE ALL DATA");
        Assert.False(Button().HasAttribute("disabled"));
        Button().Click();
        page.WaitForAssertion(() => Assert.Contains("The Admin password is wrong.", page.Markup));

        AngleSharp.Dom.IElement Button() => page.FindAll(".modal button").Single(b => b.TextContent.Trim() == "Delete all data");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}
