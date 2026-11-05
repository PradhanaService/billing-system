namespace WaterBilling.Data.Entities;

// One ledger line. Append-only (FR-LED-02): the database refuses UPDATE and DELETE.
// Exactly one of Debit or Credit is above zero.
public class LedgerEntry
{
    public long Id { get; set; }
    public long PostingSeq { get; set; }             // rises with every line: the true order of posting
    public long VoucherId { get; set; }
    public int LineNo { get; set; }

    public long AccountId { get; set; }
    public long? SupplierId { get; set; }            // party tag (at most one)
    public long? CustomerId { get; set; }
    public long? VehicleId { get; set; }             // vehicle tag: makes the vehicle statement (FR-LED-07)

    public DateOnly EntryDate { get; set; }          // "Date": business day when posted
    public DateOnly ValueDate { get; set; }          // "Value Date": business day it belongs to
    public string Particulars { get; set; } = "";
    public string ReferenceNo { get; set; } = "";    // voucher display number

    public long DebitPaise { get; set; }
    public long CreditPaise { get; set; }

    // Balance of this account + party after this line. Dr is positive, Cr negative (FR-LED-04).
    public long RunningBalancePaise { get; set; }
    // Balance of this account + party + vehicle after this line; null when no vehicle tag.
    public long? VehicleRunningBalancePaise { get; set; }

    public long? ReversesEntryId { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
