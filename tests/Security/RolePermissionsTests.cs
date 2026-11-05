using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Security;

// The role table from CLAUDE.md section 6.1.
public class RolePermissionsTests
{
    [Fact]
    public void Admin_Has_Every_Permission() =>
        Assert.All(Enum.GetValues<Permission>(), p => Assert.True(RolePermissions.Has(UserRole.Admin, p)));

    // Client rule: every non-admin role does all business work, and nothing admin-only.
    public static IEnumerable<object[]> NonAdminRolesAndPermissions() =>
        from role in Enum.GetValues<UserRole>().Where(r => r != UserRole.Admin)
        from p in Enum.GetValues<Permission>()
        select new object[] { role, p };

    [Theory]
    [MemberData(nameof(NonAdminRolesAndPermissions))]
    public void Non_Admin_Roles_Have_Everything_Except_Users_Log_And_Backup(UserRole role, Permission permission) =>
        Assert.Equal(!RolePermissions.AdminOnly.Contains(permission), RolePermissions.Has(role, permission));

    [Fact]
    public void Admin_Only_Is_Exactly_Users_Log_And_Backup() =>
        Assert.Equal(new[] { Permission.UsersManage, Permission.ActivityLogView, Permission.BackupRun }.OrderBy(p => p),
            RolePermissions.AdminOnly.OrderBy(p => p));

    [Fact]
    public void FR_ADM_07_Only_Admin_Sees_Activity_Log() =>
        Assert.All(Enum.GetValues<UserRole>().Where(r => r != UserRole.Admin),
            r => Assert.False(RolePermissions.Has(r, Permission.ActivityLogView)));
}
