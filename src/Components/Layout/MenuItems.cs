using WaterBilling.Services.Security;

namespace WaterBilling.Components.Layout;

// The side menu, written as one list so it is easy to read and change.
// Each entry: the heading it sits under, the text shown, the page address, and the
// permission needed to see it. The menu only HIDES links; every page and service checks
// the permission again on the server.
public sealed record MenuItem(string Group, string Title, string Href, Permission Permission);

public static class MenuItems
{
    public static readonly IReadOnlyList<MenuItem> All =
    [
        new("Home",         "Dashboard",         "/",                    Permission.DashboardView),

        new("Masters",      "Supplier Master",   "/suppliers",           Permission.MastersView),
        new("Masters",      "Customer Master",   "/customers",           Permission.MastersView),
        new("Masters",      "Vehicle Master",    "/vehicles",            Permission.MastersView),
        new("Masters",      "Ledger Master",     "/ledgers",             Permission.LedgerManage),

        new("Transactions", "Sales Invoice",     "/invoices/new",        Permission.InvoiceCreate),
        new("Transactions", "Invoice Display",   "/invoices",            Permission.DaySalesView),
        new("Transactions", "Receipt",           "/receipts",            Permission.ReceiptCreate),
        new("Transactions", "Payment",           "/payments",            Permission.PaymentCreate),

        new("Accounts",     "Ledger Statement",  "/statements",          Permission.StatementsView),
        new("Accounts",     "Day Sales",         "/day-sales",           Permission.DaySalesView),
        new("Accounts",     "Reports",           "/reports",             Permission.ReportsView),

        // Admin only: the Sales login does not see these, and their addresses answer "Page not found".
        new("Admin",        "Users & Roles",     "/admin/users",         Permission.UsersManage),
        new("Admin",        "Sales Persons",     "/admin/sales-persons", Permission.SalesPersonsManage),
        new("Admin",        "Activity Log",      "/admin/activity-log",  Permission.ActivityLogView),
        new("Admin",        "Backup",            "/admin/backup",        Permission.BackupRun),

        // Both logins: after Backup for the Admin, at the bottom of the menu for the Sales login.
        new("Recovery",     "Recovery",          "/recovery",            Permission.RecoveryManage),
    ];

    // The entries the logged-in user may see (none when nobody is logged in).
    public static IEnumerable<MenuItem> For(UserInfo? user) =>
        user is null ? [] : All.Where(i => user.Has(i.Permission));
}
