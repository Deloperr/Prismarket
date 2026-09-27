using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Prismarket.Application.Common;
using Prismarket.Application.Features.Auth;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Profile;

public sealed record UpdateProfileRequest(
    [Required] string CurrentPassword,
    [StringLength(32, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9_.-]+$")] string? Username,
    [EmailAddress] string? Email,
    [StringLength(128, MinimumLength = 8)] string? NewPassword);

public sealed record TwoFactorSetupDto(string Secret, string QrCodeDataUri, string OtpAuthUri);
public sealed record TwoFactorEnableRequest([Required] string Secret, [Required] string Code);
public sealed record TwoFactorDisableRequest([Required] string Password);

public sealed record LibraryItemDto(
    int Id, int GameId, string GameSlug, string GameTitle, string? ImageUrl, string Platform, ProductKind Kind,
    string Edition, string? KeyValue, string? AccountLogin, string? AccountPassword, string? AccountEmail,
    DateTime PurchasedAt, bool IsActivated, DateTime? ActivatedAt, string OrderNumber);

public sealed record BalanceTransactionDto(int Id, decimal Amount, decimal BalanceAfter, BalanceTransactionType Type,
    string? Description, DateTime CreatedAt);

public sealed record ProfileStatsDto(int GamesOwned, int OrdersCompleted, decimal TotalSpent, int Reviews,
    int WishlistCount, decimal CashbackEarned);

public interface IProfileService
{
    Task<UserDto> GetMeAsync(CancellationToken ct);
    Task<ProfileStatsDto> GetStatsAsync(CancellationToken ct);
    Task<UserDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct);
    Task<UserDto> UploadAvatarAsync(Stream content, string fileName, long length, CancellationToken ct);
    Task<TwoFactorSetupDto> SetupTwoFactorAsync(CancellationToken ct);
    Task EnableTwoFactorAsync(TwoFactorEnableRequest request, CancellationToken ct);
    Task DisableTwoFactorAsync(TwoFactorDisableRequest request, CancellationToken ct);
    Task<IReadOnlyList<LibraryItemDto>> GetLibraryAsync(CancellationToken ct);
    Task<LibraryItemDto> MarkActivatedAsync(int libraryItemId, bool activated, CancellationToken ct);
    Task<PagedResult<BalanceTransactionDto>> GetBalanceHistoryAsync(PageRequest page, CancellationToken ct);
}

public sealed class ProfileService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    ITwoFactorService twoFactor,
    IFileStorage files,
    IEmailQueue emails,
    TimeProvider clock) : IProfileService
{
    private static readonly string[] AllowedImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".gif"];
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    public async Task<UserDto> GetMeAsync(CancellationToken ct) => UserDto.From(await CurrentAsync(ct));

    public async Task<ProfileStatsDto> GetStatsAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        return new ProfileStatsDto(
            await db.LibraryItems.Where(l => l.UserId == id).Select(l => l.Product.GameId).Distinct().CountAsync(ct),
            await db.Orders.CountAsync(o => o.UserId == id && o.Status == OrderStatus.Completed, ct),
            await db.Orders.Where(o => o.UserId == id && o.Status == OrderStatus.Completed).SumAsync(o => o.Total, ct),
            await db.Reviews.CountAsync(r => r.UserId == id, ct),
            await db.WishlistItems.CountAsync(w => w.UserId == id, ct),
            await db.BalanceTransactions.Where(t => t.UserId == id && t.Type == BalanceTransactionType.Cashback)
                .SumAsync(t => t.Amount, ct));
    }

    public async Task<UserDto> UpdateAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessRuleException("Текущий пароль указан неверно.");

        var changes = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Username) && request.Username != user.Username)
        {
            if (await db.Users.AnyAsync(u => u.Id != user.Id && u.Username.ToLower() == request.Username.ToLower(), ct))
                throw new ConflictException("Это имя пользователя уже занято.");
            user.Username = request.Username.Trim();
            changes.Add("имя пользователя");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (email != user.Email)
            {
                if (await db.Users.AnyAsync(u => u.Id != user.Id && u.Email == email, ct))
                    throw new ConflictException("Этот e-mail уже используется.");
                emails.Enqueue(new EmailEnvelope(user.Email, "E-mail аккаунта изменён — Prismarket",
                    EmailTemplates.Layout("E-mail изменён",
                        $"<p>Адрес вашего аккаунта изменён на {EmailTemplates.Encode(email)}. Если это были не вы — напишите в поддержку.</p>")));
                user.Email = email;
                user.EmailConfirmed = false;
                user.EmailConfirmationToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
                changes.Add("e-mail");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            user.PasswordHash = hasher.Hash(request.NewPassword);
            changes.Add("пароль");
        }

        if (changes.Count > 0)
        {
            emails.Enqueue(new EmailEnvelope(user.Email, "Данные аккаунта изменены — Prismarket",
                EmailTemplates.Layout("Данные аккаунта изменены",
                    $"<p>В вашем аккаунте изменено: {string.Join(", ", changes)}.</p>")));
        }

        await db.SaveChangesAsync(ct);
        return UserDto.From(user);
    }

    public async Task<UserDto> UploadAvatarAsync(Stream content, string fileName, long length, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
            throw new BusinessRuleException("Допустимые форматы: PNG, JPG, WEBP, GIF.");
        if (length > MaxAvatarBytes)
            throw new BusinessRuleException("Размер аватара не должен превышать 2 МБ.");

        var user = await CurrentAsync(ct);
        var previous = user.AvatarUrl;
        user.AvatarUrl = await files.SaveAsync(content, $"avatar-{user.Id}{extension}", "avatars", ct);
        await db.SaveChangesAsync(ct);
        if (previous is not null) await files.DeleteAsync(previous, ct);
        return UserDto.From(user);
    }

    public async Task<TwoFactorSetupDto> SetupTwoFactorAsync(CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (user.TwoFactorEnabled) throw new BusinessRuleException("2FA уже включена.");
        var secret = twoFactor.GenerateSecret();
        var uri = twoFactor.BuildOtpAuthUri(secret, user.Email);
        return new TwoFactorSetupDto(secret, twoFactor.BuildQrCodeDataUri(uri), uri);
    }

    public async Task EnableTwoFactorAsync(TwoFactorEnableRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (!twoFactor.Verify(request.Secret, request.Code))
            throw new BusinessRuleException("Неверный код. Проверьте время на телефоне и попробуйте снова.");
        user.TwoFactorEnabled = true;
        user.TwoFactorSecret = request.Secret;
        emails.Enqueue(new EmailEnvelope(user.Email, "2FA включена — Prismarket",
            EmailTemplates.Layout("Двухфакторная аутентификация включена",
                "<p>Теперь при входе потребуется код из приложения-аутентификатора.</p>")));
        await db.SaveChangesAsync(ct);
    }

    public async Task DisableTwoFactorAsync(TwoFactorDisableRequest request, CancellationToken ct)
    {
        var user = await CurrentAsync(ct);
        if (!hasher.Verify(request.Password, user.PasswordHash))
            throw new BusinessRuleException("Неверный пароль.");
        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LibraryItemDto>> GetLibraryAsync(CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        return await Library(id).OrderByDescending(l => l.PurchasedAt).ToListAsync(ct);
    }

    public async Task<LibraryItemDto> MarkActivatedAsync(int libraryItemId, bool activated, CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        var item = await db.LibraryItems.FirstOrDefaultAsync(l => l.Id == libraryItemId && l.UserId == id, ct)
                   ?? throw NotFoundException.For("Элемент библиотеки", libraryItemId);
        item.IsActivated = activated;
        item.ActivatedAt = activated ? clock.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(ct);
        return await Library(id).FirstAsync(l => l.Id == libraryItemId, ct);
    }

    public async Task<PagedResult<BalanceTransactionDto>> GetBalanceHistoryAsync(PageRequest page, CancellationToken ct)
    {
        var id = currentUser.RequireUserId();
        return await db.BalanceTransactions.AsNoTracking()
            .Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new BalanceTransactionDto(t.Id, t.Amount, t.BalanceAfter, t.Type, t.Description, t.CreatedAt))
            .ToPagedAsync(page, ct);
    }

    private IQueryable<LibraryItemDto> Library(int userId) => db.LibraryItems.AsNoTracking()
        .Where(l => l.UserId == userId)
        .Select(l => new LibraryItemDto(l.Id, l.Product.GameId, l.Product.Game.Slug, l.Product.Game.Title,
            l.Product.Game.HeaderImageUrl ?? l.Product.Game.CoverImageUrl, l.Product.Game.Platform.Name,
            l.Product.Kind, l.Product.Edition,
            l.Key != null ? l.Key.Value : null,
            l.Account != null ? l.Account.Login : null,
            l.Account != null ? l.Account.Password : null,
            l.Account != null ? l.Account.Email : null,
            l.PurchasedAt, l.IsActivated, l.ActivatedAt, l.OrderItem.Order.Number));

    private async Task<User> CurrentAsync(CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == currentUser.RequireUserId(), ct)
        ?? throw new UnauthorizedException();
}
