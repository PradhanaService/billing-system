using WaterBilling.Data.Entities;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Admin;

// The "Created by" list on the Sales Invoice, managed by the Admin.
// The fixture already holds one name, "Ravi" (Seller).
public class SalesPersonServiceTests : BillingFixture
{
    private SalesPersonService Svc2 => Svc.SalesPersons;

    private InvoiceForm Invoice(long salesPerson) => new() { SalesPersonId = salesPerson, VehicleId = TripVehicle, Rate = 150 };

    private void AsAdmin() => Me.User = Db.AddUser(UserRole.Admin);

    [Fact]
    public async Task Admin_Adds_Names_And_Duplicates_Are_Refused()
    {
        AsAdmin();
        Assert.True((await Svc2.AddAsync(new SalesPersonForm { Name = "  Kumar  S " })).Ok);
        Assert.False((await Svc2.AddAsync(new SalesPersonForm { Name = "kumar s" })).Ok);
        Assert.False((await Svc2.AddAsync(new SalesPersonForm { Name = " " })).Ok);

        Assert.Equal(["Kumar S", "Ravi"], (await Svc2.ListActiveAsync()).Select(p => p.Name));
        using var ctx = Db.NewContext();
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "SALES_PERSON_ADDED");
    }

    [Fact]
    public async Task Unused_Name_Is_Deleted_When_Removed()
    {
        AsAdmin();
        await Svc2.AddAsync(new SalesPersonForm { Name = "Kumar" });
        var kumar = (await Svc2.ListAsync()).Single(p => p.Name == "Kumar");

        Assert.True((await Svc2.RemoveAsync(kumar.Id)).Ok);

        using var ctx = Db.NewContext();
        Assert.DoesNotContain(ctx.SalesPersons, p => p.Name == "Kumar");
    }

    [Fact]
    public async Task Used_Name_Leaves_The_List_But_Stays_On_Old_Invoices()
    {
        var id = (await Svc.Invoices.CreateAsync(Invoice(Seller))).Value;
        AsAdmin();

        Assert.True((await Svc2.RemoveAsync(Seller)).Ok);

        Assert.Empty(await Svc2.ListActiveAsync());
        Assert.Equal("Ravi", (await Svc.Invoices.GetAsync(id))!.CreatedBy);
        Assert.False((await Svc.Invoices.CreateAsync(Invoice(Seller))).Ok);   // can no longer be chosen

        // Adding the same name again brings the same person back.
        Assert.True((await Svc2.AddAsync(new SalesPersonForm { Name = "RAVI" })).Ok);
        Assert.Equal(Seller, Assert.Single(await Svc2.ListActiveAsync()).Id);
    }

    [Fact]
    public async Task Sales_Login_Sees_The_List_But_Cannot_Change_It()
    {
        Assert.Single(await Svc2.ListActiveAsync());   // fixture acts as the Sales login

        await Assert.ThrowsAsync<NotAllowedException>(() => Svc2.ListAsync());
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc2.AddAsync(new SalesPersonForm { Name = "X" }));
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc2.RemoveAsync(Seller));
    }

    [Fact]
    public async Task Invoice_Needs_A_Created_By_Name_And_Records_It()
    {
        var missing = await Svc.Invoices.CreateAsync(Invoice(0));
        Assert.False(missing.Ok);
        Assert.Contains("Created by", missing.Error);

        var id = (await Svc.Invoices.CreateAsync(Invoice(Seller))).Value;

        Assert.Equal("Ravi", (await Svc.Invoices.GetAsync(id))!.CreatedBy);
        using var ctx = Db.NewContext();
        Assert.Contains("Ravi", ctx.ActivityLogs.Single(l => l.Action == "INVOICE_CREATED").AfterJson);
    }
}
