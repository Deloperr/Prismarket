using ClosedXML.Excel;
using Prismarket.Application.Features.Reports;

namespace Prismarket.Infrastructure.Reports;

public sealed class ExcelReportRenderer : IReportRenderer
{
    public ReportFormat Format => ReportFormat.Excel;

    public ReportFile Render(SalesReportData data)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Сводка");
        summary.Cell(1, 1).Value = $"Prismarket — отчёт о продажах за {ReportText.Period(data)}";
        summary.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        var kpis = new (string, XLCellValue)[]
        {
            ("Выручка, BYN", data.Revenue), ("Заказов", data.Orders), ("Продано товаров", data.ItemsSold),
            ("Средний чек, BYN", data.AverageCheck), ("Кэшбэк, BYN", data.Cashback)
        };
        for (var i = 0; i < kpis.Length; i++)
        {
            summary.Cell(3 + i, 1).Value = kpis[i].Item1;
            summary.Cell(3 + i, 2).Value = kpis[i].Item2;
        }
        summary.Columns().AdjustToContents();

        AddTable(workbook, "По дням", ["Дата", "Заказы", "Товары", "Выручка"],
            data.Daily.Select(d => new XLCellValue[] { d.Day.ToDateTime(TimeOnly.MinValue), d.Orders, d.Items, d.Revenue }));
        AddTable(workbook, "Топ игр", ["Игра", "Продано", "Выручка"],
            data.TopGames.Select(g => new XLCellValue[] { g.Title, g.Sold, g.Revenue }));
        AddTable(workbook, "Оплата", ["Способ", "Заказы", "Выручка"],
            data.PaymentMethods.Select(m => new XLCellValue[] { m.Method, m.Orders, m.Revenue }));
        AddTable(workbook, "Заказы", ["Номер", "Покупатель", "Сумма", "Статус", "Оплата", "Создан"],
            data.OrderRows.Select(o => new XLCellValue[] { o.Number, o.Username, o.Total, o.Status, o.Method, o.CreatedAt }));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ReportFile($"sales-{ReportText.Stamp(data)}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", stream.ToArray());
    }

    private static void AddTable(XLWorkbook workbook, string name, string[] headers, IEnumerable<XLCellValue[]> rows)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++) sheet.Cell(r, c + 1).Value = row[c];
            r++;
        }
        var range = sheet.Range(1, 1, Math.Max(2, r - 1), headers.Length);
        range.CreateTable().Theme = XLTableTheme.TableStyleMedium2;
        sheet.Columns().AdjustToContents();
    }
}
