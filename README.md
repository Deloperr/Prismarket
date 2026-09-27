<div align="center">

# ◆ Prismarket

**Магазин цифровых ключей для игр с автоматизацией бизнес-процессов**

ASP.NET Core 10 · EF Core + Dapper · PostgreSQL · Redis · Hangfire · SignalR · React 18 + TypeScript

</div>

> 🇬🇧 **TL;DR (EN):** Prismarket is a digital game-key store built with Clean Architecture on ASP.NET Core 10 Web API and a React + TypeScript SPA with a glassmorphism UI. PostgreSQL is accessed via EF Core (writes, migrations) and Dapper (analytics SQL). 13 scheduled Hangfire jobs and 4 event-driven rules automate the store: instant key delivery, unpaid-order expiry with stock release, scheduled sales, low-stock monitoring, wishlist price-drop alerts, abandoned-cart reminders, NBRB exchange rates, e-mailed PDF/Excel reports, support bot, cashback and more — all configurable from the admin panel. Includes unit tests, Docker Compose and GitHub Actions CI/CD.

---

## Возможности

**Покупатель**
- каталог с фильтрами (жанр, платформа, цена, скидки, наличие), поиск, сортировки, рекомендации по жанрам из библиотеки;
- корзина (гостевая в браузере → автоматически сливается с серверной после входа), промокоды;
- оплата: баланс, банковская карта и ЕРИП (тестовый шлюз), Stripe Checkout (если задан ключ);
- мгновенная выдача ключей/аккаунтов в библиотеку и на e-mail, кэшбэк на внутренний баланс;
- вишлист с уведомлениями о снижении цены и поступлении, отзывы с рейтингом, отзывы о магазине;
- профиль: аватар, история заказов и операций, пополнение баланса, 2FA (TOTP + QR);
- чат с поддержкой в реальном времени (SignalR) и бот-помощник;
- мультивалютность (BYN/USD/EUR/RUB по курсу НБ РБ), светлая/тёмная тема.

**Администратор**
- дашборд: выручка, динамика, топ игр, способы оплаты, жанры, остатки (Recharts);
- CRUD игр и изданий, загрузка изображений, импорт ключей (текст/файл), генерация демо-ключей, импорт аккаунтов;
- заказы (ручное подтверждение оплаты, возврат на баланс), пользователи (роли, блокировка, корректировка баланса);
- модерация отзывов, тикеты поддержки, акции по расписанию, промокоды;
- отчёты PDF / Excel / Word, экспорт каталога и заказов в JSON / CSV / XML;
- **страница «Автоматизация»**: включение/выключение, CRON, параметры, ручной запуск, журнал запусков; панель Hangfire;
- журнал аудита всех изменений.

## Автоматизация (тема дипломного проекта)

| Автоматизация | Тип | Что делает |
|---|---|---|
| Мгновенная выдача ключей | событие | после оплаты помечает ключи проданными, заполняет библиотеку, отправляет письмо с ключами |
| Кэшбэк | событие | начисляет % от заказа на баланс |
| Автомодерация отзывов | событие | стоп-слова/ссылки → отзыв уходит на ручную модерацию |
| Бот поддержки | событие | классифицирует обращение, ставит приоритет, отвечает на типовые вопросы, назначает наименее загруженного админа |
| Отмена неоплаченных заказов | каждую минуту | снимает резерв с ключей (`FOR UPDATE SKIP LOCKED`), отменяет заказ, уведомляет |
| Отправка писем из очереди | каждую минуту | transactional outbox, параллельная отправка (`Parallel.ForEachAsync`), экспоненциальные ретраи |
| Планировщик распродаж | каждую минуту | включает скидки в момент старта акции и восстанавливает цены после окончания |
| Контроль остатков | каждые 15 мин | предупреждает о низком остатке, скрывает товар при нуле, возвращает после пополнения и уведомляет вишлист |
| Снижение цен в вишлисте | каждый час | уведомление + письмо при падении цены на N% |
| Брошенные корзины | каждые 2 ч | напоминание о товарах в корзине |
| Курсы валют | каждые 6 ч | загрузка курсов НБ РБ (api.nbrb.by), параллельные HTTP-запросы |
| Отчёт о продажах | ежедневно 08:00 | PDF + Excel во вложении всем администраторам |
| Автозакрытие обращений | каждый час | закрывает тикеты без ответа клиента |
| Ротация витрины | ежедневно | топ продаж → главная, пересчёт рейтингов |
| Возврат неактивных | по понедельникам | персональный промокод тем, кто давно не заходил |
| Очистка данных | ежедневно | токены, уведомления, аудит, история запусков |
| Резервное копирование | ежедневно (выкл. по умолчанию) | `pg_dump` с ротацией копий |

Все автоматизации описаны в одном месте (`AutomationCatalog`), синхронизируются с таблицей `automation_jobs` при старте и регистрируются в Hangfire. Каждый запуск пишется в `automation_runs`: статус, длительность, число обработанных объектов, сообщение.

## Архитектура

```
src/
├── Prismarket.Domain          # сущности, перечисления, доменные правила (без зависимостей)
├── Prismarket.Application     # сценарии (сервисы), DTO, интерфейсы, автоматизации, стратегии оплаты
├── Prismarket.Infrastructure  # EF Core + PostgreSQL, Dapper, Hangfire, SMTP, Redis, отчёты, безопасность
└── Prismarket.Api             # Web API контроллеры, JWT, SignalR hub, обработка ошибок
tests/Prismarket.UnitTests     # xUnit + NSubstitute + EF Core InMemory
client/                        # React 18 + TypeScript + Vite + TanStack Query + Zustand
```

Зависимости направлены внутрь (Clean Architecture): Application знает только интерфейсы (`IAppDbContext`, `IEmailSender`, `IAutomationScheduler`…), реализации живут в Infrastructure (Dependency Inversion).

**Паттерны и принципы:** Strategy (способы оплаты, форматы отчётов), Transactional Outbox (письма), Adapter (Hangfire), Unit of Work (DbContext), Interceptor (аудит и таймстемпы вместо SQL-триггеров), Resolver/Factory, Options pattern; SOLID, DRY (одна проекция карточки игры, один интерсептор), KISS/YAGNI.

**Асинхронность и TPL:** весь I/O асинхронный с `CancellationToken`; `Task.WhenAll` — параллельные SQL-запросы дашборда через Dapper и `NpgsqlDataSource`, параллельная загрузка курсов и рендер отчётов; `Parallel.ForEachAsync` с ограничением степени параллелизма — отправка писем; распределённая блокировка Hangfire — запрет параллельного запуска одной автоматизации.

**Данные:** PostgreSQL (snake_case, check-constraints, уникальные индексы, `xmin` как токен оптимистичной блокировки для баланса и заказов, `jsonb`), EF Core для записи и миграций, Dapper для аналитики (`FILTER`, `generate_series`, подзапросы), Redis как распределённый кэш (NoSQL), fallback на in-memory.

**Безопасность:** bcrypt, JWT (30 мин) + refresh-токен в http-only cookie с ротацией, TOTP 2FA, rate limiting на auth-эндпоинтах, роли, аудит, проверка подписи Stripe-вебхуков.

## Быстрый старт

### Docker (всё сразу)

```bash
docker compose up --build
```

- магазин и API: http://localhost:8080
- письма (Mailpit): http://localhost:8025
- Hangfire: кнопка «Панель Hangfire» на странице «Автоматизация»

Демо-аккаунты: `admin / Admin123!` (администратор), `demo / Demo123!` (покупатель с балансом). База заполняется каталогом из 18 игр и историей продаж за 45 дней.

### Локально (Rider / VS / CLI)

Требуется .NET SDK 10, Node.js 20+, PostgreSQL 15+.

```bash
# инфраструктура
docker compose up -d postgres redis mailpit   # PostgreSQL будет на localhost:5433

# backend  →  http://localhost:5080  (Scalar: /scalar/v1)
dotnet run --project src/Prismarket.Api

# frontend →  http://localhost:5173  (прокси /api на 5080)
cd client && npm install && npm run dev
```

Строка подключения и прочие настройки — в `src/Prismarket.Api/appsettings.json` (или переменные окружения `ConnectionStrings__Postgres`, `Jwt__Secret`, `Smtp__Host`, `Stripe__SecretKey`…).

### Миграции

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate -p src/Prismarket.Infrastructure -s src/Prismarket.Api -o Persistence/Migrations
```

При старте API применяет миграции автоматически (если миграций нет — создаёт схему через `EnsureCreated`).

### Тесты

```bash
dotnet test
```

Покрыты доменные правила, автоматизации (распродажи, склад, вишлист, выдача ключей и кэшбэк на EF InMemory), бот поддержки, модерация, бэкофф, CRON, подпись Stripe, CSV-экспорт.

## CI/CD

GitHub Actions (`.github/workflows/ci.yml`): сборка и тесты backend с отчётом покрытия, сборка frontend, публикация Docker-образа в GHCR при пуше в `main`.

## Лицензия

Учебный проект. Изображения игр загружаются с CDN Steam и принадлежат правообладателям.
