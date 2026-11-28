namespace WaterBilling.Tests.Design;

// Module 3 of the final design: tapping a supplier name opens that supplier's vehicles, with a way back.
public class SupplierVehiclesTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;
    public SupplierVehiclesTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SUP_01_Supplier_Name_Links_To_Its_Vehicles()
    {
        var id = await WebSeed.SupplierAsync(_factory, "Kaveri Tankers Link", "9123456781");
        var html = await (await _factory.AdminClientAsync()).GetStringAsync("/suppliers");

        Assert.Contains($"href=\"/suppliers/{id}/vehicles\"", html);
    }

    [Fact]
    public async Task SUP_02_Vehicles_Page_Lists_Only_That_Suppliers_Vehicles_And_Has_Back()
    {
        var kaveri = await WebSeed.SupplierAsync(_factory, "Kaveri Tankers V", "9123456782", "Raman", "Gandhi Road, Tiruppur");
        var other = await WebSeed.SupplierAsync(_factory, "City Water V", "9443056789");
        await WebSeed.VehicleAsync(_factory, kaveri, "K2", "TN 67 C");
        await WebSeed.VehicleAsync(_factory, kaveri, "K3", "TN 45 B 2210", 12000, 10000);
        await WebSeed.VehicleAsync(_factory, other, "C78", "TN 66 K 7807");

        var html = await (await _factory.AdminClientAsync()).GetStringAsync($"/suppliers/{kaveri}/vehicles");

        Assert.Contains("Vehicles of Kaveri Tankers V", html);
        Assert.Contains("TN 67 C", html);
        Assert.Contains("TN 45 B 2210", html);
        Assert.DoesNotContain("TN 66 K 7807", html);
        Assert.Contains("Gandhi Road, Tiruppur", html);
        Assert.Contains("href=\"/suppliers\"", html);
        Assert.Contains("Back to Supplier Master", html);
    }

    [Fact]
    public async Task SUP_03_Unknown_Supplier_Shows_A_Clear_Message()
    {
        var html = await (await _factory.AdminClientAsync()).GetStringAsync("/suppliers/999999/vehicles");

        Assert.Contains("This supplier was not found", html);
    }
}
