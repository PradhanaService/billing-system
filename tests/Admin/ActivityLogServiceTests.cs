using WaterBilling.Data.Entities;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Admin;

public class ActivityLogServiceTests : BillingFixture
{
    private static readonly DateOnly Today = new(2026, 11, 5);

    [Fact]
    public async Task FR_ADM_04_Log_Shows_Who_Did_What_With_Before_And_After()
    {
        var id = (await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 })).Value;
        await Svc.Invoices.CancelAsync(id, "Wrong trip", 1);
        Me.User = Db.AddUser(UserRole.Admin);

        var rows = await Svc.ActivityLog.SearchAsync(new ActivityFilter(Today, Today, ActivityCategory.Billing));

        var cancel = rows.Single(r => r.Action == "INVOICE_CANCELLED");
        Assert.Equal(("BillingClerk", "Wrong trip"), (cancel.Role, cancel.Reason));
        Assert.Contains("Cancelled", cancel.AfterJson);
        Assert.Contains("Posted", cancel.BeforeJson);
        Assert.Contains(rows, r => r.Action == "INVOICE_CREATED");
    }

    [Fact]
    public async Task Search_By_Text_And_User()
    {
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });
        Me.User = Db.AddUser(UserRole.Admin);

        Assert.Single(await Svc.ActivityLog.SearchAsync(new ActivityFilter(Today, Today, Text: "INV-1")));
        Assert.Empty(await Svc.ActivityLog.SearchAsync(new ActivityFilter(Today, Today, UserName: "nobody")));
    }

    [Fact]
    public async Task FR_ADM_07_Only_Admin_Views_And_Viewing_Is_Logged()
    {
        await Assert.ThrowsAsync<NotAllowedException>(() => Svc.ActivityLog.SearchAsync(new ActivityFilter(Today, Today)));

        Me.User = Db.AddUser(UserRole.Admin);
        await Svc.ActivityLog.SearchAsync(new ActivityFilter(Today, Today));

        using var ctx = Db.NewContext();
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "ACTIVITY_LOG_VIEWED");
    }

    [Fact]
    public async Task Date_Range_Uses_Indian_Days()
    {
        Me.User = Db.AddUser(UserRole.Admin);
        // 05/11/2026 10:00 UTC = 3:30 PM IST on the 5th. Searching the 4th must not include it.
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });

        Assert.DoesNotContain(await Svc.ActivityLog.SearchAsync(new ActivityFilter(Today.AddDays(-1), Today.AddDays(-1))),
            r => r.Action == "INVOICE_CREATED");
    }
}
