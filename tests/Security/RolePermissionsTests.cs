using WaterBilling.Data.Entities;
using WaterBilling.Services.Security;

namespace WaterBilling.Tests.Security;

// The role table from CLAUDE.md section 6.1.
public class RolePermissionsTests
{
    [Fact]
    public void Admin_Has_Every_Permission() =>
        Assert.All(Enum.GetValues<Permission>(), p => Assert.True(RolePermissions.Has(UserRole.Admin, p)));

    [Theory]
    [InlineData(UserRole.DispatchOperator, Permission.InvoiceCreate, true)]
    [InlineData(UserRole.DispatchOperator, Permission.ReceiptCreate, true)]
    [InlineData(UserRole.DispatchOperator, Permission.ReceiptBankUpi, false)]
    [InlineData(UserRole.DispatchOperator, Permission.PaymentCreate, false)]
    [InlineData(UserRole.DispatchOperator, Permission.DayClose, false)]
    [InlineData(UserRole.BillingClerk, Permission.InvoiceCancel, true)]
    [InlineData(UserRole.BillingClerk, Permission.StatementsView, true)]
    [InlineData(UserRole.BillingClerk, Permission.PaymentCreate, false)]
    [InlineData(UserRole.Accountant, Permission.DayClose, true)]
    [InlineData(UserRole.Accountant, Permission.PaymentCreate, true)]
    [InlineData(UserRole.Accountant, Permission.ReportsView, true)]
    [InlineData(UserRole.Accountant, Permission.UsersManage, false)]
    [InlineData(UserRole.Accountant, Permission.InvoiceOverrideRate, false)]
    public void Role_Table(UserRole role, Permission permission, bool expected) =>
        Assert.Equal(expected, RolePermissions.Has(role, permission));

    [Fact]
    public void FR_ADM_07_Only_Admin_Sees_Activity_Log() =>
        Assert.All(Enum.GetValues<UserRole>().Where(r => r != UserRole.Admin),
            r => Assert.False(RolePermissions.Has(r, Permission.ActivityLogView)));
}
