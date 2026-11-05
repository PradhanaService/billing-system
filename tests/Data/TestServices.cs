using WaterBilling.Services.Ledger;
using WaterBilling.Services.Masters;

namespace WaterBilling.Tests.Data;

// Builds every service on the test database, acting as the given (changeable) user.
public sealed class TestServices(TestDatabase db, FakeCurrentUser me)
{
    public BusinessCalendar Calendar => new(db.Clock);
    public LedgerPoster Poster => new(db.Clock, Calendar);
    public SupplierService Suppliers => new(db, me, db.Clock, Poster);
    public CustomerService Customers => new(db, me, db.Clock, Poster);
    public VehicleService Vehicles => new(db, me, db.Clock);
}
