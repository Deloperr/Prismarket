using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Prismarket.Application.Features.Reports;

namespace Prismarket.Infrastructure.Reports;

public sealed class WordReportRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Word;

    public ReportFile Render(SalesReportData data)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);

            body.Append(Para("Prismarket", bold: true, size: 36));
            body.Append(Para($"Отчёт о продажах за {ReportText.Period(data)}", bold: true, size: 28));
            body.Append(Para($"Выручка: {ReportText.Money(data.Revenue)}"));
            body.Append(Para($"Заказов: {data.Orders}; продано товаров: {data.ItemsSold}"));
            body.Append(Para($"Средний чек: {ReportText.Money(data.AverageCheck)}; кэшбэк: {ReportText.Money(data.Cashback)}"));

            body.Append(Para("Топ игр", bold: true, size: 26));
            body.Append(BuildTable(["Игра", "Продано", "Выручка"],
                data.TopGames.Select(g => new[] { g.Title, g.Sold.ToString(), ReportText.Money(g.Revenue) })));

            body.Append(Para("Продажи по дням", bold: true, size: 26));
            body.Append(BuildTable(["Дата", "Заказы", "Товары", "Выручка"],
                data.Daily.Select(d => new[] { d.Day.ToString("dd.MM.yyyy"), d.Orders.ToString(), d.Items.ToString(), ReportText.Money(d.Revenue) })));
            main.Document.Save();
        }
        return new ReportFile($"sales-{ReportText.Stamp(data)}.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", stream.ToArray());
    }

    private static Paragraph Para(string text, bool bold = false, int size = 22)
    {
        var props = new RunProperties(new FontSize { Val = size.ToString() });
        if (bold) props.Append(new Bold());
        return new Paragraph(new Run(props, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table BuildTable(string[] headers, IEnumerable<string[]> rows)
    {
        var table = new Table(new TableProperties(new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4 }, new BottomBorder { Val = BorderValues.Single, Size = 4 },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
            new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 },
            new LeftBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 })));
        table.Append(new TableRow(headers.Select(h => new TableCell(Para(h, bold: true)))));
        foreach (var row in rows) table.Append(new TableRow(row.Select(v => new TableCell(Para(v)))));
        return table;
    }
}
