namespace WaterBilling.Data.Entities;

// How much of a receipt paid which invoice (Database Schema: VoucherAllocations).
// Made inactive (never deleted) when the receipt is cancelled.
public class ReceiptAllocation
{
    public long Id { get; set; }
    public long VoucherId { get; set; }      // the receipt voucher
    public long InvoiceId { get; set; }
    public long VehicleId { get; set; }
    public long AmountPaise { get; set; }
    public bool IsActive { get; set; } = true;
    // True when a later invoice was paid from this receipt's advance (money kept on account),
    // false when the invoice was ticked on the receipt itself.
    public bool FromAdvance { get; set; }
}
