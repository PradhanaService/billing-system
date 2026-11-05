# Water Supply & Fleet Billing System

Billing, double-entry ledger and day closing for **Shree Karunambika Traders**: tankers fill water,
every fill becomes an invoice, every invoice posts to the ledger, receipts and payments are recorded,
and the accountant closes each business day.

Stack: C# / .NET 8, Blazor Server, EF Core + SQLite, QuestPDF, xUnit. Runs on one Windows PC.

## Run from source
Requires the .NET 8 SDK.
```
dotnet run --project src --launch-profile http
```
Open http://localhost:5223 and sign in as `admin` / `Admin@123` (you must choose a new password).
The database is created at `src/App_Data/waterbilling.db` on first start.

## Test
```
dotnet test
```
360 tests (unit, service, HTTP). See `docs/test-report.md` and `docs/requirement-test-matrix.md`.

## Install on the shop PC
See `docs/install-guide.md`: run `installer\build-installer.ps1` on Windows, then `WaterBillingSetup.exe`.

## Folders
| Folder | What lives there |
|---|---|
| `src/Data` | EF Core `AppDbContext`, entities, migrations |
| `src/Services` | All business rules (masters, billing, ledger, accounts, reports, admin, security) |
| `src/Pages` | Screens (Blazor pages) |
| `src/Components` | Layout, menu, shared UI, PDF layouts |
| `tests` | xUnit tests, named by requirement ID |
| `installer` | Inno Setup script and build script |
| `docs` | Spec, design notes, guides, handover pack |

## Handover pack (for the reviewer)
| Document | |
|---|---|
| `docs/architecture.md` | Layers, request flow, decisions |
| `docs/database.md` | Database diagram and tables |
| `docs/ledger-design.md` | Posting rules and guarantees |
| `docs/data-conventions.md` | Money, volume, time; SQLite replacements for PostgreSQL features |
| `docs/requirement-test-matrix.md` | Requirement ID → tests → result |
| `docs/test-report.md` | Three test rounds and bugs fixed |
| `docs/demo-script.md` | One full business day, step by step |
| `docs/user-guide.md` | For the four roles |
| `docs/install-guide.md` | Build, install, update, uninstall |
| `docs/scope-and-backlog.md` | Version 1.0 delivered and Version 1.1 backlog |
| `docs/how-it-works/` | Questions per module, answered by the developer in their own words |

See `CLAUDE.md` for the project rules.
