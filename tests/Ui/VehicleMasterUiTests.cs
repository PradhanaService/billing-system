using Microsoft.EntityFrameworkCore;
using Bunit;
using WaterBilling.Data.Entities;
using WaterBilling.Pages.Masters;

namespace WaterBilling.Tests.Ui;

// Module 4 of the final design: Vehicle Master list, Add vehicle view, and the delete pop-up.
public class VehicleMasterUiTests : UiTest
{
    [Fact]
    public async Task VM_01_List_Shows_On_Open_With_Search_Supplier_Filter_And_Add_Button()
    {
        await SupplierWithVehicleAsync();

        var page = RenderComponent<Vehicles>();

        page.WaitForAssertion(() => Assert.Contains("TN 67 C", page.Markup));
        Assert.NotNull(page.Find("#search"));
        Assert.Contains("All suppliers", page.Find("#supfilter").TextContent);
        Assert.Contains("Add vehicle", page.Markup);
    }

    [Fact]
    public async Task VM_02_Add_Vehicle_Opens_Its_Own_View_Without_Driver_Or_Water_Fields()
    {
        await SupplierWithVehicleAsync();
        var page = RenderComponent<Vehicles>();

        page.FindAll("button").First(b => b.TextContent.Contains("Add vehicle")).Click();

        page.WaitForAssertion(() => Assert.Contains("Back to list", page.Markup));
        Assert.DoesNotContain("TN 67 C", page.Markup);          // the list is replaced by the form
        foreach (var gone in new[] { "Driver", "Water Details", "Licence" })
            Assert.DoesNotContain(gone, page.Markup);
        foreach (var label in new[] { "Vehicle Code*", "Vehicle No.*", "Supplier*", "Capacity (Litres)*", "Rate (₹ per trip)*", "Trip Cost" })
            Assert.Contains(label, page.Markup);
    }

    [Fact]
    public async Task VM_03_Supplier_Filter_Narrows_The_List()
    {
        var (kaveri, _) = await SupplierWithVehicleAsync();
        await SupplierWithVehicleAsync("City Water Supply", "C78", "TN 66 K 7807", 150m, 12000);
        var page = RenderComponent<Vehicles>();
        page.WaitForAssertion(() => Assert.Contains("TN 66 K 7807", page.Markup));

        page.Find("#supfilter").Change(kaveri.ToString());

        page.WaitForAssertion(() => Assert.DoesNotContain("TN 66 K 7807", page.Markup));
        Assert.Contains("TN 67 C", page.Markup);
    }

    [Fact]
    public async Task VM_04_Admin_Delete_Pop_Up_Records_Admin_And_Moves_To_Recycle_Bin()
    {
        var (_, vehicle) = await SupplierWithVehicleAsync();
        var page = RenderComponent<Vehicles>();
        page.WaitForAssertion(() => Assert.Contains("TN 67 C", page.Markup));

        page.FindAll("button").First(b => b.TextContent == "Delete").Click();
        Assert.Contains("Delete vehicle?", page.Markup);
        Assert.Equal("Admin", page.Find("#deletedby").GetAttribute("value"));
        page.FindAll("button").Last(b => b.TextContent == "Delete").Click();

        page.WaitForAssertion(() => Assert.Contains("Recycle Bin for 30 days", page.Markup));
        using var db = Db.NewContext();
        Assert.True(db.Vehicles.IgnoreQueryFilters().Single(v => v.Id == vehicle).IsDeleted);
    }
}

// The Sales login must choose a name in "Deleted by" before Delete turns on.
public class VehicleMasterSalesUiTests : UiTest
{
    public VehicleMasterSalesUiTests() : base(UserRole.SalesPerson) { }

    [Fact]
    public async Task VM_05_Sales_Delete_Needs_A_Name_From_Sales_Persons()
    {
        var ravi = Db.AddSalesPerson("Ravi");
        var (_, vehicle) = await SupplierWithVehicleAsync();
        var page = RenderComponent<Vehicles>();
        page.WaitForAssertion(() => Assert.Contains("TN 67 C", page.Markup));

        page.FindAll("button").First(b => b.TextContent == "Delete").Click();
        var confirm = page.FindAll("button").Last(b => b.TextContent == "Delete");
        Assert.True(confirm.HasAttribute("disabled"));
        Assert.Contains("Ravi", page.Find("#deletedby").TextContent);

        page.Find("#deletedby").Change(ravi.ToString());
        page.FindAll("button").Last(b => b.TextContent == "Delete").Click();

        page.WaitForAssertion(() => Assert.Contains("deleted", page.Markup));
        using var db = Db.NewContext();
        Assert.Equal(ravi, db.Vehicles.IgnoreQueryFilters().Single(v => v.Id == vehicle).DeletedBySalesPersonId);
    }
}
