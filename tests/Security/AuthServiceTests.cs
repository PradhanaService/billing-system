using Microsoft.EntityFrameworkCore;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Security;

public class AuthServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly AuthService _auth;

    public AuthServiceTests() => _auth = new AuthService(_db, _db.Clock);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Seeds_One_Admin_Only_Once_With_Must_Change()
    {
        await _auth.EnsureAdminExistsAsync("Start@123");
        await _auth.EnsureAdminExistsAsync("Other@123");

        using var ctx = _db.NewContext();
        var admin = Assert.Single(ctx.Users);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.MustChangePassword);
        Assert.DoesNotContain("Start@123", admin.PasswordHash);   // stored as a hash, never plain
    }

    [Fact]
    public async Task Login_Succeeds_With_Right_Password_Any_Case_Of_Name()
    {
        _db.AddUser(UserRole.SalesPerson, "ravi");

        var result = await _auth.LoginAsync("RAVI", "Passw0rd!");

        Assert.True(result.Ok);
        Assert.Equal("ravi", UserInfo.FromPrincipal(result.Value)!.UserName);
    }

    [Theory]
    [InlineData("ravi", "wrong")]
    [InlineData("nobody", "Passw0rd!")]
    public async Task FR_ADM_03_Failed_Login_Is_Refused_And_Logged(string name, string password)
    {
        _db.AddUser(UserRole.SalesPerson, "ravi");

        var result = await _auth.LoginAsync(name, password);

        Assert.False(result.Ok);
        using var ctx = _db.NewContext();
        var log = Assert.Single(ctx.ActivityLogs, l => l.Action == "LOGIN_FAILED");
        Assert.Equal(name, log.UserName);
    }

    [Fact]
    public async Task Inactive_User_Cannot_Log_In()
    {
        var u = _db.AddUser(UserRole.SalesPerson, "ravi");
        using (var ctx = _db.NewContext())
        {
            ctx.Users.Single(x => x.Id == u.Id).Status = RecordStatus.Inactive;
            ctx.SaveChanges();
        }

        Assert.False((await _auth.LoginAsync("ravi", "Passw0rd!")).Ok);
    }

    [Fact]
    public async Task Changing_Password_Ends_Old_Logins()
    {
        _db.AddUser(UserRole.SalesPerson, "ravi");
        var oldLogin = (await _auth.LoginAsync("ravi", "Passw0rd!")).Value!;
        var id = UserInfo.FromPrincipal(oldLogin)!.Id;

        var changed = await _auth.ChangePasswordAsync(id, "Passw0rd!", "NewPass99");

        Assert.True(changed.Ok);
        Assert.False(await _auth.IsStillValidAsync(oldLogin));
        Assert.True(await _auth.IsStillValidAsync(changed.Value!));
        Assert.False(changed.Value!.HasClaim(UserInfo.MustChangeClaim, "true"));
        Assert.True((await _auth.LoginAsync("ravi", "NewPass99")).Ok);
    }

    [Theory]
    [InlineData("abc", "at least 4")]
    [InlineData("12", "at least 4")]
    [InlineData("RAVI", "same as the user name")]
    public async Task Weak_New_Password_Is_Refused(string newPassword, string message)
    {
        var u = _db.AddUser(UserRole.SalesPerson, "ravi");

        var result = await _auth.ChangePasswordAsync(u.Id, "Passw0rd!", newPassword);

        Assert.False(result.Ok);
        Assert.Contains(message, result.Error);
    }

    // Client decision: any kind of characters, in any mix, from 4 characters up.
    [Theory]
    [InlineData("1234")]          // numbers only
    [InlineData("abcd")]          // letters only
    [InlineData("@#$%")]          // symbols only
    [InlineData("a1@ Bc")]        // a mix, with a space
    public async Task Any_Password_Of_Four_Or_More_Characters_Is_Accepted(string newPassword)
    {
        var u = _db.AddUser(UserRole.SalesPerson, "ravi");

        var result = await _auth.ChangePasswordAsync(u.Id, "Passw0rd!", newPassword);

        Assert.True(result.Ok, result.Error);
        Assert.True((await _auth.LoginAsync("ravi", newPassword)).Ok);
    }

    [Fact]
    public async Task FR_ADM_05_Activity_Log_Rows_Cannot_Be_Changed_Or_Deleted()
    {
        await _auth.EnsureAdminExistsAsync("Start@123");
        using var ctx = _db.NewContext();

        var update = await Assert.ThrowsAnyAsync<Exception>(() =>
            ctx.Database.ExecuteSqlRawAsync("UPDATE ActivityLogs SET Action = 'X'"));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() =>
            ctx.Database.ExecuteSqlRawAsync("DELETE FROM ActivityLogs"));

        Assert.Contains("cannot be changed", update.Message);
        Assert.Contains("cannot be deleted", delete.Message);
    }
}
