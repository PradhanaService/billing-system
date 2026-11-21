using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;

namespace WaterBilling.Tests.Billing;

// Module 5 (final design): trip timing (date picked, time typed) and the three meter readings
// saved on the invoice, the capacity copied from Vehicle Master, and finding invoices for the toolbar.
public class TripDetailsTests : BillingFixture
{
    private InvoiceService Inv => Svc.Invoices;
    private static readonly DateOnly Day = new(2026, 11, 5);

    private InvoiceForm Form() => new() { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, PaymentMode = InvoicePaymentMode.Cash };

    [Theory]
    [InlineData("11:05 AM", 11, 5)]
    [InlineData("11:05am", 11, 5)]
    [InlineData("9:5 pm", 21, 5)]
    [InlineData("23:05", 23, 5)]
    [InlineData(" 07:30 PM ", 19, 30)]
    public void SI_01_Typed_Times_Are_Understood(string typed, int hour, int minute) =>
        Assert.Equal(new TimeOnly(hour, minute), IndianTime.ParseTime(typed));

    [Theory]
    [InlineData("25:00")]
    [InlineData("eleven")]
    [InlineData("11:75 AM")]
    public void SI_02_Bad_Times_Are_Refused(string typed) => Assert.Null(IndianTime.ParseTime(typed));

    [Fact]
    public async Task SI_03_Times_Are_Stored_In_UTC_And_Shown_In_IST()
    {
        var f = Form();
        (f.InDate, f.InTime, f.OutDate, f.OutTime) = (Day, "11:05 AM", Day, "11:15 AM");

        var id = (await Inv.CreateAsync(f)).Value;
        var d = (await Inv.GetAsync(id))!;

        Assert.Equal(new DateTime(2026, 11, 5, 5, 35, 0, DateTimeKind.Utc), d.InTimeUtc);
        Assert.Equal("05/11/2026 11:15 AM", IndianTime.FormatDateTime(d.OutTimeUtc!.Value));
    }

    [Fact]
    public async Task SI_04_Out_Time_Before_In_Time_Is_Refused()
    {
        var f = Form();
        (f.InDate, f.InTime, f.OutDate, f.OutTime) = (Day, "11:05 AM", Day, "10:55 AM");

        var r = await Inv.CreateAsync(f);

        Assert.False(r.Ok);
        Assert.Contains("Out Time cannot be before In Time", r.Error);
    }

    [Fact]
    public async Task SI_05_A_Typed_Time_Without_A_Date_Or_In_A_Bad_Shape_Is_Refused()
    {
        var f = Form();
        f.InTime = "11:05 AM";
        Assert.Contains("pick the date", (await Inv.CreateAsync(f)).Error);
        f.InDate = Day; f.InTime = "11.05.33";
        Assert.Contains("type the time like 11:05 AM", (await Inv.CreateAsync(f)).Error);
    }

    [Fact]
    public async Task SI_06_Meter_Readings_Give_The_Volume_And_Capacity_Comes_From_Vehicle_Master()
    {
        var f = Form();
        (f.Meter1Start, f.Meter1End, f.Meter3Start, f.Meter3End) = (82403.1m, 82404.1m, 28451.2m, 28452.1m);

        var d = (await Inv.GetAsync((await Inv.CreateAsync(f)).Value))!;

        Assert.Equal(19, d.Meters.VolumeTenths);   // 1.0 + 0.9 kL
        Assert.Equal(824031, d.Meters.Start1);
        Assert.Null(d.Meters.Start2);
        Assert.Equal(12000, d.CapacityLitres);
        Assert.Equal(15000, d.AmountPaise);         // still billed per trip
    }

    [Theory]
    [InlineData(100.0, 99.5, "end reading cannot be lower")]
    [InlineData(100.0, null, "give both the start and the end")]
    [InlineData(100.05, 101.0, "one decimal place")]
    public async Task SI_07_Bad_Meter_Readings_Are_Refused(double start, double? end, string message)
    {
        var f = Form();
        f.Meter2Start = (decimal)start;
        f.Meter2End = end is null ? null : (decimal)end;

        var r = await Inv.CreateAsync(f);

        Assert.False(r.Ok);
        Assert.Contains("Meter 2", r.Error);
        Assert.Contains(message, r.Error);
    }

    [Fact]
    public async Task SI_08_Toolbar_Finds_By_Number_And_Steps_Through_Invoices()
    {
        var first = (await Inv.CreateAsync(Form())).Value;
        var second = (await Inv.CreateAsync(Form())).Value;
        var no = (await Inv.GetAsync(first))!.DisplayNo;

        Assert.Equal(first, await Inv.FindByNumberAsync(no));
        Assert.Equal(first, await Inv.FindByNumberAsync("INV-" + no));
        Assert.Null(await Inv.FindByNumberAsync("abc"));
        Assert.Equal(second, await Inv.LatestAsync());
        Assert.Equal(first, await Inv.NeighbourAsync((await Inv.GetAsync(second))!.InvoiceNo, next: false));
    }

    [Fact]
    public async Task SI_20_Meter_Start_Carries_On_From_The_Last_End_Reading_Of_That_Meter()
    {
        Assert.All(await Inv.MeterStartsAsync(), m => Assert.Null(m.EndTenths));   // nothing yet

        var f1 = Form(); f1.Meter1Start = 28451.2m; f1.Meter1End = 28463.2m; f1.Meter2Start = 100m; f1.Meter2End = 110.5m;
        await Inv.CreateAsync(f1);
        var f2 = Form(); f2.Meter1Start = 28463.2m; f2.Meter1End = 28470.0m;          // meter 2 not used this time
        await Inv.CreateAsync(f2);

        var starts = await Inv.MeterStartsAsync();
        Assert.Equal((284700L, "2"), (starts[0].EndTenths!.Value, starts[0].FromInvoiceNo));   // meter 1: from invoice 2
        Assert.Equal((1105L, "1"), (starts[1].EndTenths!.Value, starts[1].FromInvoiceNo));     // meter 2: still from invoice 1
        Assert.Null(starts[2].FromInvoiceNo);                                                   // meter 3 never used
        Assert.Equal(28470.0m, starts[0].Kl);
    }
}
