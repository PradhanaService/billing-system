using WaterBilling.Data.Entities;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Masters;

public class SupplierServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeCurrentUser _me;
    private readonly long _seller;   // the "Deleted by" name on the Sales login
    private readonly SupplierService _svc;

    public SupplierServiceTests()
    {
        _me = new FakeCurrentUser(_db.AddUser(UserRole.SalesPerson));
        _seller = _db.AddSalesPerson();
        _svc = new TestServices(_db, _me).Suppliers;
    }

    public void Dispose() => _db.Dispose();

    public static SupplierForm Form(string name = "Sangeetha Water Supply") => new()
    {
        Name = name, ShortName = "Sangeetha", Phone = "98765 43210", ContactPerson = "Murugan",
    };

    [Fact]
    public async Task FR_VM_01_Creates_Supplier_Tidied_And_Logged()
    {
        var result = await _svc.CreateAsync(new SupplierForm
        {
            Name = "  Sangeetha   Water Supply ", ShortName = " Sangeetha ", Phone = "98765-43210",
            OpeningBalance = 1250.50m, OpeningBalanceSide = BalanceSide.Dr,
        });

        Assert.True(result.Ok, result.Error);
        using var ctx = _db.NewContext();
        var s = ctx.Suppliers.Single();
        Assert.Equal("Sangeetha Water Supply", s.Name);
        Assert.Equal("Sangeetha", s.ShortName);
        Assert.Equal("9876543210", s.Phone);
        Assert.Equal(125050, s.OpeningBalancePaise);
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "SUPPLIER_CREATED" && l.EntityId == s.Id);
    }

    [Fact]
    public async Task FR_VM_01_Duplicate_Name_Ignoring_Case_Is_Refused()
    {
        await _svc.CreateAsync(Form());

        var dup = await _svc.CreateAsync(Form("SANGEETHA water  supply"));

        Assert.False(dup.Ok);
        Assert.Contains("already exists", dup.Error);
    }

    [Theory]
    [InlineData("", "Phone No. is required.")]
    [InlineData("12345", "10 to 15 digits")]
    [InlineData("98765abcde", "10 to 15 digits")]
    public async Task FR_VM_01_Phone_Is_Required_And_Checked(string phone, string message)
    {
        var form = Form();
        form.Phone = phone;

        var result = await _svc.CreateAsync(form);

        Assert.False(result.Ok);
        Assert.Contains(message, result.Error);
    }

    [Theory]
    [InlineData("10.005", "2 decimal")]
    [InlineData("-1", "negative")]
    public async Task Opening_Balance_Must_Be_Valid_Money(string amount, string message)
    {
        var form = Form();
        form.OpeningBalance = decimal.Parse(amount);

        var result = await _svc.CreateAsync(form);

        Assert.False(result.Ok);
        Assert.Contains(message, result.Error);
    }

    [Fact]
    public async Task FR_VM_12_Opening_Balance_Cannot_Change_After_Creation()
    {
        var created = await _svc.CreateAsync(new SupplierForm { Name = "A1 Water", ShortName = "A1", Phone = "9876543210", OpeningBalance = 500 });
        var form = (await _svc.GetAsync(created.Value))!;
        form.OpeningBalance = 9999;
        form.ContactPerson = "New contact";

        await _svc.UpdateAsync(created.Value, form);

        var reloaded = (await _svc.GetAsync(created.Value))!;
        Assert.Equal(500m, reloaded.OpeningBalance);
        Assert.Equal("New contact", reloaded.ContactPerson);
    }

    [Fact]
    public async Task FR_VM_10_Search_And_Status_Filter()
    {
        await _svc.CreateAsync(Form("Sangeetha Water Supply"));
        var other = Form("Kaveri Tankers");
        other.Status = RecordStatus.Inactive;
        await _svc.CreateAsync(other);

        Assert.Single(await _svc.SearchAsync(null, StatusFilter.Active));
        Assert.Single(await _svc.SearchAsync(null, StatusFilter.Inactive));
        Assert.Equal(2, (await _svc.SearchAsync(null, StatusFilter.All)).Count);
        Assert.Equal("Kaveri Tankers", Assert.Single(await _svc.SearchAsync("kaveri", StatusFilter.All)).Name);
        Assert.Single(await _svc.SearchAsync("98765", StatusFilter.Active));
    }

    [Fact]
    public async Task Search_Text_Is_Not_Sql()
    {
        await _svc.CreateAsync(Form());

        var rows = await _svc.SearchAsync("' OR 1=1 --", StatusFilter.All);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task NFR_CON_05_Stale_Edit_Is_Refused()
    {
        var id = (await _svc.CreateAsync(Form())).Value;
        var screenA = (await _svc.GetAsync(id))!;
        var screenB = (await _svc.GetAsync(id))!;
        screenA.ContactPerson = "A";
        await _svc.UpdateAsync(id, screenA);

        screenB.ContactPerson = "B";
        var result = await _svc.UpdateAsync(id, screenB);

        Assert.False(result.Ok);
        Assert.Contains("changed by someone else", result.Error);
    }

    [Fact]
    public async Task Rename_To_Another_Suppliers_Name_Is_Refused()
    {
        await _svc.CreateAsync(Form("Kaveri Tankers"));
        var id = (await _svc.CreateAsync(Form("Sangeetha Water Supply"))).Value;
        var form = (await _svc.GetAsync(id))!;
        form.Name = "kaveri tankers";

        Assert.False((await _svc.UpdateAsync(id, form)).Ok);
    }

    [Fact]
    public async Task FR_VM_08_Supplier_Without_History_Can_Be_Deleted()
    {
        var id = (await _svc.CreateAsync(Form())).Value;

        Assert.True((await _svc.DeleteAsync(id, _seller)).Ok);
        Assert.Empty(await _svc.SearchAsync(null, StatusFilter.All));
        using var ctx = _db.NewContext();
        Assert.Contains(ctx.ActivityLogs, l => l.Action == "SUPPLIER_DELETED");
    }

    [Fact]
    public async Task Every_Role_Can_Add_Edit_And_Delete_Suppliers()
    {
        foreach (var role in Enum.GetValues<UserRole>())
        {
            _me.User = _db.AddUser(role);
            var id = (await _svc.CreateAsync(Form($"Supplier {role}"))).Value;
            var form = (await _svc.GetAsync(id))!;
            form.ContactPerson = "Edited";
            Assert.True((await _svc.UpdateAsync(id, form)).Ok);
            Assert.True((await _svc.DeleteAsync(id, _seller)).Ok);
        }
    }

    [Fact]
    public async Task NFR_SEC_05_Not_Logged_In_Is_Refused()
    {
        _me.User = null;
        await Assert.ThrowsAsync<NotAllowedException>(() => _svc.CreateAsync(Form()));
    }
}
