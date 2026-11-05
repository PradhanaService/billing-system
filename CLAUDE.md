# CLAUDE.md: Water Supply & Fleet Billing System

> Read this file fully at the start of every session. It is the project's single source of context.

---

## 1. Who this is for and how to help

- The developer is a student/freelancer, slightly familiar with coding but has never built a complete application.
- No prior .NET experience. .NET is used on purpose, so be patient and explicit.
- The finished product will be reviewed by a retired Deloitte IT professional. **The developer must be able to explain every part of the code and the product.**
- Deadline: **1 month from project start**. Scope discipline matters (see section 9).

### How Claude Code must behave

1. **Plan first, then build.** Before writing code for a feature, give a short plan (files to touch, steps) and wait for approval.
2. **One module at a time.** Never build several modules in one go.
3. **Beginner-friendly.** After each feature, explain in simple words what each file and method does and why. Use short examples.
4. **Teach the syntax.** When new C#/.NET syntax appears, show a short skeleton with blanks first when asked, then the full code, then a tip to remember it.
5. **Tests from requirement IDs.** For money, ledger, invoice and day-close rules, write tests that cite the requirement ID (e.g. `FR-LED-01`). Write the tests in the same session as the feature, not later.
6. **Never silently change a business rule.** If code and spec disagree, the spec wins until the client says otherwise. Ask before deviating.
7. **Small steps and small commits.** One feature per Git branch. Suggest a clear commit message.
8. **Layer discipline.** Keep business logic in `/Services`, screens in `/Pages`, reusable UI in `/Components`. Screens never talk to the database directly. This keeps the code easy to explain.
9. **Ask at most one clarifying question at a time**, and only when it blocks progress.
10. **Self-review.** Before a branch is merged, run through the checklist in section 10.
11. **End each session** with: what is done, what is next, and anything that needs a decision.
12. **Be concise. No filler.**

---

## 2. The product in one paragraph

Shree Karunambika Traders sells water at a filling point with three dispensing meters. Commercial tankers, each owned by a supplier (e.g. Sangeetha Water Supply), arrive, fill and leave. Every fill becomes an invoice. Every invoice posts automatically to a double-entry ledger. Receipts and payments are recorded by Cash, Bank or UPI. Supplier, vehicle and customer statements come from the same ledger. At the end of each business day the accountant closes the day, counts cash and locks the day.

### Four principles (never break these)

1. **The ledger is permanent.** Mistakes are corrected with reversal entries. Balances are never edited, and ledger rows are never updated or deleted.
2. **One posting, many views.** Supplier, vehicle and customer statements read the same ledger lines, so they cannot disagree.
3. **Manual changes are controlled and always leave a record** (reason, who, when).
4. **Money is exact.** No floating point for money, ever.

---

## 3. Contracted scope (what was promised to the client)

The client invoice lists exactly this work. This is the scope of Version 1.0:

| Invoice item | Modules to deliver |
|---|---|
| Project planning + UI/UX | Screen designs, simple flow |
| Core modules | Dashboard, Vehicle Master, Sales Invoice |
| Accounts & transaction modules | Receipt, Payment, Ledger, Day Sales |
| Reports + 3 rounds of testing, packing & deployment | Reports, three test rounds, installer/packaging |

**Not in Version 1.0 (list in the handover as "Version 1.1"):** two-step login, account lockout rules, vehicle transfer between suppliers, dated rate history, consolidated invoice and fleet statement, staff advances, manual allocation of receipts by hand, reopening a closed day, 6 PM manual-override window, hash-chained audit log, Excel export (PDF first), ageing report, cloud hosting, PWA, monitoring.

### To confirm with the client (do not assume silently)

- Is live meter dispensing (start/stop fill on Meters 1, 2, 3) in scope? If yes, build the simple version: operator types start and end reading, volume = end - start. The full "meter chain" with locked readings is Version 1.1.
- Open questions from the spec (working assumptions in section 8).

---

## 4. Technology (decided)

| Part | Use |
|---|---|
| Language | C# on .NET 8 |
| UI | ASP.NET Core with Blazor Server (runs in the browser, no JavaScript needed) |
| Database | SQLite via Entity Framework Core |
| PDF | QuestPDF |
| Excel (later) | ClosedXML |
| Tests | xUnit |
| Packing | Self-contained `dotnet publish` + Inno Setup installer |
| Source control | Git + GitHub, one branch per task |

> **The original spec PDFs recommend React, NestJS, PostgreSQL and AWS. Ignore that technology section.** Use the stack above. Everything else in the PDFs (business rules, workflows, fields) still applies.

### SQLite rules (important)

- SQLite has one writer at a time and no row locks. Wrap every multi-step save in **one database transaction** (invoice + ledger lines together; if either fails, neither is kept).
- Store money as **integer paise (`long`)**, never `decimal` as text or `double`. Convert to rupees only for display. Round half-up when computing amounts.
- Store readings/volumes as integer tenths (e.g. 28451.2 kL stored as 284512) or as `decimal` converted safely. Pick one convention and document it in `/docs`.
- Postgres features in the spec (row locks, exclusion constraints) must be replaced by: transactions, unique indexes (SQLite supports partial unique indexes), and checks in the service layer. Say which replacement you chose.
- Store all times in UTC, display in IST. Business dates are plain dates.
- Assumption (unconfirmed): the shop uses **one computer**. If several PCs must use it together, stop and raise it before designing further.
- Backups: provide a simple "Backup now" that copies the SQLite file.

---

## 5. Folder structure

```
/src
  /Data         EF Core DbContext, entities, migrations
  /Services     business logic: invoice, ledger posting, day close
  /Pages        Blazor pages
  /Components   reusable UI parts, PDF layouts
/tests          xUnit tests, named by requirement ID
/docs           spec PDFs, notes, user guide, handover pack
CLAUDE.md
README.md
```

Put the three original spec PDFs (Project Overview, Requirements, Database Schema) in `/docs/spec/`. They remain the reference for detailed field lists and rules.

---

## 6. Business rules (essentials for Version 1.0)

### 6.1 Roles

| Role | Main job |
|---|---|
| Admin | Everything, **plus** Users & Roles, Activity Log and Backup (admin-only) |
| Dispatch Operator | All business work (same as below) |
| Billing Clerk | All business work: suppliers, customers, vehicles, invoices, ledgers, receipts, payments, day close, statements, reports |
| Accountant | All business work (same as above) |

> **Client decision (Nov 2026), replaces the earlier per-role split:** only Admin can see or open Backup,
> Activity Log and Users & Roles. For other roles those pages are not in the menu and their addresses
> answer "Page not found" (no gateway). Every other role can do all business actions like Admin.
> Roles are still recorded on every activity-log entry. Code: `RolePermissions` (Services/Security/Permission.cs).

- Permissions are enforced on the **server side** for every action. Hiding a button is not security.
- Users are deactivated, never deleted.

### 6.2 Masters

- **Supplier:** name (unique, case-insensitive), short name, contact person, phone (required), alt phone, address, opening balance (Dr/Cr), status Active/Inactive.
- **Vehicle:** system code (one letter + number, e.g. `A13`, proposed by the system, never reused), vehicle number (uppercase, unique; short forms like `TN 67 C` allowed), nickname (defaults to supplier short name + plate), supplier, tank capacity (litres), billing basis (`TRIP` or `VOLUME`), trip cost, rate, status Active/Inactive/Maintenance, driver name/phone/licence.
- One supplier has many vehicles; a vehicle has one supplier.
- Inactive vehicles cannot start a fill or be invoiced. Inactive supplier makes all its vehicles unavailable.
- Delete is allowed only for a record with no history; otherwise offer Inactive.
- Search by vehicle number, code, nickname or supplier. Filter Active/Inactive/All.
- Customers and staff are separate party types, each with their own ledger.
- Saving a party with an opening balance posts one opening entry, once.

### 6.3 Sales invoice

- Invoice number assigned at save, from one gapless series. The screen shows the expected next number until then.
- Choosing vehicle code or name fills the other plus supplier and rate.
- Trip defaults to 1. Amount = Trip x Rate for TRIP vehicles, Volume x Rate for VOLUME vehicles, rounded half-up to 2 decimals (paise).
- Changing the defaulted rate needs permission and is logged.
- Payment mode: Cash or Credit.
- Bill To defaults to the vehicle's supplier and can be changed to a customer; the ledger posts to Bill To, tagged with the vehicle.
- Item details show: S.No, Material, Qty, Measurement, Rate, Total Amount.
- Save = invoice + ledger postings in one transaction.
- Editing a posted invoice is not allowed in place. It is Cancel + new invoice.
- Cancel needs a reason, reverses the postings, keeps the number and shows status Cancelled.
- Previous/Next move through invoices, View is read-only, Print outputs PDF.
- No GST/tax on invoices (assumption, subject to auditor).

### 6.4 Ledger and posting rules

- Every event posts balanced lines: total debit = total credit.
- Ledger lines are append-only. Each line stores: posting date, value date (business date), particulars, voucher/ref no, debit, credit, running balance (stored at posting time, shown with Dr/Cr).
- Reversal lines swap debit and credit, cite the original voucher, and post on the date of correction with the original value date.
- Posting to a closed business date is not possible; corrections post to the open day and quote the original value date.
- Vehicle ledger is a view of lines tagged with the vehicle, not a separate account.
- Sum of vehicle balances + unallocated = supplier balance, always.
- Statements show: opening balance b/f, entries, period debit/credit totals, closing balance c/f.

| Code | Event | Debit | Credit |
|---|---|---|---|
| P1 | Credit sale | Party | Water Sales |
| P2 | Cash sale | Party, then Cash in Hand | Water Sales, then Party |
| P3 | Receipt | Cash, Bank or UPI | Party |
| P4 | Operational expense payment | Expense ledger | Cash, Bank or UPI |
| P5 | Payout or refund to party | Party | Cash, Bank or UPI |
| P8 | Cash short at closing | Cash Over/Short | Cash in Hand |
| P9 | Cash over at closing | Cash in Hand | Cash Over/Short |
| P10 | Opening balance | Party or account | Opening Balance Equity (reversed if credit) |
| P11 | Any cancellation | Original credits | Original debits |

P2 posts through the party on purpose, so cash trips appear on the supplier statement and net to zero.

### 6.5 Receipts and payments

- **Receipt voucher:** receipt no, date, party/supplier (required), mode (Cash/Bank/UPI), amount (required), reference no, narration.
- **Payment voucher:** voucher no, date, ledger (required), pay to, vehicle (optional), mode, amount (required), reference no, narration.
- Reference no is required for Bank and UPI. Amount must be greater than zero.
- Receipts and payments have separate gapless series, displayed in ledgers as `RV-432` and `PV-432`.
- Saving posts immediately. Show the party/ledger balance now and after this voucher, updating as the user types.
- A cash payment above available cash needs a warning and confirmation.
- Receipt allocation to open invoices: oldest first (automatic) in Version 1.0. Remainder stays Unallocated.
- Editing or deleting a voucher is Cancel + new voucher, with reversal.
- **Ledger Master:** create expense ledgers (Vehicle Maintenance, Fuel Expense, Oil Change, Others). System ledgers cannot be changed. Ledgers with entries cannot be deleted.

### 6.6 Day operations

- Business day runs 6:00 PM to 6:00 PM. Before 18:00 a session belongs to the same date; from 18:00 it belongs to the next date. A session that crosses 18:00 is never split.
- Day Opening shows opening cash carried from the last close.
- View Day Sales shows Day Opening, Daily Transactions, Day Closing with Opening Balance, Closing Balance, Actual Cash Balance, Cash Difference.
- Day Closing is blocked while any fill is open, or a completed fill has no invoice (and is not marked Bill Later). List each blocker with a link to fix it.
- Cash Difference = Actual Cash - Closing Balance. A non-zero difference needs a reason and posts P8 or P9.
- Closing locks the business date and carries actual cash to the next opening.
- Closing Balance (Invoice Display) = Opening + Cash Sales + cash Receipts - cash Payments.

### 6.7 Dashboard

- Cards: Total Sales (This Month), Today's Sales, Today's Receipt, Today's Payment.
- Second row: Cash Sales, Credit Sales, Total Water Trips, Outstanding Amount, Opening and Closing Cash.
- Monthly sales chart for the last 6 months.
- Quick access to Sales Invoice, Invoice Display, Vehicle Master, Reports.
- Recent invoices (last 3).
- Month selector changes month figures; "today" figures stay on today.

### 6.8 Reports (PDF first)

Daily Sales, Date-wise Sales, Supplier-wise Sales, Vehicle operational report (vehicle, registration, total volume, unit rate, total bill, received, outstanding, with supplier subtotal and grand total), Supplier-wise Outstanding, Customer Outstanding, Trial Balance, Cash Book. All filter by date range, supplier and vehicle. **Report totals must equal ledger totals for the same filter.**

### 6.9 Simple activity log (Version 1.0)

Record time, user, role, action, record, before/after values (where relevant), reason. No edit or delete for any role. Only Admin can view it.

### 6.10 Formatting

Indian number format (`4,94,784.00`), dates `DD/MM/YYYY`, 12-hour time.

### 6.11 Meter dispensing (only if confirmed in scope)

Three meters (Meter 1, 2, 3). Simple version: a fill records vehicle, supplier, In Time, Out Time (server clock), start reading, end reading, volume = end - start. One open fill per vehicle. A meter can serve only one vehicle at a time. Warn if volume exceeds tank capacity by more than 5% and require a reason. Full chain (locked start reading, variance, rollover, fault state) is Version 1.1.

---

## 7. Working method

Claude Code acts as planner, pair programmer and reviewer.

- **Build order inside each module:** data (entities) -> service (logic) -> tests -> screen. Finish and test the logic before building the screen.
- **Interface first:** before building a screen, Claude lists the service method names and signatures it will call (e.g. `CreateInvoice(...)`, `GetLedger(...)`) and waits for approval. This keeps screens and logic cleanly separated.
- **One branch per task.** Merge to `main` only when the checklist in section 10 is done.
- **Daily (10 minutes):** write down done / doing / blocked in `/docs/progress.md`. Pick one small task for the day and plan it with Claude Code.
- **Friday explain-back:** explain that week's code out loud (or in writing) to Claude as if teaching the reviewer. Claude asks 3 questions about it and points out where the explanation is weak.
- **After each feature** add a short "how this module works" note in `/docs`, in the developer's own words (Claude may correct it, not write it).
- **Stuck rule:** if blocked for more than 45 minutes, say so and ask Claude for a simpler approach or a smaller step.

---

## 8. Open questions and working assumptions

| # | Question | Working assumption |
|---|---|---|
| 1 | What does the Trip Cost of 2,500 in Vehicle Master mean (invoices charge 50 to 150)? | Separate value with no ledger effect. Invoices use a new Rate (per trip) field. **Needed before database work.** |
| 2 | Do meters send readings electronically, or does staff read the dial? | Staff read the dial. **Needed before database work.** |
| 3 | Meter reading unit? | Kilolitres, one decimal, set per meter. |
| 4 | Do suppliers buy water from the business? | Yes, suppliers normally owe the business. |
| 5 | Does the trading day run 6 PM to 6 PM? | Yes. |
| 6 | Does GST apply? | No tax on invoices (auditor to confirm). |
| 7 | How are customers like Meena Constructions billed? | Bill To field defaults to the vehicle's supplier and can be changed to a customer. |
| 8 | One computer or several at once? | One computer (unconfirmed). |
| 9 | Is live meter dispensing in scope? | Simple version if yes (unconfirmed). |

Send these questions to the client on day 1 and record the answers in `/docs/client-answers.md`.

---

## 9. Four-week schedule

The plan below is sequential, with tests and notes written alongside the code.

| Week | Build | Done when |
|---|---|---|
| 1 | Send client questions. Create solution, SQLite database, EF Core entities, login and roles. Supplier and vehicle tables. Layout, menu, login screen, Vehicle Master screen. Tests for login and vehicles. | Admin logs in and adds a vehicle |
| 2 | Invoice logic (amount, numbering, cash or credit) + tests. Sales Invoice screen, invoice PDF, dashboard cards. | An invoice saves and prints as PDF |
| 3 | Ledger posting (invoice, receipt, payment, cancellation) + balance tests. Receipt, Payment and ledger statement screens. | Debits always equal credits |
| 4 | Day Sales logic and screen, backup, reports, installer. 3 test rounds, user guide, demo script, handover pack. | Reviewer installs and runs a full day |

### Priority if time runs short (cut from the bottom first)

1. **Must have:** Login/roles, Vehicle Master, Sales Invoice, Ledger posting, Receipt, Payment, Day close, backup, installer.
2. **Should have:** Dashboard cards, Daily Sales, Supplier-wise Outstanding, Cash Book, Trial Balance.
3. **Cut first:** Dashboard 6-month chart, remaining reports, meter dispensing (simple version), nice-to-have polish.

Anything cut is listed honestly in the handover as moved to Version 1.1, and agreed with the client early rather than at the deadline.

---

## 10. Definition of done (per feature)

Before merging a branch, tick every item:

- [ ] Works on the developer's laptop and on a second computer (or a clean install) at least once per week.
- [ ] Tests written and passing (named by requirement ID for money, ledger and day-close rules).
- [ ] Self-review: read the full diff, remove debug code, check no business rule changed silently, check money is `long` paise, check the server enforces permissions.
- [ ] Claude Code has reviewed the diff and each comment is fixed or accepted.
- [ ] Short "how it works" note added in `/docs`.
- [ ] The developer can explain it without looking at the code.

---

## 11. Handover pack for the reviewer

- `README.md` with setup and run steps.
- Architecture note and database diagram.
- Requirement-to-test table (requirement ID -> test -> pass/fail).
- Test report for all 3 rounds.
- Demo script for one full day: add vehicle -> invoice -> receipt -> payment -> day close -> statement and reports.
- User guide for the four roles.
- Written note: Version 1.0 scope delivered and Version 1.1 backlog (the list in section 3).
- A "how it works" document written by the developer, in their own words.

---

## 12. First session checklist for Claude Code

1. Read this file and the PDFs in `/docs/spec/`.
2. Summarise your understanding in 10 lines and list anything unclear.
3. Propose the solution structure for .NET 8 + Blazor Server + EF Core + SQLite matching section 5.
4. Wait for confirmation, then start Week 1 work with a plan first.
