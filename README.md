# Water Supply & Fleet Billing System

Billing, ledger and day-close system for Shree Karunambika Traders.
Stack: C# / .NET 8, Blazor Server, EF Core + SQLite, QuestPDF, xUnit.

## Requirements
- .NET 8 SDK (`dotnet --list-sdks` should show 8.0.x)

## Run
```
dotnet run --project src --launch-profile http
```
Open http://localhost:5223

## Test
```
dotnet test
```

## Folders
| Folder | What lives there |
|---|---|
| `src/Data` | Database: EF Core DbContext, entities, migrations |
| `src/Services` | Business rules: invoice, ledger, day close |
| `src/Pages` | Screens (Blazor pages) |
| `src/Components` | Reusable UI parts and layouts |
| `tests` | xUnit tests, named by requirement ID |
| `docs` | Spec, notes, client answers, progress log |

See `CLAUDE.md` for the full project rules.
