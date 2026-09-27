using System.Net;

namespace Prismarket.Application.Common;

/// <summary>Builds branded HTML e-mails. Kept deliberately simple: one layout + small helpers.</summary>
public static class EmailTemplates
{
    public static string Layout(string title, string bodyHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var button = buttonText is null || buttonUrl is null
            ? string.Empty
            : $"""
               <p style="text-align:center;margin:28px 0 8px">
                 <a href="{buttonUrl}" style="display:inline-block;padding:12px 28px;border-radius:999px;
                    background:linear-gradient(135deg,#7c8cff,#b18cff);color:#fff;text-decoration:none;font-weight:600">
                    {Encode(buttonText)}</a>
               </p>
               """;

        return $"""
                <!doctype html>
                <html lang="ru"><body style="margin:0;padding:32px 12px;background:#0b0d1a;font-family:Segoe UI,Roboto,Arial,sans-serif">
                  <div style="max-width:560px;margin:0 auto;border-radius:20px;padding:32px;
                              background:linear-gradient(160deg,rgba(124,140,255,.18),rgba(177,140,255,.08));
                              border:1px solid rgba(255,255,255,.14);color:#e8eaff">
                    <div style="font-size:20px;font-weight:700;letter-spacing:.4px;margin-bottom:20px">◆ Prismarket</div>
                    <h1 style="font-size:22px;margin:0 0 16px;color:#fff">{Encode(title)}</h1>
                    <div style="font-size:15px;line-height:1.6;color:#c9cdf2">{bodyHtml}</div>
                    {button}
                    <hr style="border:none;border-top:1px solid rgba(255,255,255,.12);margin:28px 0 12px">
                    <div style="font-size:12px;color:#8a8fb8">Это автоматическое письмо от Prismarket. Отвечать на него не нужно.</div>
                  </div>
                </body></html>
                """;
    }

    public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    public static string Table(IEnumerable<(string Left, string Right)> rows)
    {
        var body = string.Join("", rows.Select(r =>
            $"<tr><td style=\"padding:8px 0;border-bottom:1px solid rgba(255,255,255,.08)\">{r.Left}</td>" +
            $"<td style=\"padding:8px 0;border-bottom:1px solid rgba(255,255,255,.08);text-align:right\">{r.Right}</td></tr>"));
        return $"<table style=\"width:100%;border-collapse:collapse;color:#e8eaff\">{body}</table>";
    }

    public static string Money(decimal amount) => $"{amount:0.00} BYN";
}
