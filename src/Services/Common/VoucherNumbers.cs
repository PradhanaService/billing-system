namespace WaterBilling.Services.Common;

// Receipt and payment numbers are shown as plain numbers (final design: "4", not "RV-4").
// They are still stored with their prefix, which keeps every voucher number unique in the database.
public static class VoucherNumbers
{
    public static string Plain(string? displayNo)
    {
        if (string.IsNullOrEmpty(displayNo)) return "";
        var dash = displayNo.IndexOf('-');
        return dash > 0 && displayNo[..dash].All(char.IsLetter) ? displayNo[(dash + 1)..] : displayNo;
    }
}
