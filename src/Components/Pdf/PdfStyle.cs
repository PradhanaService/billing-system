using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WaterBilling.Services.Common;

namespace WaterBilling.Components.Pdf;

// Shared look for every PDF: black and white, A4, the same header and footer.
public static class PdfStyle
{
    public static void Page(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(28);
        page.DefaultTextStyle(t => t.FontSize(9.5f).FontFamily(Fonts.Lato).FontColor(Colors.Black));
    }

    public static void Header(IContainer container, CompanyInfo company, string title, string? subtitle = null)
    {
        container.PaddingBottom(10).BorderBottom(1.5f).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(company.Name).FontSize(15).Bold();
                if (!string.IsNullOrWhiteSpace(company.Address)) col.Item().Text(company.Address);
                if (!string.IsNullOrWhiteSpace(company.Phone)) col.Item().Text($"Phone: {company.Phone}");
            });
            row.ConstantItem(200).AlignRight().Column(col =>
            {
                col.Item().AlignRight().Text(title).FontSize(13).Bold();
                if (subtitle is not null) col.Item().AlignRight().Text(subtitle);
            });
        });
    }

    public static void Footer(IContainer container, DateTime generatedUtc)
    {
        container.BorderTop(0.5f).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text($"Computer generated on {IndianTime.FormatDateTime(generatedUtc)}").FontSize(8);
            row.ConstantItem(100).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    }

    public static IContainer HeaderCell(IContainer c) => c.Border(0.75f).Background(Colors.Grey.Lighten3).Padding(4);
    public static IContainer Cell(IContainer c) => c.BorderHorizontal(0.5f).BorderVertical(0.75f).Padding(4);
}
