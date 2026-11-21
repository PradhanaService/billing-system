using Microsoft.EntityFrameworkCore;
using WaterBilling.Data;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;
using WaterBilling.Services.Security;

namespace WaterBilling.Services.Reports;

// Vehicle Trip Report (final design, Nov 2026): the one report on the Reports screen.
// Filters: From, To, Supplier (All or one), Vehicle (All or one). Three shapes:
//   - All suppliers, all vehicles: per supplier, one summary line per vehicle, a supplier subtotal, a grand total.
//   - One supplier, all vehicles: that supplier's trips grouped vehicle by vehicle, with a subtotal per vehicle.
//   - One vehicle: that vehicle's trips, one line per invoice.
// Cancelled invoices are not counted. Screen and PDF are built from the same TripReport, so they always agree.

public enum TripReportShape { AllSuppliers, OneSupplier, OneVehicle }

public sealed record TripLine(int SNo, long InvoiceId, string InvoiceNo, DateOnly Date, string VehicleNo,
    DateTime? InTimeUtc, DateTime? OutTimeUtc, int Trips, long AmountPaise, long ReceivedPaise, PayState State);

public sealed record TripVehicleGroup(long VehicleId, string Code, string VehicleNo, IReadOnlyList<TripLine> Lines)
{
    public int Trips => Lines.Sum(l => l.Trips);
    public long BillPaise => Lines.Sum(l => l.AmountPaise);
    public long ReceivedPaise => Lines.Sum(l => l.ReceivedPaise);
    public long OutstandingPaise => BillPaise - ReceivedPaise;
}

public sealed record TripSupplierGroup(long SupplierId, string Name, IReadOnlyList<TripVehicleGroup> Vehicles)
{
    public int Trips => Vehicles.Sum(v => v.Trips);
    public long BillPaise => Vehicles.Sum(v => v.BillPaise);
    public long ReceivedPaise => Vehicles.Sum(v => v.ReceivedPaise);
    public long OutstandingPaise => BillPaise - ReceivedPaise;
}

public sealed record TripReport(TripReportShape Shape, string Title, string Subtitle, ReportFilter Filter, IReadOnlyList<TripSupplierGroup> Suppliers)
{
    public int Trips => Suppliers.Sum(s => s.Trips);
    public long BillPaise => Suppliers.Sum(s => s.BillPaise);
    public long ReceivedPaise => Suppliers.Sum(s => s.ReceivedPaise);
    public long OutstandingPaise => BillPaise - ReceivedPaise;
    public IEnumerable<TripLine> Lines => Suppliers.SelectMany(s => s.Vehicles).SelectMany(v => v.Lines);
}

public class TripReportService(IDbContextFactory<AppDbContext> dbFactory, ICurrentUser currentUser)
{
    public const string Name = "Vehicle Trip Report";

    public async Task<TripReport> BuildAsync(ReportFilter f)
    {
        await currentUser.RequireAsync(Permission.ReportsView);
        if (f.To < f.From) throw new ArgumentException("The To date is before the From date.");
        await using var db = await dbFactory.CreateDbContextAsync();

        var rows = await (from i in db.Invoices
                          where i.Status != InvoiceStatus.Cancelled && i.InvoiceDate >= f.From && i.InvoiceDate <= f.To
                              && (f.SupplierId == null || i.SupplierId == f.SupplierId) && (f.VehicleId == null || i.VehicleId == f.VehicleId)
                          join v in db.Vehicles on i.VehicleId equals v.Id
                          join s in db.Suppliers on i.SupplierId equals s.Id
                          select new
                          {
                              i.Id, i.DisplayNo, i.InvoiceNo, i.InvoiceDate, i.InTimeUtc, i.OutTimeUtc, i.Trips, i.AmountPaise,
                              i.AllocatedPaise, i.Status, i.PaymentMode, SupplierId = s.Id, Supplier = s.Name, VehicleId = v.Id, v.Code, v.VehicleNo,
                          }).ToListAsync();

        var suppliers = rows.GroupBy(r => (r.SupplierId, r.Supplier)).OrderBy(g => g.Key.Supplier, StringComparer.OrdinalIgnoreCase)
            .Select(sg => new TripSupplierGroup(sg.Key.SupplierId, sg.Key.Supplier,
                sg.GroupBy(r => (r.VehicleId, r.Code, r.VehicleNo)).OrderBy(g => g.Key.Code.Length).ThenBy(g => g.Key.Code)
                  .Select(vg => new TripVehicleGroup(vg.Key.VehicleId, vg.Key.Code, vg.Key.VehicleNo,
                      vg.OrderBy(r => r.InvoiceDate).ThenBy(r => r.InvoiceNo).Select((r, n) =>
                      {
                          // A cash invoice is paid at billing; a credit one by the receipts allocated to it.
                          var received = r.PaymentMode == InvoicePaymentMode.Cash ? r.AmountPaise : r.AllocatedPaise;
                          return new TripLine(n + 1, r.Id, r.DisplayNo, r.InvoiceDate, r.VehicleNo, r.InTimeUtc, r.OutTimeUtc, r.Trips,
                              r.AmountPaise, received, PayStates.For(r.Status, r.PaymentMode, r.AmountPaise, r.AllocatedPaise));
                      }).ToList())).ToList()))
            .ToList();

        var period = $"{IndianTime.FormatDate(f.From)} to {IndianTime.FormatDate(f.To)}";
        if (f.VehicleId is { } vid)
        {
            var v = await db.Vehicles.Where(x => x.Id == vid).Select(x => new { x.Code, x.VehicleNo, Supplier = x.Supplier!.Name }).SingleOrDefaultAsync();
            return new TripReport(TripReportShape.OneVehicle, v is null ? "Vehicle" : $"{v.Code} · {v.VehicleNo}",
                $"{v?.Supplier} · {period}", f, suppliers);
        }
        if (f.SupplierId is { } sid)
        {
            var name = await db.Suppliers.Where(x => x.Id == sid).Select(x => x.Name).SingleOrDefaultAsync() ?? "Supplier";
            var count = suppliers.Sum(s => s.Vehicles.Count);
            return new TripReport(TripReportShape.OneSupplier, name, $"{period} · vehicle-wise · {count} vehicle{(count == 1 ? "" : "s")}", f, suppliers);
        }
        var vehicles = suppliers.Sum(s => s.Vehicles.Count);
        return new TripReport(TripReportShape.AllSuppliers, "All suppliers · all vehicles",
            $"{period} · {suppliers.Count} supplier{(suppliers.Count == 1 ? "" : "s")} · {vehicles} vehicle{(vehicles == 1 ? "" : "s")}", f, suppliers);
    }

    // The same report as one table, for the PDF.
    public static ReportTable ToTable(TripReport r)
    {
        var subtitle = new List<string> { r.Title, r.Subtitle, "Cancelled invoices are not counted." };
        var rows = new List<IReadOnlyList<string>>();
        string T(DateTime? utc) => utc is { } t ? IndianTime.FormatTime(t) : "";
        string M(long p) => Money.Format(p);

        if (r.Shape == TripReportShape.AllSuppliers)
        {
            foreach (var s in r.Suppliers)
            {
                rows.Add([s.Name, "", "", "", "", ""]);
                foreach (var v in s.Vehicles)
                    rows.Add([v.Code, v.VehicleNo, v.Trips.ToString(), M(v.BillPaise), M(v.ReceivedPaise), M(v.OutstandingPaise)]);
                rows.Add(["Supplier subtotal", "", s.Trips.ToString(), M(s.BillPaise), M(s.ReceivedPaise), M(s.OutstandingPaise)]);
            }
            return new ReportTable(Name, subtitle,
                [new("Vehicle", Width: 2f), new("Vehicle No.", Width: 1.6f), new("Trips", true, 0.7f), new("Total bill (₹)", true), new("Received (₹)", true), new("Outstanding (₹)", true)],
                rows,
                [["Grand total", "", r.Trips.ToString(), M(r.BillPaise), M(r.ReceivedPaise), M(r.OutstandingPaise)]]);
        }

        foreach (var v in r.Suppliers.SelectMany(s => s.Vehicles))
        {
            if (r.Shape == TripReportShape.OneSupplier) rows.Add(["", $"{v.Code} · {v.VehicleNo}", "", "", "", "", "", "", ""]);
            foreach (var l in v.Lines)
                rows.Add([l.SNo.ToString(), l.InvoiceNo, IndianTime.FormatDate(l.Date), l.VehicleNo, T(l.InTimeUtc), T(l.OutTimeUtc), l.Trips.ToString(), M(l.AmountPaise), PayStates.Text(l.State)]);
            if (r.Shape == TripReportShape.OneSupplier)
                rows.Add(["", "", "", "", "", "Vehicle subtotal", v.Trips.ToString(), M(v.BillPaise), ""]);
        }
        return new ReportTable(Name, subtitle,
            [new("S.No.", Width: 0.6f), new("Invoice No."), new("Date"), new("Vehicle No.", Width: 1.4f), new("In Time"), new("Out Time"),
             new("Trips", true, 0.6f), new("Trip Cost (₹)", true), new("Status")],
            rows,
            [
                ["", "", "", "", "", "Total", r.Trips.ToString(), M(r.BillPaise), ""],
                ["", "", "", "", "", "Received", "", M(r.ReceivedPaise), ""],
                ["", "", "", "", "", "Outstanding", "", M(r.OutstandingPaise), ""],
            ], Landscape: true);
    }
}
