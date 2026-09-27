using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Admin;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Infrastructure.Persistence;

/// <summary>Fills an empty database with a realistic demo catalogue, users and 45 days of sales history.</summary>
public sealed class DataSeeder(AppDbContext db, IPasswordHasher hasher, TimeProvider clock, ILogger<DataSeeder> logger)
{
    private readonly Random _random = new(42);

    private sealed record GameSeed(
        string Title, int SteamAppId, string Developer, string Publisher, string Platform, string[] Genres,
        string Short, string Description, DateOnly Release, string Age, decimal Price, int Discount = 0,
        string? DeluxeName = null, decimal? DeluxePrice = null, int Keys = 25);

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedReferenceDataAsync(ct);
        if (await db.Users.AnyAsync(ct)) return;

        logger.LogInformation("Seeding demo data...");
        var now = clock.GetUtcNow().UtcDateTime;

        var platforms = new[]
        {
            new Platform { Name = "Steam", Slug = "steam", Website = "https://store.steampowered.com" },
            new Platform { Name = "Epic Games", Slug = "epic-games", Website = "https://store.epicgames.com" },
            new Platform { Name = "GOG", Slug = "gog", Website = "https://www.gog.com" },
            new Platform { Name = "Xbox", Slug = "xbox", Website = "https://www.xbox.com" },
            new Platform { Name = "PlayStation", Slug = "playstation", Website = "https://store.playstation.com" }
        }.ToDictionary(p => p.Name);
        db.Platforms.AddRange(platforms.Values);

        var genres = new[]
        {
            ("RPG", "rpg"), ("Экшен", "action"), ("Приключения", "adventure"), ("Шутер", "shooter"),
            ("Открытый мир", "open-world"), ("Стратегия", "strategy"), ("Инди", "indie"), ("Гонки", "racing"),
            ("Симулятор", "simulation"), ("Кооператив", "co-op"), ("Souls-like", "souls-like"), ("Хоррор", "horror")
        }.Select(g => new Genre { Name = g.Item1, Slug = g.Item2 }).ToDictionary(g => g.Name);
        db.Genres.AddRange(genres.Values);

        var catalog = Games();
        var developers = catalog.Select(g => g.Developer).Distinct()
            .ToDictionary(n => n, n => new Developer { Name = n });
        var publishers = catalog.Select(g => g.Publisher).Distinct()
            .ToDictionary(n => n, n => new Publisher { Name = n });
        db.Developers.AddRange(developers.Values);
        db.Publishers.AddRange(publishers.Values);

        var games = new List<Game>();
        foreach (var s in catalog)
        {
            var game = new Game
            {
                Title = s.Title,
                Slug = Slug.From(s.Title),
                ShortDescription = s.Short,
                Description = s.Description,
                ReleaseDate = s.Release,
                AgeRating = s.Age,
                CoverImageUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{s.SteamAppId}/library_600x900.jpg",
                HeaderImageUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{s.SteamAppId}/header.jpg",
                SystemRequirements = "ОС: Windows 10/11 64-bit\nПроцессор: Intel Core i5 / AMD Ryzen 5\nПамять: 16 ГБ ОЗУ\nВидеокарта: GTX 1060 6 ГБ / RX 580\nМесто на диске: 70 ГБ SSD",
                Developer = developers[s.Developer],
                Publisher = publishers[s.Publisher],
                Platform = platforms[s.Platform],
                IsAvailable = true
            };
            foreach (var genre in s.Genres) game.Genres.Add(genres[genre]);

            var standard = new Product
            {
                Kind = ProductKind.Key, Edition = "Standard", Price = s.Price, DiscountPercent = s.Discount,
                LowStockThreshold = null
            };
            AddKeys(standard, s.Keys);
            game.Products.Add(standard);

            if (s.DeluxeName is not null)
            {
                var deluxe = new Product { Kind = ProductKind.Key, Edition = s.DeluxeName, Price = s.DeluxePrice!.Value };
                AddKeys(deluxe, Math.Max(2, s.Keys / 3));
                game.Products.Add(deluxe);
            }
            games.Add(game);
        }

        // An account-type product to demonstrate the second product kind.
        var accountProduct = new Product { Kind = ProductKind.Account, Edition = "Аккаунт с игрой", Price = 24.90m };
        for (var i = 1; i <= 6; i++)
            accountProduct.Accounts.Add(new ProductAccount
            {
                Login = $"pm_gta_{i:D3}", Password = $"Gt@{_random.Next(100000, 999999)}", Email = $"pm_gta_{i:D3}@mail.test",
                AdditionalInfo = "Смените пароль и почту сразу после входа"
            });
        games.Single(g => g.Title.StartsWith("Grand Theft Auto")).Products.Add(accountProduct);

        db.Games.AddRange(games);

        var admin = new User
        {
            Username = "admin", Email = "admin@prismarket.local", PasswordHash = hasher.Hash("Admin123!"),
            Role = UserRole.Admin, EmailConfirmed = true, LastLoginAt = now
        };
        var demo = new User
        {
            Username = "demo", Email = "demo@prismarket.local", PasswordHash = hasher.Hash("Demo123!"),
            EmailConfirmed = true, Balance = 150m, LastLoginAt = now
        };
        var names = new[] { "nightowl", "pixel_viking", "kira_s", "mr_frost", "lena.games", "dzmitry", "arcadia",
            "neon_fox", "ivan_k", "polina_art", "sanya228", "gg_wp" };
        var customers = names.Select((n, i) => new User
        {
            Username = n, Email = $"{n.Replace('.', '_')}@mail.test", PasswordHash = admin.PasswordHash,
            EmailConfirmed = true, CreatedAt = now.AddDays(-_random.Next(10, 120)),
            LastLoginAt = now.AddDays(-_random.Next(0, i < 3 ? 60 : 10))
        }).ToList();

        db.Users.AddRange(new[] { admin, demo }.Concat(customers));
        await db.SaveChangesAsync(ct);

        await SeedHistoryAsync(games, [demo, .. customers], now, ct);
        await SeedExtrasAsync(games, customers, admin, demo, now, ct);
        logger.LogInformation("Demo data seeded: {Games} games, {Users} users", games.Count, customers.Count + 2);
    }

    /// <summary>Reference data that must exist in every environment (currency rates).</summary>
    private async Task SeedReferenceDataAsync(CancellationToken ct)
    {
        if (await db.CurrencyRates.AnyAsync(ct)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        db.CurrencyRates.AddRange(
            new CurrencyRate { Code = "USD", RateToByn = 3.27m, UpdatedAt = now, Source = "seed" },
            new CurrencyRate { Code = "EUR", RateToByn = 3.55m, UpdatedAt = now, Source = "seed" },
            new CurrencyRate { Code = "RUB", RateToByn = 0.0385m, UpdatedAt = now, Source = "seed" });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedHistoryAsync(List<Game> games, List<User> customers, DateTime now, CancellationToken ct)
    {
        var products = games.SelectMany(g => g.Products).Where(p => p.Kind == ProductKind.Key).ToList();
        var methods = new[] { PaymentMethod.Card, PaymentMethod.Card, PaymentMethod.Erip, PaymentMethod.Balance };
        var orderNo = 1;

        for (var day = 45; day >= 0; day--)
        {
            var ordersToday = _random.Next(day % 7 is 5 or 6 ? 3 : 1, day % 7 is 5 or 6 ? 8 : 5);
            for (var n = 0; n < ordersToday; n++)
            {
                var user = customers[_random.Next(customers.Count)];
                var created = now.Date.AddDays(-day).AddHours(_random.Next(8, 23)).AddMinutes(_random.Next(60));
                if (created > now) created = now.AddMinutes(-5);
                var method = methods[_random.Next(methods.Length)];

                var order = new Order
                {
                    Number = $"PM-{created:yyMMdd}-{orderNo++:D5}", User = user, PaymentMethod = method,
                    Status = OrderStatus.Completed, CreatedAt = created, ExpiresAt = created.AddMinutes(15),
                    PaidAt = created.AddMinutes(2), CompletedAt = created.AddMinutes(2)
                };

                var count = _random.Next(1, 4);
                foreach (var product in products.OrderBy(_ => _random.Next()).Take(count))
                {
                    // Sold key created specifically for history (does not reduce the demo stock).
                    var key = new ProductKey
                    {
                        Product = product, Value = KeyGenerator.Next(), Status = StockItemStatus.Sold, SoldAt = order.CompletedAt,
                        ReservedByOrderId = null
                    };
                    var item = new OrderItem
                    {
                        Product = product, UnitPrice = product.Price, DiscountPercent = product.DiscountPercent,
                        FinalPrice = Product.ApplyDiscount(product.Price, product.DiscountPercent), Key = key
                    };
                    order.Items.Add(item);
                    db.LibraryItems.Add(new LibraryItem
                    {
                        User = user, Product = product, OrderItem = item, Key = key, PurchasedAt = order.CompletedAt!.Value,
                        IsActivated = _random.NextDouble() < 0.7
                    });
                    product.Game.SalesCount++;
                }

                order.Subtotal = order.Items.Sum(i => i.FinalPrice);
                order.Total = order.Subtotal;
                order.Payments.Add(new Payment
                {
                    User = user, Purpose = PaymentPurpose.Order, Method = method, Status = PaymentStatus.Succeeded,
                    Amount = order.Total, CompletedAt = order.PaidAt, ProviderReference = $"seed-{orderNo}"
                });
                db.Orders.Add(order);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedExtrasAsync(List<Game> games, List<User> customers, User admin, User demo, DateTime now,
        CancellationToken ct)
    {
        var comments = new (int Rating, string Title, string Text)[]
        {
            (5, "Шедевр", "Одна из лучших игр, в которые я играл. Ключ пришёл моментально."),
            (5, "Рекомендую", "Отличная оптимизация, сюжет затягивает с первых минут."),
            (4, "Хорошо, но есть нюансы", "Игра классная, но в начале бывают просадки FPS."),
            (4, "Стоит своих денег", "Особенно со скидкой. Активировал без проблем."),
            (3, "Середнячок", "Ожидал большего, но на пару вечеров хватит."),
            (5, "10/10", "Лучшая покупка года, спасибо магазину за быструю выдачу.")
        };

        foreach (var game in games)
        {
            var reviewers = customers.OrderBy(_ => _random.Next()).Take(_random.Next(2, 6)).ToList();
            foreach (var user in reviewers)
            {
                var c = comments[_random.Next(comments.Length)];
                db.Reviews.Add(new Review
                {
                    User = user, Game = game, Rating = c.Rating, Title = c.Title, Comment = c.Text,
                    IsVerifiedPurchase = true, Status = ReviewStatus.Published
                });
            }
        }

        var siteComments = new[]
        {
            "Быстро, дёшево, без проблем. Буду покупать ещё!", "Ключ пришёл за секунды, поддержка отвечает быстро.",
            "Удобный сайт и приятный дизайн. Понравился кэшбэк.", "Цены ниже, чем в Steam, всё работает.",
            "Отличный магазин, уже третья покупка."
        };
        foreach (var (user, i) in customers.Take(8).Select((u, i) => (u, i)))
            db.SiteReviews.Add(new SiteReview { User = user, Rating = i % 4 == 3 ? 4 : 5, Comment = siteComments[i % siteComments.Length] });

        db.PromoCodes.AddRange(
            new PromoCode { Code = "WELCOME10", DiscountPercent = 10, Source = "admin" },
            new PromoCode { Code = "PRISM20", DiscountPercent = 20, MaxUses = 100, ExpiresAt = now.AddDays(30), Source = "admin" });

        var autumnSale = new Promotion
        {
            Name = "Осенняя распродажа", Description = "Скидки на хиты в открытом мире",
            DiscountPercent = 30, StartsAt = now.AddDays(2), EndsAt = now.AddDays(9)
        };
        foreach (var product in games.Where(g => g.Genres.Any(x => x.Slug == "open-world")).Select(g => g.Products.First()).Take(5))
            autumnSale.Products.Add(new PromotionProduct { Product = product });
        db.Promotions.Add(autumnSale);

        var ticket = new SupportTicket
        {
            User = demo, Subject = "Как активировать ключ в Steam?", Category = TicketCategory.KeyActivation,
            Status = TicketStatus.WaitingForCustomer, AssignedAdmin = admin, LastMessageAt = now.AddHours(-3)
        };
        ticket.Messages.Add(new SupportMessage { Sender = demo, Text = "Купил ключ, куда его вводить?", CreatedAt = now.AddHours(-4) });
        ticket.Messages.Add(new SupportMessage
        {
            IsBot = true, IsFromStaff = true, CreatedAt = now.AddHours(-4).AddSeconds(1),
            Text = "Все купленные ключи находятся в профиле → «Библиотека». В Steam: «Игры» → «Активировать в Steam»."
        });
        ticket.Messages.Add(new SupportMessage { Sender = admin, IsFromStaff = true, Text = "Если что-то не получится — пишите, поможем!", CreatedAt = now.AddHours(-3) });
        db.SupportTickets.Add(ticket);

        db.Notifications.Add(new Notification
        {
            User = demo, Type = NotificationType.Promo, Title = "Добро пожаловать в Prismarket!",
            Message = "Промокод WELCOME10 даёт −10% на первый заказ.", Link = "/catalog", CreatedAt = now
        });

        await db.SaveChangesAsync(ct);

        // Denormalized ratings for the seeded reviews.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE games g SET average_rating = s.avg, ratings_count = s.cnt
            FROM (SELECT game_id, ROUND(AVG(rating)::numeric, 2) AS avg, COUNT(*) AS cnt FROM reviews GROUP BY game_id) s
            WHERE s.game_id = g.id
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE games SET is_featured = TRUE
            WHERE id IN (SELECT id FROM games ORDER BY sales_count DESC LIMIT 6)
            """, ct);
    }

    private void AddKeys(Product product, int count)
    {
        for (var i = 0; i < count; i++) product.Keys.Add(new ProductKey { Value = KeyGenerator.Next() });
    }

    private static List<GameSeed> Games() =>
    [
        new("Cyberpunk 2077", 1091500, "CD PROJEKT RED", "CD PROJEKT RED", "GOG", ["RPG", "Экшен", "Открытый мир"],
            "Приключенческая ролевая игра в открытом мире мегаполиса Найт-Сити.",
            "Станьте кибернаёмником Ви и сражайтесь за выживание в городе, одержимом властью, модой и киберимплантами. Включает дополнение-сюжет с возможностью прокачать персонажа под любой стиль игры.",
            new DateOnly(2020, 12, 10), "18+", 89.90m, 35, "Ultimate Edition", 139.90m),
        new("The Witcher 3: Wild Hunt", 292030, "CD PROJEKT RED", "CD PROJEKT RED", "GOG", ["RPG", "Открытый мир", "Приключения"],
            "Легендарная RPG о ведьмаке Геральте из Ривии.",
            "Охотник на чудовищ ищет дитя предназначения в огромном открытом мире, полном политических интриг и опасных существ. Версия Next-Gen с улучшенной графикой.",
            new DateOnly(2015, 5, 19), "18+", 49.90m, 70, "Complete Edition", 79.90m),
        new("Red Dead Redemption 2", 1174180, "Rockstar Games", "Rockstar Games", "Steam", ["Экшен", "Приключения", "Открытый мир"],
            "Эпическая история о жизни вне закона на Диком Западе.",
            "Америка, 1899 год. Артур Морган и банда Ван дер Линде вынуждены бежать от закона. Огромный живой мир, охота, рыбалка и онлайн-режим Red Dead Online.",
            new DateOnly(2019, 12, 5), "18+", 119.90m, 50),
        new("Grand Theft Auto V", 271590, "Rockstar North", "Rockstar Games", "Steam", ["Экшен", "Открытый мир"],
            "Три преступника и целый Лос-Сантос в вашем распоряжении.",
            "Сюжетная кампания о трёх уголовниках и GTA Online — постоянно развивающийся мир для 30 игроков.",
            new DateOnly(2015, 4, 14), "18+", 59.90m, 40, Keys: 4),
        new("ELDEN RING", 1245620, "FromSoftware", "Bandai Namco", "Steam", ["RPG", "Souls-like", "Открытый мир"],
            "Восстань, погасшая душа, и стань Повелителем Элдена.",
            "Огромный мир Междуземья, созданный Хидэтакой Миядзаки и Джорджем Мартином. Сложные сражения, свобода исследования и множество билдов.",
            new DateOnly(2022, 2, 25), "16+", 139.90m, 0, "Shadow of the Erdtree Edition", 219.90m),
        new("Baldur's Gate 3", 1086940, "Larian Studios", "Larian Studios", "Steam", ["RPG", "Стратегия", "Кооператив"],
            "Соберите отряд и вернитесь в Забытые Королевства.",
            "Ролевая игра нового поколения по вселенной Dungeons & Dragons: пошаговые бои, огромная свобода выбора и кооператив до 4 игроков.",
            new DateOnly(2023, 8, 3), "18+", 149.90m, 15),
        new("Hogwarts Legacy", 990080, "Avalanche Software", "Warner Bros. Games", "Steam", ["RPG", "Приключения", "Открытый мир"],
            "Проживите свою историю в мире волшебства XIX века.",
            "Поступите в Хогвартс, изучайте заклинания, варите зелья и раскройте древнюю тайну волшебного мира.",
            new DateOnly(2023, 2, 10), "16+", 129.90m, 60, "Deluxe Edition", 159.90m),
        new("Stardew Valley", 413150, "ConcernedApe", "ConcernedApe", "Steam", ["Инди", "Симулятор", "Кооператив"],
            "Уютная ферма, дружелюбные соседи и море занятий.",
            "Унаследуйте старую ферму деда и превратите её в процветающее хозяйство. Рыбалка, шахты, праздники и кооператив.",
            new DateOnly(2016, 2, 26), "6+", 19.90m, 0, Keys: 40),
        new("Hollow Knight", 367520, "Team Cherry", "Team Cherry", "Steam", ["Инди", "Экшен", "Приключения"],
            "Атмосферная метроидвания о павшем королевстве насекомых.",
            "Исследуйте огромный подземный мир Халлоунеста, сражайтесь с опасными существами и раскройте древние тайны.",
            new DateOnly(2017, 2, 24), "12+", 19.90m, 50, Keys: 3),
        new("Sekiro: Shadows Die Twice", 814380, "FromSoftware", "Activision", "Steam", ["Экшен", "Souls-like"],
            "Отомстите и вызволите юного господина.",
            "Экшен в феодальной Японии от создателей Dark Souls: скрытность, вертикальность и напряжённые дуэли на мечах.",
            new DateOnly(2019, 3, 22), "18+", 129.90m, 50),
        new("Forza Horizon 5", 1551360, "Playground Games", "Xbox Game Studios", "Xbox", ["Гонки", "Открытый мир"],
            "Бесконечные гоночные приключения в Мексике.",
            "Сотни лучших автомобилей мира, яркие пейзажи Мексики и постоянно меняющиеся сезоны в открытом мире.",
            new DateOnly(2021, 11, 9), "3+", 119.90m, 50, "Premium Edition", 199.90m),
        new("DOOM Eternal", 782330, "id Software", "Bethesda Softworks", "Steam", ["Шутер", "Экшен"],
            "Разорвите и прорвитесь сквозь орды демонов.",
            "Продолжение культового шутера: новые демоны, новое оружие и безумно динамичные сражения на Земле и за её пределами.",
            new DateOnly(2020, 3, 20), "18+", 79.90m, 75),
        new("Disco Elysium - The Final Cut", 632470, "ZA/UM", "ZA/UM", "Steam", ["RPG", "Приключения", "Инди"],
            "Детективная RPG с беспрецедентной свободой диалогов.",
            "Вы — детектив с уникальной системой навыков, расследующий убийство в городе Ревашоль. Полная озвучка и новые политические квесты.",
            new DateOnly(2019, 10, 15), "18+", 49.90m, 0),
        new("Black Myth: Wukong", 2358720, "Game Science", "Game Science", "Steam", ["Экшен", "RPG", "Souls-like"],
            "Экшен-RPG по мотивам «Путешествия на Запад».",
            "Отправьтесь в путь Избранного и раскройте тайны легенды о Царе обезьян в потрясающе красивом мире древнего Китая.",
            new DateOnly(2024, 8, 20), "16+", 159.90m, 10),
        new("HELLDIVERS 2", 553850, "Arrowhead Game Studios", "Sony Interactive Entertainment", "Steam", ["Шутер", "Кооператив"],
            "Распространяйте управляемую демократию по галактике.",
            "Кооперативный шутер до 4 игроков: высаживайтесь на опасные планеты, вызывайте стратагемы и защищайте Суперземлю.",
            new DateOnly(2024, 2, 8), "18+", 99.90m, 0, "Super Citizen Edition", 139.90m),
        new("The Elder Scrolls V: Skyrim Special Edition", 489830, "Bethesda Game Studios", "Bethesda Softworks", "Steam",
            ["RPG", "Открытый мир", "Приключения"],
            "Эпическая фэнтези-RPG, где вы — последний Драконорождённый.",
            "Ремастер легендарной игры со всеми дополнениями, улучшенной графикой и поддержкой модов.",
            new DateOnly(2016, 10, 28), "18+", 99.90m, 65),
        new("Sea of Thieves", 1172620, "Rare", "Xbox Game Studios", "Steam", ["Приключения", "Кооператив", "Открытый мир"],
            "Пиратские приключения в общем мире.",
            "Соберите команду, ищите сокровища, сражайтесь со скелетами и другими пиратами в огромном открытом море.",
            new DateOnly(2020, 6, 3), "12+", 89.90m, 50, Keys: 0),
        new("Fallout 4", 377160, "Bethesda Game Studios", "Bethesda Softworks", "Steam", ["RPG", "Шутер", "Открытый мир"],
            "Выживите в постъядерном Бостоне.",
            "Единственный выживший из убежища 111 ищет сына на пустошах. Строительство поселений и огромная свобода действий.",
            new DateOnly(2015, 11, 10), "18+", 59.90m, 67)
    ];
}
