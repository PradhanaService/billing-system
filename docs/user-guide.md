# User guide

Open **Water Billing** from the desktop (it opens `http://localhost:5080`). Sign in with your user name and
password. The first time, you must choose a new password (at least 8 characters, with a letter and a number).
You are signed out after 30 minutes without activity. The menu only shows what your role may use.

The business day runs **6:00 PM to 6:00 PM**. Anything entered from 6 PM belongs to the next date.

## Who can do what
Only Admin sees **Users & Roles**, **Activity Log** and **Backup**. For everyone else those items are not in the
menu and their addresses show "Page not found". Dispatch Operators, Billing Clerks and Accountants can do all
the business work below, exactly like Admin.

| | Admin | Dispatch Operator, Billing Clerk, Accountant |
|---|---|---|
| Dashboard, Supplier, Customer, Vehicle and Ledger Masters | Yes | Yes |
| Sales Invoice, Invoice Display, cancel invoices, change a rate (with reason) | Yes | Yes |
| Receipt and Payment (Cash, Bank, UPI), cancel them | Yes | Yes |
| Ledger Statement, Day Sales and day closing, Reports | Yes | Yes |
| Users & Roles, Activity Log, Backup | Yes | No (not shown anywhere) |

## Dashboard and masters
- **Dashboard**: this month's sales, today's sales/receipts/payments, outstanding, cash, a 6-month chart
  (click a bar to see that month's invoices) and the last 3 invoices. Change the month at the top right.
- **Supplier / Customer / Vehicle Master**: search, add, edit, set Inactive, delete a record with no history.
  A vehicle code (A1, A2 ...) is proposed and never reused; you may type another unused code.
- **Change password**: link at the top right.

![Dashboard](images/dashboard.png)

## Sales Invoice
1. Choose the vehicle by code (A1) or by name; the supplier and rate fill in.
2. Trip is 1 unless more; for vehicles billed per kL, type the volume (one decimal).
3. Payment Mode: *Cash* if paid now, *Credit* otherwise. To bill a customer, set *Bill To* to *A customer*.
4. Changing the rate needs a reason and is logged. Check the total in Item Details, then **Save invoice**.
5. Use **Print** for the PDF. The number is given at save and never skipped.
6. **Cancel an invoice**: open it (Invoice Display, then the number), *Cancel invoice* with a reason. Invoices are
   never edited: cancel and make a new one. If a receipt already paid it, cancel the receipt first.

![Sales invoice](images/sales-invoice.png)

## Receipt and Payment
- **Receipt**: choose the supplier or customer, mode and amount, **Save receipt**. The screen shows which
  invoices it pays (oldest first) and the balance before and after. Bank and UPI need a reference number.
- **Payment**: choose the ledger (e.g. Fuel Expense), who is paid, optional vehicle, mode, amount.
  For a refund choose *Refund / payout to supplier* (or customer). Paying more cash than is in hand needs a tick to confirm.
- **Cancel a receipt or payment**: in its history list, *Cancel* with a reason. A reversal is posted.

## Ledgers, day close and reports
- **Ledger Master**: add expense ledgers; system ledgers cannot be changed. Admin can also set the first
  invoice number here before the first invoice.
- **Day Sales, close the day**: count the cash, type it in *Actual Cash Balance*. If it differs from the
  Closing Balance, give a reason (the difference is posted to Cash Over/Short). Days close in order.
  After closing, nothing more can be entered for that date; corrections go to the next day.
- **Ledger Statement**: supplier, customer, vehicle or ledger, any period, with PDF.
- **Reports**: 8 reports, filter by dates, supplier, vehicle; *Print PDF*.

![Day sales](images/day-sales.png)

## Admin only
- **Users & Roles**: add users (temporary password), change role, reset password, deactivate (never delete).
  You cannot deactivate yourself or remove the last Admin.
- **Activity Log**: who did what and when, with before/after values. Nobody can change it.
- **Backup, Backup now** before any big change. One backup is also taken automatically each day.

## Good habits
- Close every day. Copy `C:\ProgramData\WaterBilling\Backups` to a USB drive weekly.
- Never share passwords; each person has their own user so the log is meaningful.
