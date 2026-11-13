using WaterBilling.Services.Security;

namespace WaterBilling.Components.Layout;

// The side menu, written as one list so it is easy to read and change.
// Each entry: the heading it sits under, the text shown, the page address, and the
// permission needed to see it, and the icon drawn beside it (a name from Components/Icon.razor). The menu only HIDES links; every page and service checks
// the permission again on the server.
public sealed record MenuItem(string Group, string Title, string Href, Permission Permission, string Icon = "home");

public static class MenuItems
{
    public static readonly IReadOnlyList<MenuItem> All =
    [
        new("Home",         "Dashboard",         "/",                    Permission.DashboardView, "home"),

        new("Masters",      "Supplier Master",   "/suppliers",           Permission.MastersView, "users"),
        new("Masters",      "Customer Master",   "/customers",           Permission.MastersView, "building"),
        new("Masters",      "Vehicle Master",    "/vehicles",            Permission.MastersView, "truck"),
        new("Masters",      "Ledger Master",     "/ledgers",             Permission.LedgerManage, "layers"),

        new("Transactions", "Sales Invoice",     "/invoices/new",        Permission.InvoiceCreate, "file-text"),
        new("Transactions", "Invoice Display",   "/invoices",            Permission.DaySalesView, "table"),
        new("Transactions", "Receipt",           "/receipts",            Permission.ReceiptCreate, "download"),
        new("Transactions", "Payment",           "/payments",            Permission.PaymentCreate, "upload"),

        new("Accounts",     "Ledger Statement",  "/statements",          Permission.StatementsView, "book"),
        new("Accounts",     "Reports",           "/reports",             Permission.ReportsView, "chart"),

        // Admin only: the Sales login does not see these, and their addresses answer "Page not found".
        new("Admin",        "Users & Roles",     "/admin/users",         Permission.UsersManage, "shield"),
        new("Admin",        "Sales Persons",     "/admin/sales-persons", Permission.SalesPersonsManage, "badge"),
        new("Admin",        "Activity Log",      "/admin/activity-log",  Permission.ActivityLogView, "activity"),
        new("Admin",        "Backup",            "/admin/backup",        Permission.BackupRun, "database"),

        // Both logins: after Backup for the Admin, at the bottom of the menu for the Sales login.
        new("Recovery",     "Recovery",          "/recovery",            Permission.RecoveryManage, "recovery"),
    ];

    // The entries the logged-in user may see (none when nobody is logged in).
    public static IEnumerable<MenuItem> For(UserInfo? user) =>
        user is null ? [] : All.Where(i => user.Has(i.Permission));
}
