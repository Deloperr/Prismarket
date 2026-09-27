using System.Globalization;
using Prismarket.Application.Features.Reports;

namespace Prismarket.Infrastructure.Reports;

internal static class ReportText
{
    public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    public static string Money(decimal v) => v.ToString("N2", Ru) + " BYN";
    public static string Period(SalesReportData d) => $"{d.From:dd.MM.yyyy} — {d.To.AddDays(-1):dd.MM.yyyy}";
    public static string Stamp(SalesReportData d) => $"{d.From:yyyyMMdd}-{d.To.AddDays(-1):yyyyMMdd}";
}
