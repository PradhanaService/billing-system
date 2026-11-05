using WaterBilling.Data.Entities;
using WaterBilling.Services.Masters;
using WaterBilling.Services.Security;
using WaterBilling.Tests.Data;

namespace WaterBilling.Tests.Masters;

public class CustomerServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeCurrentUser _me;
    private readonly CustomerService _svc;

    public CustomerServiceTests()
    {
        _me = new FakeCurrentUser(_db.AddUser(UserRole.Admin));
        _svc = new CustomerService(_db, _me, _db.Clock);
    }

    public void Dispose() => _db.Dispose();

    private static CustomerForm Form(string name = "Meena Constructions") => new() { Name = name, Phone = "9876543210" };

    [Fact]
    public async Task FR_VM_11_Creates_Customer_And_Refuses_Duplicate()
    {
        Assert.True((await _svc.CreateAsync(Form())).Ok);
        Assert.False((await _svc.CreateAsync(Form("MEENA constructions"))).Ok);
    }

    [Fact]
    public async Task Edit_Keeps_Opening_Balance_And_Logs_Before_After()
    {
        var form = Form();
        form.OpeningBalance = 200;
        var id = (await _svc.CreateAsync(form)).Value;
        var edit = (await _svc.GetAsync(id))!;
        edit.Address = "Anna Nagar";
        edit.OpeningBalance = 1;

        Assert.True((await _svc.UpdateAsync(id, edit)).Ok);

        var saved = (await _svc.GetAsync(id))!;
        Assert.Equal(200m, saved.OpeningBalance);
        Assert.Equal("Anna Nagar", saved.Address);
        using var ctx = _db.NewContext();
        var log = ctx.ActivityLogs.Single(l => l.Action == "CUSTOMER_UPDATED");
        Assert.Contains("Anna Nagar", log.AfterJson);
    }

    [Fact]
    public async Task Inactive_Customers_Are_Not_Offered_For_Billing()
    {
        await _svc.CreateAsync(Form("Active Co"));
        var off = Form("Old Co");
        off.Status = RecordStatus.Inactive;
        await _svc.CreateAsync(off);

        var list = await _svc.ListActiveAsync();

        Assert.Equal("Active Co", Assert.Single(list).Name);
    }

    [Fact]
    public async Task Viewer_Cannot_Create()
    {
        _me.User = _db.AddUser(UserRole.Accountant);
        await Assert.ThrowsAsync<NotAllowedException>(() => _svc.CreateAsync(Form()));
    }
}
