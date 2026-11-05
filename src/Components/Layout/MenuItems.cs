using WaterBilling.Services.Security;

namespace WaterBilling.Components.Layout;

// One entry in the side menu.
//   Group      = heading the item sits under (e.g. "Masters")
//   Title      = text the user sees
//   Href       = URL of the page
//   Permission = who sees it. The page itself checks again; the menu only hides links.
//   IsReady    = false until the module is built. Unready items show greyed out, not as links.
public sealed record MenuItem(string Group, string Title, string Href, Permission Permission, bool IsReady);

// The whole menu lives in one list, so it is easy to read and easy to test.
public static class MenuItems
{
    public static readonly IReadOnlyList<MenuItem> All =
    [
        new("Home",         "Dashboard",         "/",              Permission.DashboardView,   IsReady: true),

        new("Masters",      "Supplier Master",   "/suppliers",     Permission.MastersView,     IsReady: true),
        new("Masters",      "Customer Master",   "/customers",     Permission.MastersView,     IsReady: true),
        new("Masters",      "Vehicle Master",    "/vehicles",      Permission.MastersView,     IsReady: true),
        new("Masters",      "Ledger Master",     "/ledgers",       Permission.LedgerManage,    IsReady: false),

        new("Transactions", "Sales Invoice",     "/invoices/new",  Permission.InvoiceCreate,   IsReady: false),
        new("Transactions", "Invoice Display",   "/invoices",      Permission.DaySalesView,    IsReady: false),
        new("Transactions", "Receipt",           "/receipts",      Permission.ReceiptCreate,   IsReady: false),
        new("Transactions", "Payment",           "/payments",      Permission.PaymentCreate,   IsReady: false),

        new("Accounts",     "Ledger Statement",  "/statements",    Permission.StatementsView,  IsReady: false),
        new("Accounts",     "Day Sales",         "/day-sales",     Permission.DaySalesView,    IsReady: false),
        new("Accounts",     "Reports",           "/reports",       Permission.ReportsView,     IsReady: false),

        new("Admin",        "Users & Roles",     "/admin/users",   Permission.UsersManage,     IsReady: true),
        new("Admin",        "Activity Log",      "/admin/activity-log", Permission.ActivityLogView, IsReady: false),
        new("Admin",        "Backup",            "/admin/backup",  Permission.BackupRun,       IsReady: false),
    ];

    public static IEnumerable<MenuItem> For(UserInfo? user) =>
        user is null ? [] : All.Where(i => user.Has(i.Permission));
}
