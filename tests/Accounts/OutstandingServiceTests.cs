using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Accounts;

// The detail behind the dashboard's Outstanding amount card.
public class OutstandingServiceTests : BillingFixture
{
    private long _advanceSupplier;

    // Sangeetha: two credit trips of 150, paid 100 -> owes 200 (INV-1 part paid, INV-2 unpaid).
    // Meena (customer): one credit trip of 150 -> owes 150.
    // Kaveri: opening balance 500 Cr (paid in advance) -> we owe 500.
    private async Task SetUpAsync()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });
        Db.Clock.Advance(TimeSpan.FromDays(1));
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });
        await Svc.Receipts.CreateAsync(new ReceiptForm { PartyType = PartyType.Supplier, PartyId = Supplier, Amount = 100 });
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, BillToType = PartyType.Customer, CustomerId = Customer });
        _advanceSupplier = (await Svc.Suppliers.CreateAsync(new SupplierForm
        {
            Name = "Kaveri Tankers", ShortName = "Kaveri", Phone = "9123456780", OpeningBalance = 500, OpeningBalanceSide = BalanceSide.Cr,
        })).Value;
    }

    [Fact]
    public async Task Lists_Who_Owes_And_Who_Has_Paid_In_Advance()
    {
        await SetUpAsync();

        var s = await Svc.Outstanding.GetAsync();

        Assert.Equal(["Kaveri Tankers", "Sangeetha Water Supply", "Meena Constructions"], s.Rows.Select(r => r.Name));   // biggest first
        Assert.Equal(-50000, s.Rows.Single(r => r.PartyId == _advanceSupplier && r.PartyType == PartyType.Supplier).BalancePaise);
        var sangeetha = s.Rows.Single(r => r.PartyType == PartyType.Supplier && r.PartyId == Supplier);
        Assert.Equal((20000L, 2, (DateOnly?)new DateOnly(2026, 11, 5)), (sangeetha.BalancePaise, sangeetha.OpenInvoices, sangeetha.OldestUnpaid));
        Assert.Equal((35000L, 50000L, -15000L), (s.OwedToUsPaise, s.WeOwePaise, s.NetPaise));
    }

    [Fact]
    public async Task Net_Total_Equals_The_Dashboard_Card()
    {
        await SetUpAsync();

        var s = await Svc.Outstanding.GetAsync();
        var dashboard = await Svc.Dashboard.GetAsync();

        Assert.Equal(dashboard.OutstandingPaise, s.NetPaise);
    }

    [Fact]
    public async Task Open_Invoices_Show_Billed_Paid_And_Due()
    {
        await SetUpAsync();

        var invoices = await Svc.Outstanding.OpenInvoicesAsync(PartyType.Supplier, Supplier);

        Assert.Equal(["INV-1", "INV-2"], invoices.Select(i => i.InvoiceNo));
        Assert.Equal((15000L, 10000L, 5000L), (invoices[0].AmountPaise, invoices[0].PaidPaise, invoices[0].DuePaise));
        Assert.Equal((15000L, 0L, 15000L), (invoices[1].AmountPaise, invoices[1].PaidPaise, invoices[1].DuePaise));
        Assert.Equal("TN 38 M 3", invoices[0].VehicleNo);
    }

    [Fact]
    public async Task Paid_Up_Parties_And_Cash_Sales_Do_Not_Appear()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash });

        var s = await Svc.Outstanding.GetAsync();

        Assert.Empty(s.Rows);
        Assert.Equal(0, s.NetPaise);
    }

    [Fact]
    public async Task Filter_And_Search()
    {
        await SetUpAsync();

        Assert.All((await Svc.Outstanding.GetAsync(OutstandingFilter.Suppliers)).Rows, r => Assert.Equal(PartyType.Supplier, r.PartyType));
        Assert.Equal("Meena Constructions", Assert.Single((await Svc.Outstanding.GetAsync(OutstandingFilter.Customers)).Rows).Name);
        Assert.Equal("Kaveri Tankers", Assert.Single((await Svc.Outstanding.GetAsync(search: "kaveri")).Rows).Name);
    }

    [Fact]
    public async Task Every_Role_Can_See_It_But_Not_Logged_In_Is_Refused()
    {
        await SetUpAsync();
        foreach (var role in Enum.GetValues<UserRole>())
        {
            Me.User = Db.AddUser(role);
            Assert.Equal(3, (await Svc.Outstanding.GetAsync()).Rows.Count);
        }

        Me.User = null;
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.Outstanding.GetAsync());
    }
}
