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
    InvoiceView,
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

// Which role can do what.
// Client decision (replaces CLAUDE.md 6.1): Admin alone sees and uses Users & Roles, the Activity Log
// and Backup. Every other role can do all the business work (masters, invoices, ledgers, receipts,
// payments, day close, reports) exactly like Admin. Roles are still recorded on every log entry.
public static class RolePermissions
{
    // Admin-only features. For other roles these pages do not exist (404), not even a link.
    public static readonly IReadOnlySet<Permission> AdminOnly = new HashSet<Permission>
    {
        Permission.UsersManage,
        Permission.ActivityLogView,
        Permission.BackupRun,
    };

    private static readonly HashSet<Permission> All = Enum.GetValues<Permission>().ToHashSet();
    private static readonly HashSet<Permission> Business = All.Except(AdminOnly).ToHashSet();

    private static readonly Dictionary<UserRole, HashSet<Permission>> Map = new()
    {
        [UserRole.Admin] = All,
        [UserRole.DispatchOperator] = Business,
        [UserRole.BillingClerk] = Business,
        [UserRole.Accountant] = Business,
    };

    public static bool Has(UserRole role, Permission permission) => Map[role].Contains(permission);

    public static string DisplayName(UserRole role) => role switch
    {
        UserRole.DispatchOperator => "Dispatch Operator",
        UserRole.BillingClerk => "Billing Clerk",
        _ => role.ToString(),
    };
}
