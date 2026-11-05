namespace WaterBilling.Data.Entities;

// One trading day, 6 PM to 6 PM (FR-DSP-08). A closed day accepts no new postings (FR-LED-10).
public class BusinessDay
{
    public DateOnly Date { get; set; }
    public BusinessDayStatus Status { get; set; } = BusinessDayStatus.Open;

    public long OpeningCashPaise { get; set; }          // carried from the last close (FR-DAY-01)
    public long? ExpectedClosingCashPaise { get; set; } // Closing Balance (FR-INV-16)
    public long? ActualCashPaise { get; set; }          // counted at closing
    public long? CashDifferencePaise { get; set; }      // Actual - Expected (FR-DAY-04)
    public string? DifferenceReason { get; set; }
    public long? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public int Version { get; set; }
}
