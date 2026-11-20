using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Billing;

namespace WaterBilling.Tests.Admin;

// Module 10: Password Management and the Activity Log "Done by" column.
public class PasswordManagementTests : BillingFixture
{
    private UserService Users => new(Db, Me, Db.Clock);
    private readonly UserInfo _admin;

    public PasswordManagementTests()
    {
        _admin = Db.AddUser(UserRole.Admin, "admin");
        Me.User = _admin;
    }

    [Fact]
    public async Task PWD_01_Shows_Exactly_Two_Logins()
    {
        var cards = await Users.LoginsAsync();
        Assert.Equal([UserRole.Admin, UserRole.SalesPerson], cards.Select(c => c.Role));
    }

    [Fact]
    public async Task PWD_02_Admin_Sets_The_Sales_Password_And_Old_Sessions_End()
    {
        var sales = (await Users.LoginsAsync())[1];
        string stamp;
        using (var db = Db.NewContext()) stamp = db.Users.Single(u => u.Id == sales.Id).SecurityStamp;

        Assert.Contains("do not match", (await Users.SetPasswordAsync(UserRole.SalesPerson, "abcd1", "abcd2")).Error);
        Assert.False((await Users.SetPasswordAsync(UserRole.SalesPerson, "ab", "ab")).Ok);   // too short
        var result = await Users.SetPasswordAsync(UserRole.SalesPerson, "Shop#2026", "Shop#2026");

        Assert.True(result.Ok, result.Error);
        using var check = Db.NewContext();
        var u = check.Users.Single(x => x.Id == sales.Id);
        Assert.True(AuthService.VerifyPassword(u, "Shop#2026"));
        Assert.False(u.MustChangePassword);                     // works from the next sign-in as it is
        Assert.NotEqual(stamp, u.SecurityStamp);                 // open sessions are signed out
        Assert.Equal(Db.Clock.GetUtcNow().UtcDateTime, u.PasswordChangedAtUtc);
        Assert.Contains(check.ActivityLogs, l => l.Action == "PASSWORD_SET" && l.EntityId == sales.Id);
    }

    [Fact]
    public async Task PWD_03_Missing_Sales_Login_Is_Created_By_Setting_Its_Password()
    {
        using (var db = Db.NewContext())
        {
            foreach (var u in db.Users.Where(x => x.Role == UserRole.SalesPerson)) u.Status = RecordStatus.Inactive;
            db.SaveChanges();
        }
        Assert.Null((await Users.LoginsAsync())[1].Id);

        Assert.True((await Users.SetPasswordAsync(UserRole.SalesPerson, "Shop#2026", "Shop#2026")).Ok);

        var card = (await Users.LoginsAsync())[1];
        Assert.Equal(UserService.SalesUserName, card.UserName);
    }

    [Fact]
    public async Task PWD_04_Sales_Login_Cannot_Change_Passwords()
    {
        Me.User = Db.AddUser(UserRole.SalesPerson);
        await Assert.ThrowsAsync<NotAllowedException>(() => Users.SetPasswordAsync(UserRole.Admin, "x1234", "x1234"));
        await Assert.ThrowsAsync<NotAllowedException>(() => Users.LoginsAsync());
    }

    [Fact]
    public async Task FR_ADM_04_Done_By_Is_The_Sales_Person_Or_Admin_And_Can_Be_Filtered()
    {
        Me.User = Db.AddUser(UserRole.SalesPerson);
        await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150 });   // by Ravi
        Me.User = _admin;
        await Svc.Invoices.CreateAsync(new InvoiceForm { VehicleId = TripVehicle, Rate = 150 });

        var log = new ActivityLogService(Db, Me, Db.Clock);
        var day = new DateOnly(2026, 11, 5);
        var created = (await log.SearchAsync(new ActivityFilter(day, day, Text: "INVOICE_CREATED"))).ToList();
        Assert.Equal(["Admin", "Ravi"], created.Select(r => r.DoneByName));
        Assert.Equal(["Admin login", "Sales login"], created.Select(r => r.LoginText));

        var ravi = await log.SearchAsync(new ActivityFilter(day, day, DoneBy: "Ravi"));
        Assert.All(ravi, r => Assert.Equal("Ravi", r.DoneBy));
        Assert.Single(ravi);
        var admin = await log.SearchAsync(new ActivityFilter(day, day, DoneBy: "Admin"));
        Assert.All(admin, r => Assert.Equal("Admin", r.Role));
    }
}
