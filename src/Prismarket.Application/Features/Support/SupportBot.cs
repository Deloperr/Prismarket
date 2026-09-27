using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Support;

public sealed record BotAnswer(TicketCategory Category, TicketPriority Priority, string? Reply);

public interface ISupportBot
{
    BotAnswer Analyze(string subject, string message);
}

/// <summary>
/// Keyword-based assistant: classifies a new ticket and, when it recognises a typical question,
/// answers immediately so the customer does not have to wait for a human.
/// </summary>
public sealed class KeywordSupportBot : ISupportBot
{
    private sealed record Rule(TicketCategory Category, TicketPriority Priority, string[] Keywords, string Reply);

    private static readonly Rule[] Rules =
    [
        new(TicketCategory.Refund, TicketPriority.High,
            ["возврат", "вернуть деньги", "refund", "верните"],
            "Мы получили запрос на возврат. Возврат возможен, если ключ ещё не был активирован. " +
            "Администратор проверит статус ключа и ответит в ближайшее время. Пожалуйста, укажите номер заказа, если не сделали этого."),
        new(TicketCategory.Payment, TicketPriority.High,
            ["оплат", "списал", "деньги", "ерип", "карт", "платеж", "платёж"],
            "Если деньги списались, а заказ не выполнен — не переживайте: оплата, поступившая после отмены заказа, " +
            "автоматически зачисляется на ваш баланс Prismarket. Проверьте вкладку «Баланс» в профиле. " +
            "Если средств там нет — пришлите номер заказа и время оплаты."),
        new(TicketCategory.KeyActivation, TicketPriority.Normal,
            ["ключ", "активир", "не работает", "invalid", "код"],
            "Все купленные ключи находятся в профиле → «Библиотека». Для активации в Steam: «Игры» → «Активировать в Steam»; " +
            "в Epic Games: аватар → «Активировать код». Убедитесь, что ключ вводится для нужной платформы и региона. " +
            "Если ошибка сохраняется — пришлите скриншот, мы заменим ключ."),
        new(TicketCategory.Account, TicketPriority.Normal,
            ["пароль", "войти", "вход", "2fa", "двухфактор", "почт", "email", "e-mail"],
            "Восстановить пароль можно на странице входа → «Забыли пароль?». Если вы потеряли доступ к приложению 2FA, " +
            "напишите здесь e-mail аккаунта — администратор поможет после проверки владельца.")
    ];

    public BotAnswer Analyze(string subject, string message)
    {
        var text = $"{subject} {message}".ToLowerInvariant();
        var rule = Rules.FirstOrDefault(r => r.Keywords.Any(text.Contains));
        return rule is null
            ? new BotAnswer(TicketCategory.General, TicketPriority.Normal, null)
            : new BotAnswer(rule.Category, rule.Priority, rule.Reply);
    }
}
