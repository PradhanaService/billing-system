# Scope: Version 1.0 delivered, Version 1.1 backlog

## Delivered in Version 1.0 (matches the client invoice)
| Invoice item | Delivered |
|---|---|
| Project planning + UI/UX | Screen designs (Figma), black-and-white layout, simple flow |
| Core modules | Dashboard, Supplier/Customer/Vehicle Masters, Sales Invoice with PDF |
| Accounts & transactions | Receipt (with oldest-first allocation), Payment, Ledger Master, double-entry ledger, statements, Day Sales and Day Closing, Invoice Display |
| Reports, testing, packing | 8 reports with PDF; 3 test rounds (311 automated tests); Windows installer |
| Also | Login with 4 roles, activity log, backup, starting invoice number |

## Not built in Version 1.0
| Item | Why | Status |
|---|---|---|
| Meter dispensing (Meters 1–3, fills, meter chain, overrides) | Not confirmed by the client (question 9) | Build the simple version if the client confirms |
| Two-step login, account lockout | CLAUDE.md §3 | Version 1.1 |
| Vehicle transfer between suppliers | CLAUDE.md §3 | Version 1.1 |
| Dated rate history | CLAUDE.md §3 (rate is one value per vehicle) | Version 1.1 |
| Consolidated invoice, fleet statement | CLAUDE.md §3 | Version 1.1 |
| Staff advances, Staff Outstanding report | CLAUDE.md §3 | Version 1.1 |
| Manual allocation of receipts | CLAUDE.md §3 (oldest-first is automatic) | Version 1.1 |
| Reopening a closed day | CLAUDE.md §3 | Version 1.1 |
| 6 PM manual-override window | Part of meter dispensing | Version 1.1 |
| Hash-chained audit log | CLAUDE.md §3 (log is append-only by triggers) | Version 1.1 |
| Excel export | CLAUDE.md §3 (PDF first) | Version 1.1 |
| Ageing report | CLAUDE.md §3 | Version 1.1 |
| Cloud hosting, PWA, monitoring | CLAUDE.md §3 | Version 1.1 |
| Encryption at rest inside the app | Use BitLocker on the shop PC (install guide) | Decision for client |

## Working assumptions still to confirm with the client
See `client-questions.md`. Built on: Trip Cost has no ledger effect (Q1); staff read the dial (Q2);
6 PM to 6 PM day (Q5); no GST (Q6); Bill To can be a customer (Q7); one computer (Q8).
If Q8 changes to "several computers", the localhost-only setting and SQLite need review first.
