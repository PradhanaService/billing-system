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
    public async Task MTR_02_The_Start_Reading_Is_Locked_And_Only_The_Admin_Can_Set_One_For_A_Replaced_Meter()
    {
        var ids = await ThreeTripsAsync();

        var refused = await Meters.UpdateAsync(ids[1], EditOf(ids[1], 108.5m, 125.5m));
        Assert.Contains("start reading cannot be changed", refused.Error);
        Assert.Contains("invoice 1", refused.Error);                                   // fix it on the previous invoice

        var salesReplace = EditOf(ids[1], 0m, 15.5m); salesReplace.MeterReplaced = true;
        Assert.Contains("Only the Admin", (await Meters.UpdateAsync(ids[1], salesReplace)).Error);

        Me.User = Db.AddUser(UserRole.Admin);
        var replaced = EditOf(ids[1], 0m, 15.5m, "Meter 1 replaced"); replaced.MeterReplaced = true;
        Assert.True((await Meters.UpdateAsync(ids[1], replaced)).Ok);
        Assert.Equal((0L, 155L), (Get(ids[1]).Meter1StartTenths!.Value, Get(ids[1]).Meter1EndTenths!.Value));
        Assert.Equal(1100L, Get(ids[0]).Meter1EndTenths);                              // the old meter's last trip is untouched
        Assert.Equal(155L, Get(ids[2]).Meter1StartTenths);                             // the next trip follows the new meter
        using var db = Db.NewContext();
        Assert.Contains(db.ActivityLogs, l => l.Action == "INVOICE_METER_REPLACED" && l.Reason == "Meter 1 replaced");
    }

    [Fact]
    public async Task MTR_03_A_Correction_That_Would_Break_A_Neighbour_Is_Refused()
    {
        var ids = await ThreeTripsAsync();

        var tooHigh = await Meters.UpdateAsync(ids[1], EditOf(ids[1], null, 131.0m));    // invoice 3 ends at 130.0
        var tooLow = await Meters.UpdateAsync(ids[1], EditOf(ids[1], null, 105.0m));     // below its own start 110.0

        Assert.Contains("above the end reading of invoice 3", tooHigh.Error);
        Assert.Contains("cannot be lower than the start reading (110.0)", tooLow.Error);
        Assert.Equal(1255L, Get(ids[2]).Meter1StartTenths);                            // nothing saved
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

        // Correcting the copy's End (it stands where invoice 1 stood) moves invoice 2's Start, not anything on cancelled invoices.
        Assert.True((await Meters.UpdateAsync(copy, EditOf(copy, null, 109.0m))).Ok);

        Assert.Equal(1090L, Get(ids[1]).Meter1StartTenths);
        Assert.Equal(1100L, Get(ids[0]).Meter1EndTenths);                     // cancelled original untouched
        Assert.Equal(1255L, Get(ids[2]).Meter1StartTenths);                   // cancelled invoice 3 untouched
        var starts = await Svc.Invoices.MeterStartsAsync();
        Assert.Equal((1255L, "2"), (starts[0].EndTenths!.Value, starts[0].FromInvoiceNo));   // the next new invoice starts at 125.5
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
        Assert.Contains("end reading cannot be lower", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], null, 105.0m))).Error);
        Assert.Contains("one decimal place", (await Meters.UpdateAsync(ids[1], EditOf(ids[1], null, 120.05m))).Error);
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

    [Fact]
    public async Task MTR_09_New_Invoice_Start_Is_The_Last_End_And_Cannot_Be_Typed_Differently()
    {
        await ThreeTripsAsync();                                                  // meter 1 last ended at 130.0

        var typedWrong = await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1Start = 128m, Meter1End = 140m });
        Assert.Contains("start reading cannot be changed", typedWrong.Error);

        var endOnly = await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1End = 140m });
        Assert.True(endOnly.Ok, endOnly.Error);
        Assert.Equal((1300L, 1400L), (Get(endOnly.Value).Meter1StartTenths!.Value, Get(endOnly.Value).Meter1EndTenths!.Value));

        var unused = await Svc.Invoices.CreateAsync(new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1Start = 140m });
        Assert.True(unused.Ok, unused.Error);                                    // no End: meter not used on this trip
        Assert.Null(Get(unused.Value).Meter1StartTenths);

        var salesReset = new InvoiceForm { SalesPersonId = Seller, VehicleId = TripVehicle, Rate = 150, Meter1Start = 0m, Meter1End = 5m, MeterReplaced = true, MeterReplacedReason = "new meter" };
        Assert.Contains("Only the Admin", (await Svc.Invoices.CreateAsync(salesReset)).Error);
        Me.User = Db.AddUser(UserRole.Admin);
        salesReset.SalesPersonId = null;
        var reset = await Svc.Invoices.CreateAsync(salesReset);
        Assert.True(reset.Ok, reset.Error);
        Assert.Equal((0L, 50L), (Get(reset.Value).Meter1StartTenths!.Value, Get(reset.Value).Meter1EndTenths!.Value));
    }

    [Fact]
    public async Task MTR_12_Invoice_Edit_Meters_Only_Keeps_The_Number_And_With_Rate_Goes_On_The_Corrected_Copy()
    {
        var ids = await ThreeTripsAsync();
        var inv = Get(ids[1]);
        var meters = new MeterEdit { EditedBySalesPersonId = Seller, Meter1End = 124.0m, Reason = "Misread the dial" };

        var onlyMeters = await Svc.Invoices.EditAsync(ids[1], new InvoiceRevision { EditedBySalesPersonId = Seller, CapacityLitres = inv.CapacityLitres,
            Rate = 150, Version = inv.Version, Meters = meters });

        Assert.True(onlyMeters.Ok, onlyMeters.Error);
        Assert.Equal((ids[1], false), (onlyMeters.Value!.InvoiceId, onlyMeters.Value.NewCopy));             // same invoice, same number
        Assert.Equal("invoice 3, Meter 1 start: 125.5 → 124.0", onlyMeters.Value.MeterChanges.Single().Text);
        Assert.Equal(InvoiceStatus.Posted, Get(ids[1]).Status);

        inv = Get(ids[1]);
        var withRate = await Svc.Invoices.EditAsync(ids[1], new InvoiceRevision { EditedBySalesPersonId = Seller, CapacityLitres = inv.CapacityLitres,
            Rate = 160, Version = inv.Version, Meters = new MeterEdit { Meter1End = 123.0m, Reason = "Rate and reading" } });

        Assert.True(withRate.Ok, withRate.Error);
        Assert.True(withRate.Value!.NewCopy);
        var copy = Get(withRate.Value.InvoiceId);
        Assert.Equal((1100L, 1230L, 16000L), (copy.Meter1StartTenths!.Value, copy.Meter1EndTenths!.Value, copy.AmountPaise));
        Assert.Equal(1230L, Get(ids[2]).Meter1StartTenths);                     // the next trip follows the copy
        Assert.Equal(InvoiceStatus.Cancelled, Get(ids[1]).Status);

        // A refused meter change keeps the whole edit from saving (no corrected copy is issued).
        inv = Get(ids[2]);
        var refused = await Svc.Invoices.EditAsync(ids[2], new InvoiceRevision { EditedBySalesPersonId = Seller, CapacityLitres = inv.CapacityLitres,
            Rate = 170, Version = inv.Version, Meters = new MeterEdit { Meter1End = 100.0m, Reason = "x" } });
        Assert.False(refused.Ok);
        Assert.Equal(InvoiceStatus.Posted, Get(ids[2]).Status);
        using var db = Db.NewContext();
        Assert.Equal(4, db.Invoices.Count());                                   // 3 trips + 1 corrected copy; the refused edit added none
        Assert.Equal(db.LedgerEntries.Sum(e => e.DebitPaise), db.LedgerEntries.Sum(e => e.CreditPaise));
    }
}
