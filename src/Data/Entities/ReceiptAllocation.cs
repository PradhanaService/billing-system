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
}
