# Ledger design

## Tables
| Table | Holds |
|---|---|
| `Accounts` | Ledgers. 8 system ledgers (fixed Ids, see `SystemAccounts`) + expense ledgers. |
| `Vouchers` | Header of each posting: type, number, dates, party, amount, status. |
| `LedgerEntries` | The lines. **Append-only**: SQLite triggers refuse UPDATE and DELETE. |
| `NumberSeries` | Next number per series: INVOICE (INV-), RECEIPT (RV-), PAYMENT (PV-), JOURNAL (JV-), REVERSAL (REV-). |
| `BusinessDays` | One row per trading day (6 PM to 6 PM), Open or Closed, with the cash figures. |

## How a party balance works
There are two control ledgers: *Trade Receivables - Suppliers* and *Trade Receivables - Customers*.
A party's lines sit in its control ledger **tagged with the party Id**. Its balance is the running
balance of its latest line. A line can also carry a **vehicle tag**; the vehicle statement is just
the tagged lines (FR-LED-07), so nothing is posted twice.

## Posting rules (CLAUDE.md 6.4)
| Code | Event | Debit | Credit | Where in code |
|---|---|---|---|---|
| P1 | Credit sale | Party (+vehicle) | Water Sales | `InvoiceService` (Module 8) |
| P2 | Cash sale | Party, Cash in Hand | Water Sales, Party | `InvoiceService` |
| P3 | Receipt | Cash/Bank/UPI | Party | `ReceiptService` (Module 9) |
| P4 | Expense payment | Expense ledger | Cash/Bank/UPI | `PaymentService` (Module 10) |
| P5 | Refund to party | Party | Cash/Bank/UPI | `PaymentService` |
| P8/P9 | Cash short/over at close | Cash Over/Short ↔ Cash in Hand | | `DayCloseService` (Module 12) |
| P10 | Opening balance | Party | Opening Balance Equity (swapped if Cr) | `OpeningBalances` |
| P11 | Cancellation | original credits | original debits | `LedgerPoster.ReverseAsync` |

## What `LedgerPoster` guarantees
1. Debits = credits, and each line has exactly one side above zero (FR-LED-01).
2. Only an open business day accepts postings (FR-LED-10).
3. Gapless numbers: the series row moves inside the save's transaction; a rollback gives the number back.
4. `PostingSeq` rises by one per line; running balances are stored at posting (FR-LED-04).
5. A reversal is entered today with the **original value date**, swaps every line, links each line to the
   one it reverses, and marks the original Cancelled. A voucher can be reversed once (unique index).
6. It refuses to run outside a transaction.

## Why two saves cannot clash
SQLite allows one writer at a time. EF Core starts every transaction with `BEGIN IMMEDIATE`, which takes
the write lock at once; a second save waits (up to 30 s) until the first commits. Proven by
`ConcurrencyTests`. This replaces the PostgreSQL row locks in the spec.

## Business day
`BusinessCalendar.DateFor(utc)`: IST time before 18:00 → that date; from 18:00 → next date.
A day's row is created on its first posting, with opening cash = actual cash counted at the last close.
