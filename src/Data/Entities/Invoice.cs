namespace WaterBilling.Data.Entities;

// A trip invoice (Database Schema page 8). One vehicle visit, one item line.
// Its money side is the Sales voucher it points to; the two are saved together (FR-INV-07).
public class Invoice
{
    public long Id { get; set; }
    public long InvoiceNo { get; set; }              // gapless (FR-INV-01) = Sales voucher number
    public string DisplayNo { get; set; } = "";      // "INV-7232"
    public DateOnly InvoiceDate { get; set; }        // business date

    public long VehicleId { get; set; }
    public long SupplierId { get; set; }             // the vehicle's supplier at the time of sale
    public PartyType BillToType { get; set; }        // FR-INV-08
    public long? CustomerId { get; set; }            // set when billed to a customer

    public InvoicePaymentMode PaymentMode { get; set; }

    // Item details (FR-INV-06). One line per trip invoice.
    public string Material { get; set; } = "Water";
    public int Trips { get; set; } = 1;
    public long VolumeTenths { get; set; }           // kL in tenths; required for Volume vehicles
    public BillingBasis BillingBasis { get; set; }
    public long RatePaise { get; set; }              // copied at save, never re-derived
    public RateSource RateSource { get; set; }
    public long AmountPaise { get; set; }

    public long AllocatedPaise { get; set; }         // paid so far (cash sale: all of it)
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Posted;
    public string? Narration { get; set; }

    public long VoucherId { get; set; }
    public long? ReversalVoucherId { get; set; }
    public long? CancelledByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }

    // The sales person chosen in "Created by" (null on invoices made before the list existed).
    public long? SalesPersonId { get; set; }

    public long CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int Version { get; set; }

    public long OutstandingPaise => Status == InvoiceStatus.Cancelled ? 0 : AmountPaise - AllocatedPaise;
    public string Measurement => BillingBasis == BillingBasis.Trip ? "Trip" : "kL";
}
