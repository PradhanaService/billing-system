using WaterBilling.Data.Entities;
using WaterBilling.Services.Masters;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Billing;

// A small shop for billing tests: one supplier, a per-trip vehicle (₹150/trip),
// a per-kL vehicle (₹12.35/kL), and a customer. Acting user is a Billing Clerk.
public class BillingFixture : IDisposable
{
    public TestDatabase Db { get; } = new();
    public FakeCurrentUser Me { get; }
    public TestServices Svc { get; }
    public long Supplier { get; }
    public long TripVehicle { get; }
    public long VolumeVehicle { get; }
    public long Customer { get; }

    public BillingFixture()
    {
        Me = new FakeCurrentUser(Db.AddUser(UserRole.Admin));
        Svc = new TestServices(Db, Me);
        Supplier = Svc.Suppliers.CreateAsync(new SupplierForm { Name = "Sangeetha Water Supply", ShortName = "Sangeetha", Phone = "9876543210" }).Result.Value;
        TripVehicle = Svc.Vehicles.CreateAsync(new VehicleForm { VehicleNo = "TN 38 M 3", SupplierId = Supplier, TankCapacityLitres = 12000, Rate = 150 }).Result.Value;
        VolumeVehicle = Svc.Vehicles.CreateAsync(new VehicleForm
        {
            VehicleNo = "TN 67 C", SupplierId = Supplier, TankCapacityLitres = 24000, Rate = 12.35m, BillingBasis = BillingBasis.Volume,
        }).Result.Value;
        Customer = Svc.Customers.CreateAsync(new CustomerForm { Name = "Meena Constructions", Phone = "9000000001" }).Result.Value;
        Me.User = Db.AddUser(UserRole.BillingClerk);
    }

    public void Dispose() => Db.Dispose();
}
