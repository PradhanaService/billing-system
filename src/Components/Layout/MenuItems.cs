namespace WaterBilling.Components.Layout;

// One entry in the side menu.
//   Group  = heading the item sits under (e.g. "Masters")
//   Title  = text the user sees
//   Href   = URL of the page
//   IsReady = false until the module is built. Unready items show greyed out, not as links.
public sealed record MenuItem(string Group, string Title, string Href, bool IsReady);

// The whole menu lives in one list, so it is easy to read and easy to test.
// When a module is finished, flip its IsReady to true.
public static class MenuItems
{
    public static readonly IReadOnlyList<MenuItem> All =
    [
        new("Home",         "Dashboard",         "/",              IsReady: true),

        new("Masters",      "Supplier Master",   "/suppliers",     IsReady: false),
        new("Masters",      "Vehicle Master",    "/vehicles",      IsReady: false),
        new("Masters",      "Ledger Master",     "/ledgers",       IsReady: false),

        new("Transactions", "Sales Invoice",     "/invoices/new",  IsReady: false),
        new("Transactions", "Invoice Display",   "/invoices",      IsReady: false),
        new("Transactions", "Receipt",           "/receipts",      IsReady: false),
        new("Transactions", "Payment",           "/payments",      IsReady: false),

        new("Accounts",     "Ledger Statement",  "/statements",    IsReady: false),
        new("Accounts",     "Day Sales",         "/day-sales",     IsReady: false),
        new("Accounts",     "Reports",           "/reports",       IsReady: false),

        new("Admin",        "Users & Roles",     "/users",         IsReady: false),
        new("Admin",        "Activity Log",      "/activity-log",  IsReady: false),
        new("Admin",        "Backup",            "/backup",        IsReady: false),
    ];
}
