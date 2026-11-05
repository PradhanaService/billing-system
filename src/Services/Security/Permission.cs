using WaterBilling.Data.Entities;

namespace WaterBilling.Services.Security;

// Every action that needs a permission. Checked on the server in every service method
// (NFR-SEC-05). Pages also use them to hide menu items, but hiding is not security.
public enum Permission
{
    DashboardView,
    MastersView,
    MastersEdit,
    VehicleCodeOverride, // type a vehicle code instead of the proposed one (Admin)
    InvoiceCreate,
    InvoiceCancel,
    InvoiceOverrideRate,
    ReceiptCreate,
    ReceiptBankUpi,      // receipts by Bank or UPI (Dispatch Operators take cash only)
    ReceiptCancel,
    PaymentCreate,
    PaymentCancel,
    LedgerManage,        // Ledger Master
    StatementsView,
    DaySalesView,
    DayClose,
    ReportsView,
    UsersManage,
    ActivityLogView,
    BackupRun,
}

// Which role can do what (CLAUDE.md section 6.1). One table, easy to read and to test.
public static class RolePermissions
{
    private static readonly Dictionary<UserRole, HashSet<Permission>> Map = new()
    {
        [UserRole.Admin] = Enum.GetValues<Permission>().ToHashSet(),

        [UserRole.DispatchOperator] =
        [
            Permission.DashboardView, Permission.MastersView,
            Permission.InvoiceCreate, Permission.ReceiptCreate,
        ],

        [UserRole.BillingClerk] =
        [
            Permission.DashboardView, Permission.MastersView, Permission.MastersEdit,
            Permission.InvoiceCreate, Permission.InvoiceCancel,
            Permission.ReceiptCreate, Permission.ReceiptBankUpi,
            Permission.StatementsView, Permission.DaySalesView,
        ],

        [UserRole.Accountant] =
        [
            Permission.DashboardView, Permission.MastersView,
            Permission.InvoiceCancel,
            Permission.ReceiptCreate, Permission.ReceiptBankUpi, Permission.ReceiptCancel,
            Permission.PaymentCreate, Permission.PaymentCancel,
            Permission.LedgerManage, Permission.StatementsView,
            Permission.DaySalesView, Permission.DayClose,
            Permission.ReportsView, Permission.BackupRun,
        ],
    };

    public static bool Has(UserRole role, Permission permission) => Map[role].Contains(permission);

    public static string DisplayName(UserRole role) => role switch
    {
        UserRole.DispatchOperator => "Dispatch Operator",
        UserRole.BillingClerk => "Billing Clerk",
        _ => role.ToString(),
    };
}
