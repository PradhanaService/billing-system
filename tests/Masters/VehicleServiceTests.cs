using WaterBilling.Data.Entities;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Masters;

public class VehicleServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeCurrentUser _me;
    private readonly long _seller;   // the "Deleted by" name on the Sales login
    private readonly VehicleService _svc;
    private readonly SupplierService _suppliers;
    private readonly long _sangeetha;

    public VehicleServiceTests()
    {
        _me = new FakeCurrentUser(_db.AddUser(UserRole.SalesPerson));
        _seller = _db.AddSalesPerson();
        _svc = new VehicleService(_db, _me, _db.Clock);
        _suppliers = new TestServices(_db, _me).Suppliers;
        _sangeetha = _suppliers.CreateAsync(SupplierServiceTests.Form()).Result.Value;
    }

    public void Dispose() => _db.Dispose();

    private int _codes;   // each test vehicle gets its own typed code: T1, T2 ...
    private VehicleForm Form(string no = "tn 38  m 3") => new()
    {
        Code = $"T{++_codes}", VehicleNo = no, SupplierId = _sangeetha, TankCapacityLitres = 12000, Rate = 150, TripCost = 2500,
    };

    [Fact]
    public async Task FR_VM_03_Code_Is_Typed_By_The_User_And_Required()
    {
        // Client change, Nov 2026: the code is entered by hand, not proposed by the system.
        var none = Form("TN 38 M 3");
        none.Code = null;
        Assert.Contains("Vehicle Code is required", (await _svc.CreateAsync(none)).Error);

        var typed = Form("TN 67 C");
        typed.Code = " a13 ";
        Assert.True((await _svc.CreateAsync(typed)).Ok);
        Assert.Equal("A13", Assert.Single(await _svc.SearchAsync(null, StatusFilter.All)).Code);
    }

    [Fact]
    public async Task FR_VM_03_Code_Of_Deleted_Vehicle_Is_Never_Reused()
    {
        var first = Form();
        first.Code = "A1";
        var id = (await _svc.CreateAsync(first)).Value;
        await _svc.DeleteAsync(id, _seller);

        var again = Form("TN 67 C");
        again.Code = "A1";
        Assert.Contains("already used", (await _svc.CreateAsync(again)).Error);
    }

    [Fact]
    public async Task FR_VM_03_A_Typed_Code_Must_Be_Valid_And_Unused()
    {
        var adminForm = Form("TN 38 M 3");   // any login may type the code now
        adminForm.Code = "s13";
        Assert.True((await _svc.CreateAsync(adminForm)).Ok);
        Assert.Contains(await _svc.SearchAsync(null, StatusFilter.All), r => r.Code == "S13");

        var bad = Form("TN 10 A");
        bad.Code = "A-13";   // only letters and numbers
        Assert.False((await _svc.CreateAsync(bad)).Ok);
        var dup = Form("TN 11 A");
        dup.Code = "S13";
        Assert.Contains("already used", (await _svc.CreateAsync(dup)).Error);
    }

    [Fact]
    public async Task FR_VM_04_Vehicle_No_Is_Uppercase_Tidied_And_Unique_Ignoring_Spaces()
    {
        await _svc.CreateAsync(Form("tn 38  m 3"));

        var dup = await _svc.CreateAsync(Form("TN38M3"));

        Assert.Equal("TN 38 M 3", (await _svc.SearchAsync(null, StatusFilter.All)).Single().VehicleNo);
        Assert.False(dup.Ok);
        Assert.Contains("already exists", dup.Error);
    }

    [Theory]
    [InlineData("TN 67 C", true)]
    [InlineData("TN 01 AB 1234", true)]
    [InlineData("12345", false)]
    [InlineData("TN-38-M-3", false)]
    public async Task FR_VM_04_Vehicle_No_Format(string number, bool ok) =>
        Assert.Equal(ok, (await _svc.CreateAsync(Form(number))).Ok);

    [Fact]
    public async Task FR_VM_05_Nickname_Defaults_To_Short_Name_And_Plate()
    {
        await _svc.CreateAsync(Form("TN 38 M 3"));
        Assert.Equal("Sangeetha TN 38 M 3", (await _svc.SearchAsync(null, StatusFilter.All)).Single().Nickname);
    }

    [Theory]
    [InlineData(0, 150, "Capacity")]
    [InlineData(12000, 0, "greater than zero")]
    public async Task Capacity_And_Rate_Must_Be_Above_Zero(int capacity, decimal rate, string message)
    {
        var form = Form();
        form.TankCapacityLitres = capacity;
        form.Rate = rate;

        var result = await _svc.CreateAsync(form);

        Assert.False(result.Ok);
        Assert.Contains(message, result.Error);
    }

    [Fact]
    public async Task FR_VM_09_Only_Active_Vehicles_Of_Active_Suppliers_Are_Billable()
    {
        await _svc.CreateAsync(Form("TN 38 M 3"));
        var maint = Form("TN 67 C");
        maint.Status = VehicleStatus.Maintenance;
        await _svc.CreateAsync(maint);

        Assert.Equal("TN 38 M 3", Assert.Single(await _svc.ListBillableAsync()).VehicleNo);

        // Deactivate the supplier: its vehicles become unavailable, their own status unchanged.
        var supplier = (await _suppliers.GetAsync(_sangeetha))!;
        supplier.Status = RecordStatus.Inactive;
        await _suppliers.UpdateAsync(_sangeetha, supplier);

        Assert.Empty(await _svc.ListBillableAsync());
        Assert.Contains(await _svc.SearchAsync(null, StatusFilter.Active), r => r.VehicleNo == "TN 38 M 3");
    }

    [Fact]
    public async Task Cannot_Add_Vehicle_To_Inactive_Supplier()
    {
        var supplier = (await _suppliers.GetAsync(_sangeetha))!;
        supplier.Status = RecordStatus.Inactive;
        await _suppliers.UpdateAsync(_sangeetha, supplier);

        Assert.False((await _svc.CreateAsync(Form())).Ok);
    }

    [Fact]
    public async Task FR_VM_08_Supplier_With_Vehicles_Cannot_Be_Deleted()
    {
        await _svc.CreateAsync(Form());

        var result = await _suppliers.DeleteAsync(_sangeetha, _seller);

        Assert.False(result.Ok);
        Assert.Contains("Inactive", result.Error);
    }

    [Fact]
    public async Task FR_VM_10_Search_By_Number_Code_Name_Or_Supplier()
    {
        await _svc.CreateAsync(Form("TN 38 M 3"));

        Assert.Single(await _svc.SearchAsync("TN38", StatusFilter.All));
        Assert.Single(await _svc.SearchAsync("T1", StatusFilter.All));
        Assert.Single(await _svc.SearchAsync("sangeetha", StatusFilter.All));
        Assert.Empty(await _svc.SearchAsync("kaveri", StatusFilter.All));
    }

    [Fact]
    public async Task Rate_Change_Is_Logged_Separately()
    {
        var id = (await _svc.CreateAsync(Form())).Value;
        var form = (await _svc.GetAsync(id))!;
        form.Rate = 175;

        await _svc.UpdateAsync(id, form);

        using var ctx = _db.NewContext();
        var log = ctx.ActivityLogs.Single(l => l.Action == "VEHICLE_RATE_CHANGED");
        Assert.Contains("15000", log.BeforeJson);
        Assert.Contains("17500", log.AfterJson);
    }

    [Fact]
    public async Task Supplier_Cannot_Be_Changed_On_Edit()
    {
        var other = (await _suppliers.CreateAsync(SupplierServiceTests.Form("Kaveri Tankers"))).Value;
        var id = (await _svc.CreateAsync(Form())).Value;
        var form = (await _svc.GetAsync(id))!;
        form.SupplierId = other;

        Assert.False((await _svc.UpdateAsync(id, form)).Ok);
    }

    [Fact]
    public async Task Deleted_Vehicle_Number_Can_Be_Registered_Again()
    {
        var id = (await _svc.CreateAsync(Form("TN 38 M 3"))).Value;
        await _svc.DeleteAsync(id, _seller);

        Assert.True((await _svc.CreateAsync(Form("TN 38 M 3"))).Ok);
    }

    [Fact]
    public async Task Dispatch_Operator_Can_Add_Vehicles_And_Choose_Code()
    {
        _me.User = _db.AddUser(UserRole.SalesPerson);
        var form = Form();
        form.Code = "D7";
        Assert.True((await _svc.CreateAsync(form)).Ok);
        Assert.Equal("D7", Assert.Single(await _svc.SearchAsync(null, StatusFilter.All)).Code);
    }
}
