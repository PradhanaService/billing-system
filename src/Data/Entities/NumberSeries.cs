namespace WaterBilling.Data.Entities;

// Gapless numbering (FR-INV-01, FR-TXN-04). The row is read and moved on inside the same
// transaction as the save, so a failed save gives the number back and no number is skipped.
public class NumberSeries
{
    public string Code { get; set; } = "";     // INVOICE, RECEIPT, PAYMENT, JOURNAL, REVERSAL
    public string Prefix { get; set; } = "";   // "INV-", "RV-" ...
    public long NextNo { get; set; } = 1;
}
