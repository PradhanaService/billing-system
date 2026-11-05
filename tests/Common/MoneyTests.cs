using WaterBilling.Services.Common;

namespace WaterBilling.Tests.Common;

public class MoneyTests
{
    [Theory]
    [InlineData("1250.50", 125050)]
    [InlineData("0", 0)]
    [InlineData("0.01", 1)]
    [InlineData("494784", 49478400)]
    public void FromRupees_Converts_To_Exact_Paise(string rupees, long expected) =>
        Assert.Equal(expected, Money.FromRupees(decimal.Parse(rupees)));

    [Fact]
    public void FromRupees_Refuses_More_Than_Two_Decimals() =>
        Assert.Throws<ArgumentException>(() => Money.FromRupees(10.005m));

    [Theory]
    [InlineData("12.345", 1235)]    // exactly half a paisa: goes up
    [InlineData("12.344", 1234)]
    [InlineData("12.3449", 1234)]
    [InlineData("0.005", 1)]
    [InlineData("-12.345", -1235)]  // half-up means away from zero for minus amounts too
    public void FR_INV_03_RoundHalfUp_To_Paise(string rupees, long expected) =>
        Assert.Equal(expected, Money.RoundHalfUp(decimal.Parse(rupees)));

    [Fact]
    public void Paise_Round_Trip_Through_Rupees_Is_Exact()
    {
        foreach (var paise in new long[] { 0, 1, 99, 100, 125050, 49478400, 999_999_999_99 })
            Assert.Equal(paise, Money.FromRupees(Money.ToRupees(paise)));
    }

    [Theory]
    [InlineData(49478400, "4,94,784.00")]
    [InlineData(125050, "1,250.50")]
    [InlineData(5, "0.05")]
    [InlineData(1234567890, "1,23,45,678.90")]
    public void NFR_USE_02_Format_Uses_Indian_Grouping(long paise, string expected) =>
        Assert.Equal(expected, Money.Format(paise));
}
