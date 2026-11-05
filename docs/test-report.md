# Test report: Version 1.0

**Result: 311 automated tests, 311 passed.** 56 requirement IDs have named tests; see
`requirement-test-matrix.md`. All tests run with `dotnet test` in about 30 seconds.

## How testing is organised
| Layer | What it checks | Where |
|---|---|---|
| Unit | Money, volume, IST time, invoice maths, amount in words | `tests/Common`, `tests/Billing/InvoiceServiceTests` |
| Service | Every business rule against a real SQLite database (in memory, built from the real migrations, so tables, checks and triggers are the real ones) | `tests/Masters`, `tests/Ledger`, `tests/Billing`, `tests/Accounts`, `tests/Reports`, `tests/Admin` |
| HTTP | The whole app in memory: login, redirects, security headers, permissions per role, PDF endpoints | `tests/Web` |
| Browser | Real Chromium driving the screens (Playwright), run after every module | scripts kept outside the repo; results below |

## Round 1: security review
| Check | Result |
|---|---|
| Pages without a permission check | Only Login, Access denied, Not found, Error (intended) |
| Every role × every menu page over HTTP (60 cases) | Each role gets exactly what the role table allows; others go to "Not allowed" |
| PDF endpoints for roles without permission | Refused |
| Raw SQL | One constant `PRAGMA` at startup; all queries are parameterised by EF Core |
| HTML typed into names (`<script>`) | Shown as text, never run (no `MarkupString` anywhere) |
| Bad URL parameters on reports | 400 Bad Request, no crash |
| Login: wrong password, unknown user, inactive user | Same generic message; same timing; logged |
| Open redirect after login (`//evil.com`) | Ignored, goes to `/` |
| Logout without antiforgery token | Refused |
| Ledger and activity log tampering (`UPDATE`/`DELETE`) | Refused by database triggers |
| Two saves at the same moment | Second waits for the first (SQLite `BEGIN IMMEDIATE`), proven by test |
| Known-vulnerable packages | None (`dotnet list package --vulnerable --include-transitive`, NuGet audit on every build) |

## Round 2: one full business day, four users (browser)
Admin creates three users, sets the first invoice number to 7232, adds 2 suppliers (one with a
₹5,000 opening balance), a customer and 2 vehicles. The Dispatch Operator makes a cash invoice and a
10.5 kL credit invoice and takes a cash receipt; the Billing Clerk bills a customer for 2 trips and
takes a UPI receipt; the Accountant pays ₹50 fuel, closes the day with ₹5 short, and runs reports.

| Check | Expected | Seen |
|---|---|---|
| First invoice number | INV-7232 | INV-7232 |
| 10.5 kL × ₹12.35 | ₹129.68 | ₹129.68 |
| Operator receipt modes | Cash only | Cash only |
| Operator opens Payment | Refused | Refused |
| Clerk can change rate | No (read-only) | Read-only |
| Day closing balance | 0 + 150 + 100 − 50 = 200 | 200.00 |
| Trial balance | Debits = credits | 5,579.68 = 5,579.68 |
| Customer statement after paying in full | 0.00 | 0.00 |
| Browser errors (all four users) | None | None |

## Round 3: edge cases
Volume with 2 decimals, amounts with 3 decimals or above the limit, the largest allowed invoice
(no overflow), over-long narration, inactive customer, deleted vehicle, closing a day with no
activity, Tamil names, reversed statement dates, double/triple clicks on Save, and a test that posts
every kind of event and then checks every voucher balances and invoice numbers are gapless.

## Bugs found and fixed during the build
| Module | Bug | Fix |
|---|---|---|
| 2 | Unknown URLs showed a blank page | Friendly 404 page via status-code re-execute |
| 2 | Pages were not interactive, so buttons would have done nothing | `InteractiveServer` for all pages |
| 3 | Times read back from SQLite lost their UTC marker (5½-hour shift risk) | Converter marks UTC; unzoned times refused |
| 4 | User and its log row saved separately (could leave a user with no log) | One transaction |
| 7 | Balance lookup could miss unsaved lines from an earlier posting | Poster saves inside the caller's transaction |
| 10 | Refund party type guessed from the ledger's name | Service returns the party type |
| 12 | A failed day close wiped the counted cash typed by the user | Reload only on success |
| 15 | Backups left `-wal`/`-shm` side files | Backup copy switched to single-file mode |
| 15 | `"Automatic": false` could not switch off the daily backup | Proper boolean parse |
| 16 | A very fast second click could, in theory, save twice | Re-entry guard on every save |

## Known limits (agreed Version 1.1 or awaiting the client)
- Meter dispensing (FR-MTR, FR-DSP, FR-OVR) not built: awaiting client confirmation (CLAUDE.md §3, question 9).
- Login lockout, two-step login, hash-chained audit, Excel export, ageing report, manual receipt allocation,
  day reopening, vehicle transfer, dated rate history: Version 1.1 (CLAUDE.md §3).
- Data encryption at rest (NFR-SEC-01): use Windows BitLocker on the shop PC (see handover).
