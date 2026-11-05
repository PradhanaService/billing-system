# Data conventions

Rules every table and every module follows. The code that applies them is named in each row.

| Kind of value | Stored as | Example | Code |
|---|---|---|---|
| Money | `long`, whole **paise** | ₹1,250.50 → `125050` | `Services/Common/Money.cs` |
| Meter reading / volume | `long`, whole **tenths of a kL** | 28451.2 kL → `284512` | `Services/Common/Volume.cs` |
| Moment in time | `DateTime` in **UTC** (column ends in `Utc`) | 05/11/2026 06:15 PM IST → `2026-11-05 12:45 UTC` | `Services/Common/IndianTime.cs` |
| Business date | `DateOnly` (no time) | `2026-11-05` | (none) |
| Status and type fields | enum stored as its **name** | `"Active"` | `Data/Entities/Enums.cs` |

## Money
- Typed amounts with more than 2 decimals are **refused**, not rounded (`Money.FromRupees`).
- Calculated amounts (e.g. volume × rate) are rounded **half-up** to the paisa (`Money.RoundHalfUp`, FR-INV-03). Half a paisa goes up; for minus amounts it goes away from zero.
- `double` and `float` are never used for money.
- Shown in Indian format: `4,94,784.00` (`Money.Format`).

## Time
- The app reads the clock through `TimeProvider`, never `DateTime.Now`, so tests can fix the time.
- Saving a `DateTime` without a time zone is refused (it could be off by 5½ hours).
- Shown as IST (UTC + 5:30, no daylight saving): `dd/MM/yyyy hh:mm tt`.

## Text lengths
SQLite stores every string as `TEXT` and does **not** enforce `HasMaxLength`. The real limit is the
`[StringLength]` attribute on each form, checked by the service on every save (`FormValidator`).

## Audit columns
Every master table has `CreatedAtUtc`, `UpdatedAtUtc` and `Version`, filled automatically in
`AppDbContext.SaveChanges`. `CreatedBy` / `UpdatedBy` are added in Session 6 (login).

## Replacements for PostgreSQL features in the spec
The spec targets PostgreSQL. SQLite does not have every feature, so:

| Spec feature | SQLite replacement used |
|---|---|
| `numeric(14,2)` money | `long` paise |
| `numeric(12,1)` readings | `long` tenths |
| Unique on `lower(name)` | Extra `NormalizedName` column (spaces tidied, upper case) with a unique index. Handles all languages, not just A to Z. |
| `version` column for edit conflicts | EF Core concurrency token on `Version`. The second editor gets a "changed by someone else" error (NFR-CON-05). |
| `timestamptz` | UTC `DateTime` plus a converter that marks values as UTC on read |
| Check constraints | Same: SQLite supports `CHECK` |
| Row locks for numbering and balances | One transaction per save; SQLite `BEGIN IMMEDIATE` serialises writers (see `ledger-design.md`). |
| "No update/delete" database rights on ledger and audit | `BEFORE UPDATE` / `BEFORE DELETE` triggers that abort. **If a migration ever rebuilds these tables, re-create the triggers** (tests check them). |

## The database file
- Path set in `appsettings.json` → `Database:Path` (default `App_Data/waterbilling.db`, git-ignored).
- Created and upgraded automatically at startup (`Database.Migrate()`).
- WAL mode is on. The file has two companions, `-wal` and `-shm`. **Backups must use SQLite's backup command, not a plain file copy** (Session 16).

## Adding or changing a table
```
dotnet tool restore
dotnet ef migrations add <Name> --project src --output-dir Data/Migrations
dotnet ef migrations has-pending-model-changes --project src   # should say "No changes"
```
