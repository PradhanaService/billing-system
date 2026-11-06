# Code guide: where each part of the business lives

Every source file opens with a short explanation in plain English. This page tells you which file to open.

## How the code is arranged
| Folder | What it holds | Example |
|---|---|---|
| `src/Pages` | The screens. They show data and collect input; they contain no business rules. | `Pages/Billing/SalesInvoice.razor` |
| `src/Services` | The business rules. Every screen asks a service; services check permissions and do the work. | `Services/Billing/InvoiceService.cs` |
| `src/Data` | The tables (one class per table) and the database connection. | `Data/Entities/Invoice.cs` |
| `src/Components` | Shared screen parts: layout, menu, and the PDF layouts. | `Components/Pdf/InvoicePdf.cs` |
| `tests` | Automated checks. Test names describe the rule they check, often with its requirement ID. | `tests/Billing/InvoiceServiceTests.cs` |

## Find a feature
| Business area | Screen | Rules | Table(s) |
|---|---|---|---|
| Sign in, passwords | `Pages/Account/Login.razor`, `ChangePassword.razor` | `Services/Security/AuthService.cs`, `PasswordRules.cs` | `User` |
| Who may do what (Admin, Sales Person) | (menu) `Components/Layout/MenuItems.cs` | `Services/Security/Permission.cs` | |
| Logins (one per role) | `Pages/Admin/Users.razor` | `Services/Security/UserService.cs` | `User` |
| "Created by" names | `Pages/Admin/SalesPersons.razor` | `Services/Admin/SalesPersonService.cs` | `SalesPerson` |
| Suppliers | `Pages/Masters/Suppliers.razor` | `Services/Masters/SupplierService.cs` | `Supplier` |
| Customers | `Pages/Masters/Customers.razor` | `Services/Masters/CustomerService.cs` | `Customer` |
| Vehicles | `Pages/Masters/Vehicles.razor` | `Services/Masters/VehicleService.cs` | `Vehicle` |
| Sales Invoice, cancel, PDF | `Pages/Billing/SalesInvoice.razor`, `InvoiceView.razor` | `Services/Billing/InvoiceService.cs`, `InvoiceMath.cs` | `Invoice`, `Voucher`, `LedgerEntry` |
| Receipts (oldest invoice paid first) | `Pages/Accounts/Receipts.razor` | `Services/Accounts/ReceiptService.cs` | `Voucher`, `ReceiptAllocation` |
| Payments | `Pages/Accounts/Payments.razor` | `Services/Accounts/PaymentService.cs` | `Voucher` |
| Ledgers (expense heads) | `Pages/Accounts/Ledgers.razor` | `Services/Accounts/LedgerMasterService.cs` | `Account`, `NumberSeries` |
| The accounts engine (debit = credit, reversals, numbering) | none | `Services/Ledger/LedgerPoster.cs` | `LedgerEntry`, `Voucher` |
| Business day 6 PM to 6 PM | none | `Services/Ledger/BusinessCalendar.cs`, `BusinessDays.cs` | `BusinessDay` |
| Day Sales and closing | `Pages/Accounts/DaySales.razor` | `Services/Accounts/DaySalesService.cs` | `BusinessDay` |
| Invoice Display | `Pages/Billing/InvoiceDisplay.razor` | `Services/Accounts/DaySalesService.cs` | |
| Statements | `Pages/Accounts/Statements.razor` | `Services/Accounts/StatementService.cs` | `LedgerEntry` |
| Who has to pay | `Pages/Accounts/Outstanding.razor` | `Services/Accounts/OutstandingService.cs` | `LedgerEntry`, `Invoice` |
| Dashboard | `Pages/Home.razor` | `Services/Accounts/DashboardService.cs` | |
| Reports and PDFs | `Pages/Reports/Reports.razor` | `Services/Reports/ReportService.cs`, `Components/Pdf/ReportPdf.cs` | |
| Activity Log | `Pages/Admin/ActivityLog.razor` | `Services/ActivityLogger.cs`, `Services/Admin/ActivityLogService.cs` | `ActivityLog` |
| Backup | `Pages/Admin/Backup.razor` | `Services/Admin/BackupService.cs` | (copies the database file) |
| Start-up, security, PDF downloads | none | `src/Program.cs` | |
| Money, volume, Indian dates | none | `Services/Common/Money.cs`, `Volume.cs`, `IndianTime.cs` | |

## Rules that hold everywhere
- **Money is exact**: stored as whole paise (₹150.00 is 15000), never as a decimal fraction (`Money.cs`).
- **The accounts are permanent**: entries are never changed or deleted; mistakes are reversed (`LedgerPoster.cs`, enforced by the database).
- **Each save is all-or-nothing**: an invoice and its accounts entries are saved together or not at all.
- **Permissions are checked on the server**: every service method starts with `RequireAsync(Permission....)`.
- **Every change leaves a record**: who, when, before and after, and the reason (`ActivityLogger.cs`).

More detail: `architecture.md` (how the parts fit), `ledger-design.md` (accounting rules), `database.md` (tables).
