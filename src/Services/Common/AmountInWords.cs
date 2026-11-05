namespace WaterBilling.Services.Common;

// 1,25,050.50 -> "Rupees One Lakh Twenty Five Thousand Fifty and Fifty Paise Only".
// Indian grouping: crore (10^7), lakh (10^5), thousand, hundred.
public static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen",
    ];
    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Format(long paise)
    {
        if (paise < 0) return "Minus " + Format(-paise);
        var rupees = paise / 100;
        var p = paise % 100;
        var text = "Rupees " + Words(rupees);
        if (p > 0) text += " and " + Words(p) + " Paise";
        return text + " Only";
    }

    private static string Words(long n)
    {
        if (n < 20) return Ones[n];
        if (n < 100) return Tens[n / 10] + (n % 10 > 0 ? " " + Ones[n % 10] : "");
        if (n < 1000) return Ones[n / 100] + " Hundred" + (n % 100 > 0 ? " " + Words(n % 100) : "");
        if (n < 1_00_000) return Words(n / 1000) + " Thousand" + (n % 1000 > 0 ? " " + Words(n % 1000) : "");
        if (n < 1_00_00_000) return Words(n / 1_00_000) + " Lakh" + (n % 1_00_000 > 0 ? " " + Words(n % 1_00_000) : "");
        return Words(n / 1_00_00_000) + " Crore" + (n % 1_00_00_000 > 0 ? " " + Words(n % 1_00_00_000) : "");
    }
}
