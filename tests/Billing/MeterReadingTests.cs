using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;

namespace WaterBilling.Tests.Billing;

// Client requirement (Nov 2026): meter readings of a saved invoice can be corrected, and the
// correction carries on to the invoices before and after it on the same meter.
public class MeterReadingTests : BillingFixture
{
    private MeterReadingService Meters => Svc.MeterReadings;

    // Three trips on Meter 1: 100.0-110.0, 110.0-125.5, 125.5-130.0 (invoices 1, 2, 3).
    private async Task<long[]> ThreeTripsAsync()
    {
        var ids = new List<long>();
        foreach (var (s, e) in new[] { (100.0m, 110.0m), (110.0m, 125.5m), (125.5m, 130.0m) })
        {
            var r = await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1Start = s, Meter1End = e });
            Assert.True(r.Ok, r.Error);
            ids.Add(r.Value);
        }
        return [.. ids];
    }

    private Invoice Get(long id) { using var db = Db.NewContext(); return db.Invoices.Single(i => i.Id == id); }

    private MeterEdit EditOf(long id, decimal? s1, decimal? e1, string reason = "Misread the dial")
    {
        var inv = Get(id);
        return new MeterEdit { EditedBySalesPersonId = Seller, Meter1Start = s1, Meter1End = e1, Reason = reason, Version = inv.Version };
    }

    [Fact]
    public async Task MTR_01_Changing_The_End_Reading_Moves_The_Next_Invoices_Start()
    {
        var ids = await ThreeTripsAsync();

        var result = await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, 124.0m));

        Assert.True(result.Ok, result.Error);
        Assert.Equal((1100L, 1240L, 140L), (Get(ids[1]).Meter1StartTenths!.Value, Get(ids[1]).Meter1EndTenths!.Value, Get(ids[1]).VolumeTenths));
        Assert.Equal((1240L, 1300L, 60L), (Get(ids[2]).Meter1StartTenths!.Value, Get(ids[2]).Meter1EndTenths!.Value, Get(ids[2]).VolumeTenths));
        Assert.Equal(1100L, Get(ids[0]).Meter1EndTenths);                      // previous invoice untouched
        Assert.Equal("invoice 3, Meter 1 start: 125.5 → 124.0", result.Value!.Single().Text);
    }

    [Fact]
    public async Task MTR_02_Changing_The_Start_Reading_Moves_The_Previous_Invoices_End()
    {
        var ids = await ThreeTripsAsync();

        Assert.True((await Meters.UpdateAsync(ids[1], EditOf(ids[1], 108.5m, 125.5m))).Ok);

        Assert.Equal((1000L, 1085L, 85L), (Get(ids[0]).Meter1StartTenths!.Value, Get(ids[0]).Meter1EndTenths!.Value, Get(ids[0]).VolumeTenths));
        Assert.Equal(1255L, Get(ids[2]).Meter1StartTenths);                    // next invoice untouched
    }

    [Fact]
    public async Task MTR_03_A_Correction_That_Would_Break_A_Neighbour_Is_Refused()
    {
        var ids = await ThreeTripsAsync();

        var tooHigh = await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, 131.0m));   // invoice 3 ends at 130.0
        var tooLow = await Meters.UpdateAsync(ids[1], EditOf(ids[1], 99.0m, 125.5m));     // invoice 1 starts at 100.0

        Assert.Contains("above the end reading of invoice 3", tooHigh.Error);
        Assert.Contains("below the start reading of invoice 1", tooLow.Error);
        Assert.Equal((1255L, 1100L), (Get(ids[2]).Meter1StartTenths!.Value, Get(ids[0]).Meter1EndTenths!.Value));   // nothing saved
    }

    [Fact]
    public async Task MTR_04_Taking_A_Meter_Off_An_Invoice_Joins_Its_Neighbours()
    {
        var ids = await ThreeTripsAsync();

        Assert.True((await Meters.UpdateAsync(ids[1], EditOf(ids[1], null, null, "Meter 1 was not used"))).Ok);

        Assert.Null(Get(ids[1]).Meter1StartTenths);
        Assert.Equal(0, Get(ids[1]).VolumeTenths);
        Assert.Equal(1100L, Get(ids[2]).Meter1StartTenths);                    // next trip now starts where invoice 1 ended
    }

    [Fact]
    public async Task MTR_05_Cancelled_Invoices_Are_Skipped_And_A_Corrected_Copy_Keeps_Its_Place()
    {
        var ids = await ThreeTripsAsync();
        Me.User = Db.AddUser(UserRole.Admin);
        Assert.True((await Svc.Invoices.CancelAsync(ids[2], "Wrong vehicle", Get(ids[2]).Version)).Ok);   // invoice 3 cancelled
        var copy = (await Svc.Invoices.ReviseAsync(ids[0], new InvoiceRevision { CapacityLitres = 12000, Rate = 160, Version = Get(ids[0]).Version })).Value;   // invoice 1 -> copy 4

        // Correcting invoice 2's start moves the END of the copy (it stands where invoice 1 stood), not of the cancelled 1.
        Assert.True((await Meters.UpdateAsync(ids[1], EditOf(ids[1], 109.0m, 126.0m))).Ok);

        Assert.Equal(1090L, Get(copy).Meter1EndTenths);
        Assert.Equal(1100L, Get(ids[0]).Meter1EndTenths);                     // cancelled original untouched
        Assert.Equal(1255L, Get(ids[2]).Meter1StartTenths);                   // cancelled invoice 3 untouched
        var starts = await Svc.Invoices.MeterStartsAsync();
        Assert.Equal((1260L, "2"), (starts[0].EndTenths!.Value, starts[0].FromInvoiceNo));   // the next new invoice starts at 126.0
    }

    [Fact]
    public async Task MTR_06_Needs_Reason_And_Who_Is_Editing_And_Is_Logged_Without_Touching_The_Ledger()
    {
        var ids = await ThreeTripsAsync();
        long ledgerLines; using (var db = Db.NewContext()) ledgerLines = db.LedgerEntries.Count();

        Assert.Contains("reason", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, 124.0m, " "))).Error);
        var noName = EditOf(ids[1], 110.0m, 124.0m); noName.EditedBySalesPersonId = null;
        Assert.Contains("salesperson", (await Meters.UpdateAsync(ids[1], noName)).Error);
        Assert.Contains("Nothing was changed", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, 125.5m))).Error);
        var stale = EditOf(ids[1], 110.0m, 124.0m); stale.Version--;
        Assert.False((await Meters.UpdateAsync(ids[1], stale)).Ok);
        Assert.True((await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, 124.0m))).Ok);

        using var check = Db.NewContext();
        Assert.Equal(ledgerLines, check.LedgerEntries.Count());               // readings never change money
        var changed = check.ActivityLogs.Single(l => l.Action == "INVOICE_METERS_CHANGED");
        Assert.Equal(("Ravi", "Misread the dial"), (changed.DoneBy, changed.Reason));
        Assert.Contains("110.0 to 125.5", changed.BeforeJson);
        Assert.Contains("110.0 to 124.0", changed.AfterJson);
        var adjusted = check.ActivityLogs.Single(l => l.Action == "INVOICE_METERS_ADJUSTED");
        Assert.Equal(ids[2], adjusted.EntityId);
        Assert.StartsWith("Follows the meter correction on invoice 2", adjusted.Reason);
    }

    [Fact]
    public async Task MTR_07_Cancelled_Invoice_Cannot_Be_Corrected_And_Bad_Readings_Are_Refused()
    {
        var ids = await ThreeTripsAsync();
        Assert.Contains("end reading cannot be lower", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], 120.0m, 115.0m))).Error);
        Assert.Contains("both the start and the end", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, null))).Error);
        Me.User = Db.AddUser(UserRole.Admin);
        await Svc.Invoices.CancelAsync(ids[1], "test", Get(ids[1]).Version);
        Assert.Contains("cancelled", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], 110.0m, 124.0m))).Error);
    }

    [Fact]
    public async Task MTR_08_Each_Meter_Has_Its_Own_Chain()
    {
        var a = (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1Start = 10, Meter1End = 20, Meter2Start = 500, Meter2End = 505 })).Value;
        var b = (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1Start = 20, Meter1End = 30 })).Value;
        var c = (await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter2Start = 505, Meter2End = 509 })).Value;

        var edit = EditOf(a, 10, 20); edit.Meter2Start = 500; edit.Meter2End = 506;
        Assert.True((await Meters.UpdateAsync(a, edit)).Ok);

        Assert.Equal(5060L, Get(c).Meter2StartTenths);    // the next Meter 2 trip is invoice 3, not invoice 2
        Assert.Equal(200L, Get(b).Meter1StartTenths);
        Assert.Null(Get(b).Meter2StartTenths);
    }
}
