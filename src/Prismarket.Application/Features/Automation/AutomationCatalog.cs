using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Automation;

public static class AutomationKeys
{
    // Scheduled (CRON) jobs
    public const string OrderExpiry = "order-expiry";
    public const string EmailOutbox = "email-outbox";
    public const string PromotionsScheduler = "promotions-scheduler";
    public const string StockMonitor = "stock-monitor";
    public const string WishlistPriceAlerts = "wishlist-price-alerts";
    public const string AbandonedCart = "abandoned-cart";
    public const string CurrencyRates = "currency-rates";
    public const string SalesReport = "sales-report";
    public const string SupportAutoClose = "support-autoclose";
    public const string FeaturedRotation = "featured-rotation";
    public const string WinBack = "win-back";
    public const string Cleanup = "cleanup";
    public const string DatabaseBackup = "db-backup";

    // Event-driven rules
    public const string KeyDelivery = "key-delivery";
    public const string Cashback = "cashback";
    public const string ReviewModeration = "review-moderation";
    public const string SupportBot = "support-bot";
}

public sealed record SettingDefinition(string Key, string Label, string Type, object DefaultValue, string? Hint = null);

public sealed record AutomationDefinition(
    string Key,
    string Name,
    string Description,
    AutomationKind Kind,
    string? DefaultCron,
    IReadOnlyList<SettingDefinition> Settings);

/// <summary>
/// Single source of truth for every automation in the system. On startup definitions are synced
/// to the <c>automation_jobs</c> table, where admins can enable/disable them, change CRON and settings.
/// </summary>
public static class AutomationCatalog
{
    public static readonly IReadOnlyList<AutomationDefinition> All =
    [
        new(AutomationKeys.OrderExpiry, "Отмена неоплаченных заказов",
            "Отменяет заказы, не оплаченные за отведённое время, и возвращает зарезервированные ключи в продажу.",
            AutomationKind.Scheduled, "* * * * *",
            [new("reservationMinutes", "Время на оплату, мин", "int", 15)]),

        new(AutomationKeys.EmailOutbox, "Отправка писем из очереди",
            "Transactional outbox: отправляет письма пачками параллельно, при ошибке повторяет с экспоненциальной задержкой.",
            AutomationKind.Scheduled, "* * * * *",
            [
                new("batchSize", "Писем за запуск", "int", 50),
                new("maxAttempts", "Максимум попыток", "int", 5),
                new("parallelism", "Параллельных отправок", "int", 4)
            ]),

        new(AutomationKeys.PromotionsScheduler, "Планировщик распродаж",
            "Автоматически включает скидки в момент начала акции и возвращает прежние цены после её окончания.",
            AutomationKind.Scheduled, "* * * * *", []),

        new(AutomationKeys.StockMonitor, "Контроль остатков ключей",
            "Следит за количеством ключей: предупреждает админов о низком остатке, скрывает товар при нуле и возвращает его в продажу после пополнения (с уведомлением подписчиков вишлиста).",
            AutomationKind.Scheduled, "*/15 * * * *",
            [
                new("lowStockThreshold", "Порог низкого остатка", "int", 5),
                new("autoHide", "Скрывать товар при нуле", "bool", true),
                new("notifyWishlist", "Сообщать о поступлении", "bool", true)
            ]),

        new(AutomationKeys.WishlistPriceAlerts, "Снижение цен в вишлисте",
            "Сравнивает текущие цены с ценами на момент добавления в список желаемого и уведомляет о снижении.",
            AutomationKind.Scheduled, "0 * * * *",
            [new("minDropPercent", "Минимальное снижение, %", "int", 5)]),

        new(AutomationKeys.AbandonedCart, "Брошенные корзины",
            "Напоминает пользователю о товарах, которые давно лежат в корзине.",
            AutomationKind.Scheduled, "0 */2 * * *",
            [new("idleHours", "Через сколько часов напоминать", "int", 24)]),

        new(AutomationKeys.CurrencyRates, "Обновление курсов валют",
            "Загружает официальные курсы НБ РБ (api.nbrb.by) для отображения цен в USD/EUR/RUB.",
            AutomationKind.Scheduled, "5 */6 * * *",
            [new("currencies", "Валюты", "string", "USD,EUR,RUB")]),

        new(AutomationKeys.SalesReport, "Отчёт о продажах на почту",
            "Формирует отчёт о продажах за период (PDF и/или Excel) и отправляет всем администраторам.",
            AutomationKind.Scheduled, "0 8 * * *",
            [
                new("periodDays", "Период, дней", "int", 1),
                new("format", "Формат (pdf/excel/both)", "string", "both")
            ]),

        new(AutomationKeys.SupportAutoClose, "Автозакрытие обращений",
            "Закрывает обращения в поддержку, по которым пользователь не отвечает дольше заданного времени.",
            AutomationKind.Scheduled, "0 * * * *",
            [new("inactiveHours", "Часов без ответа", "int", 72)]),

        new(AutomationKeys.FeaturedRotation, "Ротация витрины и рейтингов",
            "Пересчитывает рейтинги игр и выводит на главную самые продаваемые игры за период.",
            AutomationKind.Scheduled, "0 3 * * *",
            [
                new("topCount", "Игр на витрине", "int", 6),
                new("periodDays", "Период продаж, дней", "int", 7)
            ]),

        new(AutomationKeys.WinBack, "Возврат неактивных пользователей",
            "Генерирует персональный промокод и отправляет его тем, кто давно не заходил.",
            AutomationKind.Scheduled, "0 10 * * 1",
            [
                new("inactiveDays", "Неактивен дней", "int", 30),
                new("discountPercent", "Скидка, %", "int", 10),
                new("validDays", "Срок действия, дней", "int", 14)
            ]),

        new(AutomationKeys.Cleanup, "Очистка устаревших данных",
            "Удаляет истёкшие refresh-токены, старые уведомления, журналы и историю запусков.",
            AutomationKind.Scheduled, "30 3 * * *",
            [
                new("auditRetentionDays", "Хранить аудит, дней", "int", 180),
                new("notificationRetentionDays", "Хранить уведомления, дней", "int", 60),
                new("runRetentionDays", "Хранить историю запусков, дней", "int", 30)
            ]),

        new(AutomationKeys.DatabaseBackup, "Резервное копирование БД",
            "Создаёт дамп PostgreSQL (pg_dump) и хранит последние N копий.",
            AutomationKind.Scheduled, "0 4 * * *",
            [new("keepLast", "Хранить копий", "int", 7)]),

        new(AutomationKeys.KeyDelivery, "Мгновенная выдача ключей",
            "После оплаты заказа ключи автоматически попадают в библиотеку и отправляются на e-mail.",
            AutomationKind.EventDriven, null,
            [new("sendEmail", "Отправлять ключи на почту", "bool", true)]),

        new(AutomationKeys.Cashback, "Кэшбэк на баланс",
            "Начисляет процент от суммы выполненного заказа на внутренний баланс покупателя.",
            AutomationKind.EventDriven, null,
            [new("percent", "Кэшбэк, %", "decimal", 3m)]),

        new(AutomationKeys.ReviewModeration, "Автомодерация отзывов",
            "Проверяет отзывы на запрещённые слова и ссылки; подозрительные отправляет на ручную модерацию.",
            AutomationKind.EventDriven, null,
            [
                new("bannedWords", "Стоп-слова (через запятую)", "string", "казино,скам,обман,http,t.me"),
                new("minLength", "Мин. длина комментария", "int", 0)
            ]),

        new(AutomationKeys.SupportBot, "Бот поддержки",
            "Отвечает на типовые вопросы по ключевым словам, определяет категорию и назначает наименее загруженного администратора.",
            AutomationKind.EventDriven, null,
            [
                new("autoReply", "Автоответ", "bool", true),
                new("autoAssign", "Автоназначение", "bool", true)
            ])
    ];

    public static AutomationDefinition Get(string key) =>
        All.FirstOrDefault(d => d.Key == key) ?? throw new KeyNotFoundException($"Unknown automation '{key}'.");
}
