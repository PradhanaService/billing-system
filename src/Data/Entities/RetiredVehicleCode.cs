namespace WaterBilling.Data.Entities;

// A vehicle code (e.g. A5) whose vehicle was removed for good after 30 days in Recovery.
// Kept so the code is never given to another vehicle (codes are never reused).
public class RetiredVehicleCode
{
    public string Code { get; set; } = "";
    public DateTime RetiredAtUtc { get; set; }
}
