using Microsoft.EntityFrameworkCore;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Recovery;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Recovery;

// Soft delete and Recovery: a delete only hides the record; Admin and Sales can restore it for 30 days;
// after that it is removed for good. Every step is in the activity log.
// The fixture runs as the Sales login.
public class RecoveryServiceTests : BillingFixture
{
    private RecoveryService Recovery => Svc.Recovery;

    private async Task<long> NewSupplierAsync(string name = "Kaveri Tankers") =>
        (await Svc.Suppliers.CreateAsync(new SupplierForm { Name = name, ShortName = "Kaveri", Phone = "9123456780" })).Value;

    [Fact]
    public async Task Delete_Keeps_The_Row_But_Hides_It_Everywhere()
    {
        var id = await NewSupplierAsync();

        Assert.True((await Svc.Suppliers.DeleteAsync(id, Seller)).Ok);

        using var ctx = Db.NewContext();
        var row = ctx.Suppliers.IgnoreQueryFilters().Single(s => s.Id == id);   // still in the database
        Assert.NotNull(row.DeletedAtUtc);
        Assert.Equal(Me.User!.Id, row.DeletedByUserId);
        Assert.DoesNotContain(ctx.Suppliers, s => s.Id == id);                    // gone from every list
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "SUPPLIER_DELETED" && l.EntityId == id);
    }

    [Fact]
    public async Task Every_Kind_Of_Delete_Appears_In_Recovery_And_Can_Be_Restored()
    {
        var supplier = await NewSupplierAsync();
        var ledger = (await Svc.Ledgers.CreateAsync(new LedgerForm { Name = "Tyre Repair" })).Value;
        await Svc.Suppliers.DeleteAsync(supplier, Seller);
        await Svc.Customers.DeleteAsync(Customer, Seller);
        await Svc.Vehicles.DeleteAsync(VolumeVehicle, Seller);
        await Svc.Ledgers.DeleteAsync(ledger, Seller);
        var sales = Me.User;
        Me.User = Db.AddUser(UserRole.Admin, "boss");
        await Svc.SalesPersons.RemoveAsync(Seller);
        Me.User = sales;

        var rows = await Recovery.ListAsync();
        Assert.Equal(5, rows.Count);
        Assert.Equal(Enum.GetValues<RecoveryKind>().Order(), rows.Select(r => r.Kind).Order());

        foreach (var r in rows) Assert.True((await Recovery.RestoreAsync(r.Kind, r.Id)).Ok, r.Kind.ToString());

        Assert.Empty(await Recovery.ListAsync());
        Assert.NotNull(await Svc.Vehicles.GetAsync(VolumeVehicle));
        Assert.Contains(await Svc.SalesPersons.ListActiveAsync(), p => p.Id == Seller);
        using var ctx = Db.NewContext();
        Assert.Contains(ctx.Suppliers, s => s.Id == supplier);
        Assert.Contains(ctx.Customers, c => c.Id == Customer);
        Assert.Contains(ctx.Accounts, a => a.Id == ledger);
        Assert.Equal(5, ctx.ActivityLogs.Count(l => l.Action.EndsWith("_RESTORED")));
    }

    [Fact]
    public async Task Sales_Login_And_Admin_Login_Can_Both_Restore()
    {
        var a = await NewSupplierAsync("Supplier A");
        var b = await NewSupplierAsync("Supplier B");
        await Svc.Suppliers.DeleteAsync(a, Seller);
        await Svc.Suppliers.DeleteAsync(b, Seller);

        Assert.True((await Recovery.RestoreAsync(RecoveryKind.Supplier, a)).Ok);   // Sales login
        Me.User = Db.AddUser(UserRole.Admin, "boss");
        Assert.True((await Recovery.RestoreAsync(RecoveryKind.Supplier, b)).Ok);   // Admin login
    }

    [Fact]
    public async Task Restore_Is_Refused_When_The_Name_Is_Used_Again()
    {
        var old = await NewSupplierAsync();
        await Svc.Suppliers.DeleteAsync(old, Seller);
        Assert.True((await Svc.Suppliers.CreateAsync(new SupplierForm { Name = "kaveri tankers", ShortName = "K", Phone = "9123456789" })).Ok);

        var result = await Recovery.RestoreAsync(RecoveryKind.Supplier, old);

        Assert.False(result.Ok);
        Assert.Contains("exists again", result.Error);
    }

    [Fact]
    public async Task Vehicle_Restore_Gets_Its_Number_Back_Unless_It_Was_Reused()
    {
        await Svc.Vehicles.DeleteAsync(VolumeVehicle, Seller);
        var reused = (await Svc.Vehicles.CreateAsync(new VehicleForm { VehicleNo = "TN 67 C", SupplierId = Supplier, TankCapacityLitres = 1000, Rate = 1 })).Value;

        Assert.False((await Recovery.RestoreAsync(RecoveryKind.Vehicle, VolumeVehicle)).Ok);

        await Svc.Vehicles.DeleteAsync(reused, Seller);
        Assert.True((await Recovery.RestoreAsync(RecoveryKind.Vehicle, VolumeVehicle)).Ok);
        Assert.Equal("TN 67 C", (await Svc.Vehicles.GetAsync(VolumeVehicle))!.VehicleNo);
    }

    [Fact]
    public async Task After_30_Days_It_Is_Removed_For_Good_And_Logged()
    {
        var supplier = await NewSupplierAsync();
        await Svc.Suppliers.DeleteAsync(supplier, Seller);
        await Svc.Vehicles.DeleteAsync(VolumeVehicle, Seller);
        var code = Db.NewContext().Vehicles.IgnoreQueryFilters().Single(v => v.Id == VolumeVehicle).Code;

        Db.Clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(0, await Recovery.RemoveExpiredAsync());       // still restorable on day 29
        Assert.Equal(2, (await Recovery.ListAsync()).Count);

        Db.Clock.Advance(TimeSpan.FromDays(1.5));
        Assert.Equal(2, await Recovery.RemoveExpiredAsync());

        Assert.Empty(await Recovery.ListAsync());
        Assert.False((await Recovery.RestoreAsync(RecoveryKind.Supplier, supplier)).Ok);
        using var ctx = Db.NewContext();
        Assert.DoesNotContain(ctx.Suppliers.IgnoreQueryFilters(), s => s.Id == supplier);
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "SUPPLIER_REMOVED_FOR_GOOD" && l.UserName == "System");
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "VEHICLE_REMOVED_FOR_GOOD");
        Assert.Contains(ctx.RetiredVehicleCodes, r => r.Code == code);
    }

    [Fact]
    public async Task Code_Of_A_Vehicle_Removed_For_Good_Is_Never_Reused()
    {
        await Svc.Vehicles.DeleteAsync(VolumeVehicle, Seller);                 // the highest code, A2
        Db.Clock.Advance(TimeSpan.FromDays(31));
        await Recovery.RemoveExpiredAsync();

        Assert.Equal("A3", await Svc.Vehicles.ProposeCodeAsync());
    }

    [Fact]
    public async Task Sales_Person_Used_On_Invoices_Keeps_Its_Name_On_Them_Forever()
    {
        var invoice = (await Svc.Invoices.CreateAsync(new WaterBilling.Services.Billing.InvoiceForm
            { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 })).Value;
        Me.User = Db.AddUser(UserRole.Admin, "boss");
        await Svc.SalesPersons.RemoveAsync(Seller);

        Db.Clock.Advance(TimeSpan.FromDays(31));
        await Recovery.RemoveExpiredAsync();

        Assert.Equal("Ravi", (await Svc.Invoices.GetAsync(invoice))!.CreatedBy);
        Assert.True((await Svc.SalesPersons.AddAsync(new SalesPersonForm { Name = "Ravi" })).Ok);   // brings Ravi back
        Assert.Equal(Seller, Assert.Single(await Svc.SalesPersons.ListActiveAsync()).Id);
    }

    [Fact]
    public async Task Sales_Login_Must_Choose_Who_Is_Deleting()
    {
        var id = await NewSupplierAsync();

        var missing = await Svc.Suppliers.DeleteAsync(id);
        Assert.False(missing.Ok);
        Assert.Contains("Deleted by", missing.Error);
        Assert.False((await Svc.Customers.DeleteAsync(Customer)).Ok);
        Assert.False((await Svc.Vehicles.DeleteAsync(VolumeVehicle)).Ok);
        Assert.False((await Svc.Ledgers.DeleteAsync(11)).Ok);

        Assert.True((await Svc.Suppliers.DeleteAsync(id, Seller)).Ok);
        Assert.Equal("Ravi", Assert.Single(await Recovery.ListAsync()).DeletedBy);
        using var ctx = Db.NewContext();
        Assert.Equal("Deleted by Ravi", ctx.ActivityLogs.Single(l => l.Action == "SUPPLIER_DELETED").Reason);
    }

    [Fact]
    public async Task Admin_Login_Deletes_Are_Recorded_As_Admin()
    {
        var id = await NewSupplierAsync();
        Me.User = Db.AddUser(UserRole.Admin, "boss");

        Assert.True((await Svc.Suppliers.DeleteAsync(id, Seller)).Ok);   // a name sent anyway is ignored

        Assert.Equal("Admin", Assert.Single(await Recovery.ListAsync()).DeletedBy);
        using var ctx = Db.NewContext();
        Assert.Null(ctx.Suppliers.IgnoreQueryFilters().Single(s => s.Id == id).DeletedBySalesPersonId);
        Assert.Equal("Deleted by Admin", ctx.ActivityLogs.Single(l => l.Action == "SUPPLIER_DELETED").Reason);
    }
}
