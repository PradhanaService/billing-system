using Bunit;
using WaterBilling.Data.Entities;
using ReportsPage = WaterBilling.Pages.Reports.Reports;
using WaterBilling.Services.Billing;

namespace WaterBilling.Tests.Ui;

// Module 9 screen: filters, the three shapes, Export PDF link.
public class ReportsUiTests : UiTest
{
    private long _kaveri, _a2, _city, _c78;

    public ReportsUiTests() : base(UserRole.SalesPerson) { }

    private async Task SeedAsync()
    {
        (_kaveri, _a2) = await SupplierWithVehicleAsync();
        (_city, _c78) = await SupplierWithVehicleAsync("City Water Supply", "C78", "TN 66 K 7807", 150m);
        var ravi = Db.AddSalesPerson("Ravi");
        await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = _a2, Rate = 129.68m, SalesPersonId = ravi, InDate = new DateOnly(2026, 11, 5), InTime = "11:30 AM" });
        await S.Invoices.CreateAsync(new InvoiceForm { VehicleId = _c78, Rate = 150m, PaymentMode = InvoicePaymentMode.Cash, SalesPersonId = ravi });
    }

    [Fact]
    public async Task RPT_10_Opens_With_All_Suppliers_And_Grand_Total()
    {
        await SeedAsync();
        var page = RenderComponent<ReportsPage>();
        page.WaitForAssertion(() => Assert.Contains("All suppliers · all vehicles", page.Markup));
        Assert.Equal(2, page.FindAll(".rpt-group").Count);
        Assert.Contains("Grand total trips", page.Find("#totals").TextContent);
        Assert.Contains("₹279.68", page.Find("#totals").TextContent);
        Assert.DoesNotContain("Report by", page.Markup);
        Assert.Equal("/pdf/trip-report?from=2026-11-01&to=2026-11-06", page.Find("#export").GetAttribute("href"));
    }

    [Fact]
    public async Task RPT_11_Choosing_A_Supplier_Narrows_Vehicles_And_Groups_By_Vehicle()
    {
        await SeedAsync();
        var page = RenderComponent<ReportsPage>();
        page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("#vehicle option").Count));

        page.Find("#supplier").Change(_kaveri.ToString());

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("#vehicle option").Count));
        Assert.Contains("A2 · TN 67 C", page.Find(".rpt-group").TextContent);
        Assert.Contains("Vehicle subtotal", page.Markup);
        Assert.Contains("11:30 AM", page.Markup);
        Assert.Contains("Not paid", page.Markup);
        Assert.Contains($"supplierId={_kaveri}", page.Find("#export").GetAttribute("href"));
    }

    [Fact]
    public async Task RPT_12_Choosing_A_Vehicle_Lists_Its_Trips()
    {
        await SeedAsync();
        var page = RenderComponent<ReportsPage>();
        page.WaitForAssertion(() => page.Find("#vehicle"));
        page.Find("#vehicle").Change(_c78.ToString());

        page.WaitForAssertion(() => Assert.Contains("C78 · TN 66 K 7807", page.Find(".rpt-title").TextContent));
        Assert.Contains("<th>Vehicle No.</th>", page.Markup);
        Assert.Contains("Paid", page.Find("tbody").TextContent);
        Assert.Contains("Total trips", page.Find("#totals").TextContent);
    }

    [Fact]
    public async Task RPT_13_To_Before_From_Is_Explained()
    {
        await SeedAsync();
        var page = RenderComponent<ReportsPage>();
        page.WaitForAssertion(() => page.Find("#to"));
        page.Find("#to").Change("2026-10-01");
        page.WaitForAssertion(() => Assert.Contains("The To date is before the From date.", page.Markup));
        Assert.Empty(page.FindAll("#export"));
    }
}
