using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WaterBilling.Data;
using WaterBilling.Data.Entities;

namespace WaterBilling.Tests.Design;

// Puts a few master records straight into the web test app's database, so a page test can open
// a screen with real data on it. (Business rules are tested on the services; this only fills tables.)
public static class WebSeed
{
    public static async Task<long> SupplierAsync(TestAppFactory f, string name, string phone = "9123456780", string? contact = null, string? address = null)
    {
        await using var db = await Db(f);
        var s = new Supplier { Name = name, ShortName = name.Split(' ')[0],
                               Phone = phone, ContactPerson = contact, Address = address };
        db.Suppliers.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    public static async Task<long> VehicleAsync(TestAppFactory f, long supplierId, string code, string vehicleNo, long ratePaise = 12968, int capacity = 24000,
        VehicleStatus status = VehicleStatus.Active)
    {
        await using var db = await Db(f);
        var v = new Vehicle { Code = code, VehicleNo = vehicleNo, NormalizedVehicleNo = vehicleNo.Replace(" ", ""), Nickname = code + " " + vehicleNo,
                              SupplierId = supplierId, TankCapacityLitres = capacity, RatePaise = ratePaise, Status = status };
        db.Vehicles.Add(v);
        await db.SaveChangesAsync();
        return v.Id;
    }

    public static async Task<AppDbContext> Db(TestAppFactory f) =>
        await f.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
}
