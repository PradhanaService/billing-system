using System.Net;
using WaterBilling.Components.Pdf;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;

namespace WaterBilling.Tests.Billing;

public class InvoicePdfTests : BillingFixture
{
    [Fact]
    public async Task Invoice_Pdf_Is_A_Valid_Pdf()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var id = (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 })).Value;

        var pdf = InvoicePdf.Generate((await Svc.Invoices.GetAsync(id))!, new CompanyInfo(), DateTime.UtcNow);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}

public class InvoicePdfEndpointTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public InvoicePdfEndpointTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Pdf_Needs_Login()
    {
        var response = await _factory.NewClient().GetAsync("/pdf/invoice/1");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_Invoice_Returns_404()
    {
        var client = await _factory.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/pdf/invoice/999")).StatusCode);
    }
}
