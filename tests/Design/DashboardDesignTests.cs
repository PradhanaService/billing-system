using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;

namespace WaterBilling.Tests.Design;

// Module 2 of the final design: the Dashboard drops three cards and shows recent invoices full width
// with a status for each. PayStates is the one rule for that status.
public class DashboardDesignTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;
    public DashboardDesignTests(TestAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DASH_01_No_Water_Trips_Or_Cash_Cards()
    {
        var html = await (await _factory.AdminClientAsync()).GetStringAsync("/");

        Assert.DoesNotContain("Water trips", html);
        Assert.DoesNotContain("Opening cash", html);
        Assert.DoesNotContain("Closing cash", html);
        Assert.Contains("Outstanding amount", html);
        Assert.Contains("Today&#x27;s receipt", html);
    }

    [Fact]
    public async Task DASH_02_Recent_Invoices_Is_A_Full_Width_Table_With_Status()
    {
        var html = await (await _factory.AdminClientAsync()).GetStringAsync("/");

        Assert.Contains("dash-recent", html);
        Assert.Contains("<th>Status</th>", html);
        Assert.Contains("View all invoices", html);
    }

    [Theory]
    [InlineData(InvoiceStatus.Posted, InvoicePaymentMode.Cash, 15000, 0, PayState.Paid)]
    [InlineData(InvoiceStatus.Posted, InvoicePaymentMode.Credit, 15000, 15000, PayState.Paid)]
    [InlineData(InvoiceStatus.Posted, InvoicePaymentMode.Credit, 15000, 5000, PayState.PartPaid)]
    [InlineData(InvoiceStatus.Posted, InvoicePaymentMode.Credit, 15000, 0, PayState.NotPaid)]
    [InlineData(InvoiceStatus.Cancelled, InvoicePaymentMode.Cash, 15000, 0, PayState.Cancelled)]
    public void DASH_03_Status_Rule(InvoiceStatus status, InvoicePaymentMode mode, long amount, long allocated, PayState expected)
    {
        Assert.Equal(expected, PayStates.For(status, mode, amount, allocated));
    }
}
