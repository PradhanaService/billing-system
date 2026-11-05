using QuestPDF.Fluent;
using QuestPDF.Helpers;
using WaterBilling.Data.Entities;
using WaterBilling.Services.Billing;
using WaterBilling.Services.Common;

namespace WaterBilling.Components.Pdf;

// The printed invoice (FR-INV-14). Black and white, A4.
public static class InvoicePdf
{
    public static byte[] Generate(InvoiceDetail inv, CompanyInfo company, DateTime generatedUtc) =>
        Document.Create(doc => doc.Page(page =>
        {
            PdfStyle.Page(page);
            page.Header().Element(c => PdfStyle.Header(c, company, "INVOICE", inv.DisplayNo));

            page.Content().PaddingTop(8).Column(col =>
            {
                col.Spacing(8);
                if (inv.Status == InvoiceStatus.Cancelled)
                {
                    col.Item().Border(2).Padding(6).AlignCenter()
                        .Text($"CANCELLED - {inv.CancelReason} (reversal {inv.ReversalNo})").Bold().FontSize(12);
                }

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(left =>
                    {
                        left.Item().Text("Bill To").Bold();
                        left.Item().Text(inv.BillToName).FontSize(11).Bold();
                        if (!string.IsNullOrWhiteSpace(inv.BillToAddress)) left.Item().Text(inv.BillToAddress);
                        if (!string.IsNullOrWhiteSpace(inv.BillToPhone)) left.Item().Text($"Phone: {inv.BillToPhone}");
                    });
                    row.ConstantItem(230).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.ConstantColumn(95); c.RelativeColumn(); });
                        void Pair(string k, string v) { t.Cell().Text(k).Bold(); t.Cell().Text(v); }
                        Pair("Invoice No.", inv.DisplayNo);
                        Pair("Invoice Date", IndianTime.FormatDate(inv.InvoiceDate));
                        Pair("Vehicle No.", $"{inv.VehicleCode} / {inv.VehicleNo}");
                        Pair("Vehicle Name", inv.VehicleName);
                        Pair("Supplier", inv.SupplierName);
                        Pair("Payment Mode", inv.PaymentMode.ToString());
                    });
                });

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(35); c.RelativeColumn(2.6f); c.RelativeColumn(0.8f); c.RelativeColumn(1.3f); c.RelativeColumn(1.2f); c.RelativeColumn(1.6f);
                    });
                    t.Header(h =>
                    {
                        foreach (var title in new[] { "S.No", "Material", "Qty", "Measurement", "Rate (Rs.)", "Total Amount (Rs.)" })
                            h.Cell().Element(PdfStyle.HeaderCell).Text(title).Bold();
                    });
                    t.Cell().Element(PdfStyle.Cell).Text("1");
                    t.Cell().Element(PdfStyle.Cell).Text(inv.Material);
                    t.Cell().Element(PdfStyle.Cell).AlignRight().Text(inv.Quantity);
                    t.Cell().Element(PdfStyle.Cell).Text(inv.Measurement);
                    t.Cell().Element(PdfStyle.Cell).AlignRight().Text(Money.Format(inv.RatePaise));
                    t.Cell().Element(PdfStyle.Cell).AlignRight().Text(Money.Format(inv.AmountPaise));
                    t.Cell().ColumnSpan(5).Element(PdfStyle.HeaderCell).AlignRight().Text("Total").Bold();
                    t.Cell().Element(PdfStyle.HeaderCell).AlignRight().Text(Money.Format(inv.AmountPaise)).Bold();
                });

                col.Item().Text($"Amount in words: {AmountInWords.Format(inv.AmountPaise)}").Italic();
                if (inv.PaymentMode == InvoicePaymentMode.Cash)
                    col.Item().Text("Paid in cash.").Bold();
                if (!string.IsNullOrWhiteSpace(inv.Narration))
                    col.Item().Text($"Narration: {inv.Narration}");
                col.Item().PaddingTop(30).Row(row =>
                {
                    row.RelativeItem().Text($"Prepared by: {inv.CreatedBy}");
                    row.RelativeItem().AlignRight().Text($"For {company.Name}").Bold();
                });
                col.Item().PaddingTop(25).AlignRight().Text("Authorised signatory");
            });

            page.Footer().Element(c => PdfStyle.Footer(c, generatedUtc));
        })).GeneratePdf();
}
