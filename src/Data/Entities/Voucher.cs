namespace WaterBilling.Data.Entities;

// The header of every posting: sale, receipt, payment, journal (opening, cash difference), reversal.
public class Voucher
{
    public long Id { get; set; }
    public VoucherType Type { get; set; }
    public long Number { get; set; }                 // from NumberSeries
    public string DisplayNo { get; set; } = "";      // "INV-7232", "RV-432", "PV-12" (unique)

    public DateOnly VoucherDate { get; set; }        // business day it was entered (Date)
    public DateOnly ValueDate { get; set; }          // business day it belongs to (Value Date)

    public PartyType? PartyType { get; set; }
    public long? SupplierId { get; set; }
    public long? CustomerId { get; set; }
    public long? VehicleId { get; set; }
    public long? PurposeAccountId { get; set; }      // Payment: the ledger paid for
    public PaymentMode? Mode { get; set; }
    public string? ReferenceNo { get; set; }
    public string? PayTo { get; set; }
    public long AmountPaise { get; set; }
    public string? Narration { get; set; }

    public VoucherStatus Status { get; set; } = VoucherStatus.Posted;
    public long? ReversesVoucherId { get; set; }     // set on a reversal voucher
    public string? CancelReason { get; set; }

    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int Version { get; set; }

    public List<LedgerEntry> Lines { get; set; } = [];
}
