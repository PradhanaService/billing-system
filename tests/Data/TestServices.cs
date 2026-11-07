using WaterBilling.Services.Accounts;
using WaterBilling.Services.Billing;
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
    public InvoiceService Invoices => new(db, me, db.Clock, Poster, Calendar);
    public ReceiptService Receipts => new(db, me, db.Clock, Poster);
    public PaymentService Payments => new(db, me, db.Clock, Poster);
    public LedgerMasterService Ledgers => new(db, me, db.Clock);
    public StatementService Statements => new(db, me);
    public OutstandingService Outstanding => new(db, me);
    public DaySalesService DaySales => new(db, me, db.Clock, Poster, Calendar);
    public DashboardService Dashboard => new(db, me, Calendar);
    public WaterBilling.Services.Reports.ReportService Reports => new(db, me);
    public WaterBilling.Services.Admin.ActivityLogService ActivityLog => new(db, me, db.Clock);
    public WaterBilling.Services.Admin.SalesPersonService SalesPersons => new(db, me, db.Clock);
    public WaterBilling.Services.Recovery.RecoveryService Recovery => new(db, me, db.Clock);
}
