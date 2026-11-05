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
