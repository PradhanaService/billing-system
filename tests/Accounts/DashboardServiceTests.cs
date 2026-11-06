using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

public class DashboardServiceTests : BillingFixture
{
    public DashboardServiceTests() => Me.User = Db.AddUser(UserRole.Admin);

    [Fact]
    public async Task FR_DASH_01_02_Cards_Match_The_Transactions()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Trips = 2 });
        var cancelled = (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 })).Value;
        await Svc.Invoices.CancelAsync(cancelled, "x", 1);
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 100 });
        await Svc.Payments.CreateAsync(new PaymentForm { AccountId = 10, Amount = 40 });

        var d = await Svc.Dashboard.GetAsync();

        Assert.Equal((45000L, 45000L, 10000L, 4000L), (d.MonthSalesPaise, d.TodaySalesPaise, d.TodayReceiptsPaise, d.TodayPaymentsPaise));
        Assert.Equal((15000L, 30000L, 3), (d.MonthCashSalesPaise, d.MonthCreditSalesPaise, d.MonthTrips));
        Assert.Equal(20000, d.OutstandingPaise);                  // 300 credit - 100 received
        Assert.Equal((0L, 21000L), (d.OpeningCashPaise, d.ClosingCashPaise));
        Assert.Equal(3, d.RecentInvoices.Count);
    }

    [Fact]
    public async Task FR_DASH_04_Six_Months_Ending_With_The_Selected_Month()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 });

        var d = await Svc.Dashboard.GetAsync(2026, 12);

        Assert.Equal(["2026-07", "2026-08", "2026-09", "2026-10", "2026-11", "2026-12"], d.LastSixMonths.Select(m => m.Key));
        Assert.Equal(15000, d.LastSixMonths.Single(m => m.Key == "2026-11").SalesPaise);
    }

    [Fact]
    public async Task FR_DASH_08_Month_Selector_Changes_Month_Figures_Not_Today()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 });

        var october = await Svc.Dashboard.GetAsync(2026, 10);

        Assert.Equal(0, october.MonthSalesPaise);
        Assert.Equal(15000, october.TodaySalesPaise);
    }

    [Fact]
    public async Task Every_Role_Sees_The_Dashboard()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            Me.User = Db.AddUser(role);
            Assert.NotNull(await Svc.Dashboard.GetAsync());
        }
    }
}
