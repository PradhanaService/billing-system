using WaterBilling.Services.Common;

namespace WaterBilling.Tests.Common;

public class VolumeAndTimeTests
{
    [Fact]
    public void Volume_Stored_As_Tenths()
    {
        Assert.Equal(284512, Volume.FromKilolitres(28451.2m));
        Assert.Equal(28451.2m, Volume.ToKilolitres(284512));
        Assert.Equal("28451.2", Volume.Format(284512));
        Assert.Equal("0.0", Volume.Format(0));
    }

    [Fact]
    public void Volume_Refuses_More_Than_One_Decimal() =>
        Assert.Throws<ArgumentException>(() => Volume.FromKilolitres(1.25m));

    [Fact]
    public void NFR_AUD_03_Utc_Shown_As_Ist()
    {
        var utc = new DateTime(2026, 11, 5, 12, 45, 0, DateTimeKind.Utc);   // 12:45 UTC

        Assert.Equal(new DateTime(2026, 11, 5, 18, 15, 0), IndianTime.ToIst(utc));
        Assert.Equal("05/11/2026 06:15 PM", IndianTime.FormatDateTime(utc));
    }

    [Fact]
    public void Ist_Crosses_Midnight_Correctly()
    {
        var utc = new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal("06/11/2026 01:30 AM", IndianTime.FormatDateTime(utc));
    }

    [Fact]
    public void NFR_USE_02_Date_Format() =>
        Assert.Equal("05/11/2026", IndianTime.FormatDate(new DateOnly(2026, 11, 5)));

    [Fact]
    public void ToIst_Refuses_Non_Utc_Time() =>
        Assert.Throws<ArgumentException>(() => IndianTime.ToIst(new DateTime(2026, 1, 1)));
}
