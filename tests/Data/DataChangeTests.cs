using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Data;

// Open screens (Dashboard, Outstanding) reload when they hear DataChangeNotifier.Changed.
// These tests prove the notice comes only after the data is really saved.
public class DataChangeTests : BillingFixture
{
    [Fact]
    public async Task Credit_Invoice_Notifies_After_Commit_With_New_Outstanding_Visible()
    {
        long? outstandingSeen = null;
        Db.Changes.Changed += () => outstandingSeen ??= Svc.Outstanding.GetAsync().Result.NetPaise;

        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 });

        // At the moment of the notice, another reader already sees the new ₹150 outstanding.
        Assert.Equal(15000, outstandingSeen);
    }

    [Fact]
    public async Task Failed_Save_Sends_No_Notice()
    {
        using (var ctx = Db.NewContext())
        {
            ctx.BusinessDays.Add(new BusinessDay { Date = new DateOnly(2026, 11, 5), Status = BusinessDayStatus.Closed, Version = 1 });
            ctx.SaveChanges();
        }
        var notices = 0;
        Db.Changes.Changed += () => notices++;

        var result = await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 });

        Assert.False(result.Ok);
        Assert.Equal(0, notices);
    }

    [Fact]
    public async Task Plain_Edit_Without_Transaction_Also_Notifies()
    {
        var notices = 0;
        Db.Changes.Changed += () => notices++;
        var form = (await Svc.Suppliers.GetAsync(Supplier))!;
        form.ContactPerson = "New contact";

        await Svc.Suppliers.UpdateAsync(Supplier, form);

        Assert.True(notices >= 1);
    }
}
