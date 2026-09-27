using Prismarket.Application.Features.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Prismarket.Infrastructure.Reports;

public sealed class PdfReportRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Pdf;

    public ReportFile Render(SalesReportData data)
    {
        var accent = Color.FromHex("#6F7CFF");
        var bytes = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontFamily("DejaVu Sans", "Arial"));

            page.Header().Column(col =>
            {
                col.Item().Text("◆ Prismarket").FontSize(18).Bold().FontColor(accent);
                col.Item().Text($"Отчёт о продажах за {ReportText.Period(data)}").FontSize(13).SemiBold();
                col.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });

            page.Content().PaddingVertical(12).Column(col =>
            {
                col.Spacing(14);
                col.Item().Row(row =>
                {
                    Kpi(row, "Выручка", ReportText.Money(data.Revenue));
                    Kpi(row, "Заказов", data.Orders.ToString(ReportText.Ru));
                    Kpi(row, "Продано ключей", data.ItemsSold.ToString(ReportText.Ru));
                    Kpi(row, "Средний чек", ReportText.Money(data.AverageCheck));
                });

                col.Item().Text("Продажи по дням").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(2); });
                    Header(t, "Дата", "Заказы", "Товары", "Выручка");
                    foreach (var d in data.Daily)
                        Row(t, d.Day.ToString("dd.MM.yyyy", ReportText.Ru), d.Orders.ToString(), d.Items.ToString(),
                            ReportText.Money(d.Revenue));
                });

                col.Item().Text("Топ игр").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(); c.RelativeColumn(2); });
                    Header(t, "Игра", "Продано", "Выручка");
                    foreach (var g in data.TopGames) Row(t, g.Title, g.Sold.ToString(), ReportText.Money(g.Revenue));
                });

                col.Item().Text("Способы оплаты").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(2); });
                    Header(t, "Способ", "Заказы", "Выручка");
                    foreach (var m in data.PaymentMethods) Row(t, m.Method, m.Orders.ToString(), ReportText.Money(m.Revenue));
                });

                col.Item().Text($"Начислено кэшбэка: {ReportText.Money(data.Cashback)}").Italic();
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span($"Сформировано автоматически {DateTime.UtcNow:dd.MM.yyyy HH:mm} UTC · стр. ");
                t.CurrentPageNumber();
            });
        })).GeneratePdf();

        return new ReportFile($"sales-{ReportText.Stamp(data)}.pdf", "application/pdf", bytes);

        static void Kpi(QuestPDF.Fluent.RowDescriptor row, string title, string value) =>
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(c =>
            {
                c.Item().Text(title).FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().Text(value).FontSize(12).Bold();
            });

        static void Header(TableDescriptor t, params string[] titles)
        {
            foreach (var title in titles)
                t.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(title).SemiBold();
        }

        static void Row(TableDescriptor t, params string[] values)
        {
            foreach (var value in values)
                t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(value);
        }
    }
}
