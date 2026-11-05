# Progress log

## Session 1: Environment setup & project creation
- Done: .NET 8 solution, Blazor Server app, xUnit test project, folder layout, security headers, 3 startup tests.

## Session 2: Project structure & architecture
- Done: main layout (top bar + side menu), menu as a tested list, friendly 404 page, Content Security Policy, app made interactive, `docs/architecture.md`. 7 tests passing.
- Next: Session 3, database design & EF Core setup.
- Blocked: client answers to Q1 (Trip Cost) and Q2 (meter readings) before Session 3.

## Session 3: Database design & EF Core setup
- Done: EF Core + SQLite, `AppDbContext`, Suppliers table with checks and unique name index, first migration, money/volume/IST helpers, audit columns and edit-conflict check, `docs/data-conventions.md`. 36 tests passing.
- Next: Session 4, Supplier Master (service + screen).
- Blocked: Q1 (Trip Cost) before Session 5 Vehicle Master; Q2 (meter readings) before meter tables.

## Module 4: Login, roles & activity log
(Moved ahead of the masters so every later screen checks permissions and logs actions from the start.)
- Done: cookie login (30 min idle timeout), forced password change at first login, four roles with a permission table, server-side checks in every service, menu filtered by role, Users & Roles screen, append-only activity log (database triggers), sign-out by POST with antiforgery. 79 tests passing.

## Module 5: Supplier & Customer Masters
- Done: SupplierService and CustomerService (search, active/inactive filter, create, edit, delete-if-no-history), opening balance fixed after creation, edit-conflict check, activity log of every change with before/after. Screens for both. 97 tests passing.

## Module 6: Vehicle Master
- Done: Vehicles table and VehicleService. System code proposed (A1, A2 ...) and never reused; Admin may choose a code. Vehicle No. upper case, unique ignoring spaces; nickname default; capacity and rate checks; billable = vehicle Active + supplier Active; rate changes logged separately; supplier cannot be deleted while it has vehicles. 116 tests passing.
- Assumption: Trip Cost stored with no ledger effect; invoices use Rate (client question 1).

## Module 7: Ledger core
- Done: Accounts (seeded), NumberSeries, BusinessDays, Vouchers, append-only LedgerEntries; LedgerPoster (balanced check, open-day check, gapless numbers, running balances, vehicle balances, reversals with original value date); 6 PM business-day calendar; opening balances (P10) posted on party creation; history checks now include ledger lines. Proven that saves cannot interleave. 139 tests passing. See `docs/ledger-design.md`.

## Module 8: Sales Invoice
- Done: Invoices table; InvoiceService (amount by trip or volume with half-up rounding, gapless number shown before save, P1 credit / P2 cash posting through the party, Bill To supplier or customer tagged with vehicle, rate override needs permission + reason and is logged, cancel by reversal keeping the number, previous/next); Sales Invoice and Invoice View screens; invoice PDF (QuestPDF) behind a permission-checked endpoint; amount in words (Indian system). 166 tests passing.

## Module 9: Receipts
- Done: ReceiptAllocations table; ReceiptService (P3 posting, reference required for Bank/UPI, Dispatch cash-only, oldest-first allocation with vehicle-level credit lines and unallocated remainder, balance now/after, allocation preview, cancel with reversal that frees the invoices, history with filters); invoice cancel blocked while receipts pay it; Receipt screen with history. 180 tests passing.
- Note: opening balances are not invoices, so receipts do not allocate against them; any surplus stays "on account" (manual allocation is Version 1.1).

## Module 10: Payments & Ledger Master
- Done: PaymentService (P4 expense with optional vehicle tag, P5 refund to supplier/customer, Bank/UPI reference rule, cash-above-drawer confirmation, cancel by reversal, history); LedgerMasterService (create/edit/deactivate expense ledgers, system ledgers locked, delete only without entries, Admin sets a series' starting number before first use); Payment and Ledger Master screens. 195 tests passing.

## Module 11: Ledger Statements
- Done: StatementService (supplier, customer, vehicle, account/cash book; opening b/f, lines, period totals, closing c/f; by Date or by Value Date); generic ReportTable + ReportPdf + ReportView shared by statements and reports; statement PDF endpoint; Ledger Statement screen. Test proves statement balances equal the stored running balances. 203 tests passing.
