using WaterBilling.Data.Entities;

namespace WaterBilling.Services.Billing;

// What an invoice shows in its Status column (Dashboard, Invoice Display): one rule, used everywhere.
// Cash invoices are paid at billing; credit invoices are paid when receipts cover the whole amount.
public enum PayState { Paid, PartPaid, NotPaid, Cancelled }

public static class PayStates
{
    public static PayState For(InvoiceStatus status, InvoicePaymentMode mode, long amountPaise, long allocatedPaise) =>
        status == InvoiceStatus.Cancelled ? PayState.Cancelled
        : mode == InvoicePaymentMode.Cash || allocatedPaise >= amountPaise ? PayState.Paid
        : allocatedPaise > 0 ? PayState.PartPaid
        : PayState.NotPaid;

    public static PayState For(InvoiceRow r) => For(r.Status, r.PaymentMode, r.AmountPaise, r.AllocatedPaise);

    public static string Text(PayState s) => s switch
    {
        PayState.Paid => "Paid",
        PayState.PartPaid => "Part paid",
        PayState.NotPaid => "Not paid",
        _ => "Cancelled",
    };
}
