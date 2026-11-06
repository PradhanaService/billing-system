using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Security;

public class UserServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeCurrentUser _me;
    private readonly UserService _users;

    public UserServiceTests()
    {
        _me = new FakeCurrentUser(_db.AddUser(UserRole.Admin, "boss"));
        _users = new UserService(_db, _me, _db.Clock);
    }

    public void Dispose() => _db.Dispose();

    private static UserForm Form(string name = "kumar", UserRole role = UserRole.SalesPerson) =>
        new() { UserName = name, FullName = "Kumar S", Role = role, TemporaryPassword = "Temp1234" };

    [Fact]
    public async Task FR_ADM_01_Admin_Creates_User_With_Must_Change_Password()
    {
        var result = await _users.CreateAsync(Form());

        Assert.True(result.Ok);
        using var ctx = _db.NewContext();
        var user = ctx.Users.Single(u => u.Id == result.Value);
        Assert.True(user.MustChangePassword);
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "USER_CREATED" && l.EntityId == user.Id);
    }

    [Fact]
    public async Task User_Name_Is_Unique_Ignoring_Case()
    {
        await _users.CreateAsync(Form("kumar"));

        var second = await _users.CreateAsync(Form("KUMAR"));

        Assert.False(second.Ok);
        Assert.Contains("already exists", second.Error);
    }

    [Theory]
    [InlineData(UserRole.SalesPerson)]
    public async Task NFR_SEC_05_Only_Admin_Can_Manage_Users(UserRole role)
    {
        _me.User = _db.AddUser(role);

        await Assert.ThrowsAsync<NotAllowedException>(() => _users.ListAsync());
        await Assert.ThrowsAsync<NotAllowedException>(() => _users.CreateAsync(Form()));
    }

    [Fact]
    public async Task Not_Logged_In_Is_Refused()
    {
        _me.User = null;
        await Assert.ThrowsAsync<NotAllowedException>(() => _users.ListAsync());
    }

    [Fact]
    public async Task Admin_Cannot_Deactivate_Self()
    {
        var me = (await _users.ListAsync()).Single(u => u.UserName == "boss");

        var result = await _users.SetStatusAsync(me.Id, RecordStatus.Inactive, me.Version);

        Assert.False(result.Ok);
    }

    [Fact]
    public async Task Last_Active_Admin_Cannot_Lose_Admin_Role()
    {
        var me = (await _users.ListAsync()).Single(u => u.UserName == "boss");

        var result = await _users.UpdateAsync(me.Id, new UserForm { UserName = "boss", FullName = "Boss", Role = UserRole.SalesPerson }, me.Version);

        Assert.False(result.Ok);
        Assert.Contains("last active Admin", result.Error);
    }

    [Fact]
    public async Task Deactivating_Or_Changing_Role_Ends_That_Users_Logins()
    {
        var auth = new AuthService(_db, _db.Clock);
        _db.AddUser(UserRole.SalesPerson, "ravi");
        var login = (await auth.LoginAsync("ravi", "Passw0rd!")).Value!;
        var ravi = (await _users.ListAsync()).Single(u => u.UserName == "ravi");

        await _users.SetStatusAsync(ravi.Id, RecordStatus.Inactive, ravi.Version);

        Assert.False(await auth.IsStillValidAsync(login));
    }

    [Fact]
    public async Task NFR_CON_05_Stale_Edit_Is_Refused()
    {
        var created = await _users.CreateAsync(Form());
        var row = (await _users.ListAsync()).Single(u => u.Id == created.Value);
        await _users.UpdateAsync(row.Id, new UserForm { UserName = "kumar", FullName = "First edit", Role = row.Role }, row.Version);

        var stale = await _users.UpdateAsync(row.Id, new UserForm { UserName = "kumar", FullName = "Second edit", Role = row.Role }, row.Version);

        Assert.False(stale.Ok);
        Assert.Contains("changed by someone else", stale.Error);
    }

    // Client rule: one login per role (one Admin login, one shared Sales login).
    [Fact]
    public async Task Only_One_Active_Login_Per_Role()
    {
        var first = await _users.CreateAsync(Form("sales"));
        Assert.True(first.Ok);

        var second = await _users.CreateAsync(Form("sales2"));
        Assert.False(second.Ok);
        Assert.Contains("Only one login per role", second.Error);
        Assert.False((await _users.CreateAsync(Form("admin2", UserRole.Admin))).Ok);   // "boss" is the Admin

        // Deactivate the first Sales login, then a new one is allowed; the old one cannot come back.
        var row = (await _users.ListAsync()).Single(u => u.UserName == "sales");
        await _users.SetStatusAsync(row.Id, RecordStatus.Inactive, row.Version);
        Assert.True((await _users.CreateAsync(Form("sales2"))).Ok);
        row = (await _users.ListAsync()).Single(u => u.UserName == "sales");
        Assert.False((await _users.SetStatusAsync(row.Id, RecordStatus.Active, row.Version)).Ok);
    }
}
