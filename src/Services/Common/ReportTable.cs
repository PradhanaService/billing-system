namespace WaterBilling.Services.Common;

// A printable table: statements and every report turn their data into this,
// and one PDF writer (Components/Pdf/ReportPdf) prints any of them the same way.
public sealed record ReportColumn(string Title, bool Numeric = false, float Width = 1);

public sealed record ReportTable(
    string Title,
    IReadOnlyList<string> SubtitleLines,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<IReadOnlyList<string>> FooterRows,
    bool Landscape = false);
