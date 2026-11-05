using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WaterBilling.Services.Common;

namespace WaterBilling.Components.Pdf;

// Prints any ReportTable: statements and all reports share this one layout.
public static class ReportPdf
{
    public static byte[] Generate(ReportTable report, CompanyInfo company, DateTime generatedUtc) =>
        Document.Create(doc => doc.Page(page =>
        {
            PdfStyle.Page(page);
            if (report.Landscape) page.Size(PageSizes.A4.Landscape());
            page.Header().Element(c => PdfStyle.Header(c, company, report.Title));

            page.Content().PaddingTop(6).Column(col =>
            {
                foreach (var line in report.SubtitleLines) col.Item().Text(line).Bold();
                col.Item().PaddingTop(6).Table(t =>
                {
                    t.ColumnsDefinition(c => { foreach (var column in report.Columns) c.RelativeColumn(column.Width); });
                    t.Header(h =>
                    {
                        foreach (var column in report.Columns)
                        {
                            var cell = h.Cell().Element(PdfStyle.HeaderCell);
                            (column.Numeric ? cell.AlignRight() : cell).Text(column.Title).Bold();
                        }
                    });
                    foreach (var row in report.Rows) AddRow(t, report.Columns, row, bold: false);
                    foreach (var row in report.FooterRows) AddRow(t, report.Columns, row, bold: true);
                });
                if (report.Rows.Count == 0) col.Item().PaddingTop(8).Text("No entries for this selection.").Italic();
            });

            page.Footer().Element(c => PdfStyle.Footer(c, generatedUtc));
        })).GeneratePdf();

    private static void AddRow(TableDescriptor t, IReadOnlyList<ReportColumn> columns, IReadOnlyList<string> row, bool bold)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var cell = t.Cell().Element(bold ? PdfStyle.HeaderCell : PdfStyle.Cell);
            var text = (columns[i].Numeric ? cell.AlignRight() : cell).Text(i < row.Count ? row[i] : "");
            if (bold) text.Bold();
        }
    }
}
