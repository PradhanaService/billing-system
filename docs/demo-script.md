# Demo script: one full business day

About 20 minutes. Start from a fresh install (or delete `App_Data` when running from source).
Expected figures are given so the reviewer can check each step. This same flow is tested
automatically in round 2 (`test-report.md`).

## 1. Admin: set up (5 min)
1. Open the app, sign in as `admin` / `Admin@123`. You are made to choose a new password.
2. **Users & Roles**: add `op1` (Dispatch Operator), `clerk1` (Billing Clerk), `acct1` (Accountant), each with temporary password `Temp1234`.
3. **Ledger Master → Starting numbers**: set INVOICE to `7232`.
4. **Supplier Master**: add *Sangeetha Water Supply* (short name *Sangeetha*, phone 9876543210, opening balance ₹5,000 Dr) and *Kaveri Tankers* (short name *Kaveri*, phone 9123456780).
   Show: typing *sangeetha water supply* again is refused as a duplicate.
5. **Customer Master**: add *Meena Constructions* (9000000001).
6. **Vehicle Master**: add *TN 38 M 3* (Sangeetha, per trip, ₹150) → code **A1**; add *TN 67 C* (Kaveri, per kL, ₹12.35) → code **A2**.
7. Sign out.

## 2. Dispatch Operator (4 min)
1. Sign in as `op1`, set a new password. Note the menu: all business screens, but no Users & Roles, Activity Log or Backup.
2. **Sales Invoice**: expected number shows **INV-7232**. Choose vehicle code *A1* (name fills in), Payment Mode *Cash*, save → ₹150.00.
3. Choose vehicle name *Kaveri TN 67 C*, volume *10.5*, Credit, save → **INV-7233 for ₹129.68** (10.5 × 12.35 = 129.675, rounded half-up).
4. **Transactions → Receipt**: Supplier *Kaveri Tankers*, Cash, ₹100. Balance now ₹129.68 Dr, after ₹29.68 Dr; it will pay INV-7233. Save → RV-1.
5. Type `/admin/backup` in the address bar → *Page not found* (admin areas do not exist for other roles).

## 3. Billing Clerk (3 min)
1. Sign in as `clerk1`. **Sales Invoice**: vehicle *A1*, Bill To *A customer* → *Meena Constructions*, Trip 2 → **INV-7234 ₹300.00**.
2. **Transactions → Receipt**: party type *Customer*, *Meena Constructions*, UPI, ₹300 — save without reference → refused; add reference *UTR778899* → RV-2.

## 4. Corrections (2 min, as Admin, before the day is closed)
1. Open **Invoice Display**, click INV-7234. Try to cancel → refused: *receipts are allocated (RV-2)*.
2. **Transactions → Receipt → Previous** until RV-2 shows → **Delete** with reason *wrong party*. Then cancel INV-7234 with reason *billed in error*.
   Its number stays, status *Cancelled*, reversals REV-1 and REV-2 are posted.
3. **Ledger Statement → Customer → Meena**: four lines (invoice, receipt, two reversals), closing **0.00**.

## 5. Accountant (6 min)
1. Sign in as `acct1`. **Transactions → Payment**: Fuel Expense, Pay To *Indian Oil*, vehicle *A1*, Cash, ₹50 → PV-1.
2. **Day Sales**: Opening 0 + Cash Sales 150 + Cash Receipts 100 − Cash Payments 50 = **Closing Balance 200.00**.
   (The cancelled UPI receipt and credit invoice do not touch cash.)
3. Enter Actual Cash **195**. Difference −5.00. Close without a reason → refused. Reason *₹5 change short* → closed.
   (Posts P8: Dr Cash Over/Short, Cr Cash.) Until 6 PM, any new entry is now refused: the day is locked.
4. **Reports → Trial Balance**: totals **5,279.68 = 5,279.68**
   (Dr: Cash 195, Suppliers 5,029.68, Fuel 50, Cash short 5. Cr: Water Sales 279.68, Opening Balance Equity 5,000).
5. **Reports → Supplier-wise Outstanding**: Kaveri 29.68 Dr, Sangeetha 5,000.00 Dr. Print PDF.
6. **Ledger Statement → Ledger → Cash in Hand** (Cash Book): closing 195.00.

## 6. Admin wrap-up (1 min)
1. **Activity Log**: every step above is listed with who, when, before/after and reason.
2. **Backup → Backup now**: a checked single-file copy appears in the list.
