using Bunit;
using Bunit.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Accounts;
using WaterBilling.Services.Admin;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Ui;

// Screen tests (bUnit): renders a real page with the real services on an in-memory database,
// then clicks and types like a user. Pop-ups and forms that only appear after a click are tested here.
public abstract class UiTest : TestContext
{
    protected readonly TestDatabase Db = new();
    protected readonly FakeCurrentUser Me;
    protected TestServices S => new(Db, Me);

    protected UiTest(UserRole role = UserRole.Admin)
    {
        var user = Db.AddUser(role);
        Me = new FakeCurrentUser(user);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(Db);
        Services.AddSingleton<ICurrentUser>(Me);
        Services.AddSingleton<TimeProvider>(Db.Clock);
        Services.AddSingleton(Db.Changes);
        Services.AddSingleton<BusinessCalendar>();
        Services.AddTransient<LedgerPoster>();
        Services.AddTransient<SupplierService>();
        Services.AddTransient<CustomerService>();
        Services.AddTransient<VehicleService>();
        Services.AddTransient<InvoiceService>();
        Services.AddTransient<InvoiceSearchService>();
        Services.AddTransient<ReceiptService>();
        Services.AddTransient<PaymentService>();
        Services.AddTransient<LedgerMasterService>();
        Services.AddTransient<OutstandingService>();
        Services.AddTransient<DashboardService>();
        Services.AddTransient<SalesPersonService>();
        Services.AddTransient<ActivityLogService>();
        Services.AddTransient<WaterBilling.Services.Reports.ReportService>();
        Services.AddTransient<WaterBilling.Services.Reports.TripReportService>();
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized(user.UserName);
        auth.SetClaims(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
                       new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role.ToString()));
    }

    // A supplier with one vehicle, made through the real services. Returns (supplierId, vehicleId).
    protected async Task<(long Supplier, long Vehicle)> SupplierWithVehicleAsync(string name = "Kaveri Tankers", string code = "A2",
        string vehicleNo = "TN 67 C", decimal rate = 129.68m, int capacity = 24000)
    {
        var supplier = (await S.Suppliers.CreateAsync(new SupplierForm { Name = name, ShortName = name.Split(' ')[0], Phone = "9123456780" })).Value;
        var vehicle = (await S.Vehicles.CreateAsync(new VehicleForm { Code = code, VehicleNo = vehicleNo, SupplierId = supplier,
                                                                     TankCapacityLitres = capacity, Rate = rate })).Value;
        return (supplier, vehicle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Db.Dispose();
        base.Dispose(disposing);
    }
}
