using WaterBilling.Services.Security;

namespace WaterBilling.Components.Layout;

// One entry in the side menu.
//   Group      = heading the item sits under
//   Title      = text the user sees
//   Href       = URL of the page
//   Permission = who sees it. The page itself checks again; the menu only hides links.
//   Icon       = icon name (Components/Icon.razor)
public sealed record MenuItem(string Group, string Title, string Href, Permission Permission, string Icon, bool IsReady = true);

// The whole menu lives in one list, so it is easy to read and easy to test.
// Order follows the designs: Dashboard, Vehicle Master, Sales Invoice, Invoice Display,
// Transactions, Ledger Master, Reports.
public static class MenuItems
{
    public static readonly IReadOnlyList<MenuItem> All =
    [
        new("Main menu", "Dashboard",        "/",                   Permission.DashboardView,   "home"),
        new("Main menu", "Vehicle Master",   "/vehicles",           Permission.MastersView,     "truck"),
        new("Main menu", "Sales Invoice",    "/invoices/new",       Permission.InvoiceCreate,   "invoice"),
        new("Main menu", "Invoice Display",  "/invoices",           Permission.DaySalesView,    "grid"),
        new("Main menu", "Transactions",     "/transactions",       Permission.ReceiptCreate,   "swap"),
        new("Main menu", "Ledger Master",    "/ledgers",            Permission.LedgerManage,    "layers"),
        new("Main menu", "Reports",          "/reports",            Permission.ReportsView,     "chart"),

        new("Accounts",  "Suppliers",        "/suppliers",          Permission.MastersView,     "users"),
        new("Accounts",  "Customers",        "/customers",          Permission.MastersView,     "briefcase"),
        new("Accounts",  "Day Sales",        "/day-sales",          Permission.DaySalesView,    "clock"),
        new("Accounts",  "Ledger Statement", "/statements",         Permission.StatementsView,  "book"),

        // Admin only: for every other role these items are not in the menu and the pages answer 404.
        new("Admin",     "Users & Roles",    "/admin/users",        Permission.UsersManage,     "shield"),
        new("Admin",     "Activity Log",     "/admin/activity-log", Permission.ActivityLogView, "activity"),
        new("Admin",     "Backup",           "/admin/backup",       Permission.BackupRun,       "database"),
    ];

    public static IEnumerable<MenuItem> For(UserInfo? user) =>
        user is null ? [] : All.Where(i => user.Has(i.Permission));
}
