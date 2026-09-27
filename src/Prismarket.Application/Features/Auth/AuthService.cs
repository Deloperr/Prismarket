using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prismarket.Application.Common;
using Prismarket.Domain.Entities;
using Prismarket.Domain.Enums;

namespace Prismarket.Application.Features.Auth;

public sealed record RegisterRequest(
    [Required, StringLength(32, MinimumLength = 3), RegularExpression("^[a-zA-Z0-9_.-]+$")] string Username,
    [Required, EmailAddress, StringLength(100)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password);

public sealed record LoginRequest(
    [Required] string Login,
    [Required] string Password,
    string? TwoFactorCode);

public sealed record ForgotPasswordRequest([Required] string Login);

public sealed record ResetPasswordRequest(
    [Required] string Token,
    [Required, StringLength(128, MinimumLength = 8)] string NewPassword);

public sealed record UserDto(
    int Id, string Username, string Email, string? AvatarUrl, string Role, decimal Balance,
    bool EmailConfirmed, bool TwoFactorEnabled, DateTime CreatedAt)
{
    public static UserDto From(User u) => new(u.Id, u.Username, u.Email, u.AvatarUrl, u.Role.ToString(),
        u.Balance, u.EmailConfirmed, u.TwoFactorEnabled, u.CreatedAt);
}

public sealed record AuthResult(
    bool RequiresTwoFactor,
    string? AccessToken,
    DateTime? AccessTokenExpiresAt,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresAt,
    UserDto? User)
{
    public static readonly AuthResult TwoFactorRequired = new(true, null, null, null, null, null);
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);
    Task LogoutAsync(string? refreshToken, CancellationToken ct);
    Task ConfirmEmailAsync(string token, CancellationToken ct);
    Task ResendConfirmationAsync(CancellationToken ct);
    Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
}

public sealed class AuthService(
    IAppDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    ITwoFactorService twoFactor,
    IEmailQueue emails,
    IAppUrls urls,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var username = request.Username.Trim();

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("Пользователь с таким e-mail уже зарегистрирован.");
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct))
            throw new ConflictException("Это имя пользователя уже занято.");

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = hasher.Hash(request.Password),
            EmailConfirmationToken = tokens.GenerateSecureToken(),
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        QueueConfirmationEmail(user);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {Username} registered", user.Username);
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var login = request.Login.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email == login || u.Username.ToLower() == login, ct);

        if (user is null || !hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Неверный логин или пароль.");
        if (!user.IsActive)
            throw new ForbiddenException("Аккаунт заблокирован. Обратитесь в поддержку.");

        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                return AuthResult.TwoFactorRequired;
            if (!twoFactor.Verify(user.TwoFactorSecret!, request.TwoFactorCode))
                throw new UnauthorizedException("Неверный код двухфакторной аутентификации.");
        }

        user.LastLoginAt = Now;
        return await IssueTokensAsync(user, ct);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = tokens.Hash(refreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || !stored.IsActive(Now) || !stored.User.IsActive)
            throw new UnauthorizedException("Сессия истекла, войдите снова.");

        // Rotation: every refresh token can be used only once.
        stored.RevokedAt = Now;
        return await IssueTokensAsync(stored.User, ct);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = tokens.Hash(refreshToken);
        await db.RefreshTokens.Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now), ct);
    }

    public async Task ConfirmEmailAsync(string token, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmailConfirmationToken == token, ct)
                   ?? throw new BusinessRuleException("Ссылка подтверждения недействительна или уже использована.");
        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task ResendConfirmationAsync(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([currentUser.RequireUserId()], ct)
                   ?? throw new UnauthorizedException();
        if (user.EmailConfirmed) return;
        user.EmailConfirmationToken = tokens.GenerateSecureToken();
        QueueConfirmationEmail(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var login = request.Login.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == login || u.Username.ToLower() == login, ct);
        // Do not reveal whether the account exists.
        if (user is null) return;

        user.PasswordResetToken = tokens.GenerateSecureToken();
        user.PasswordResetExpiresAt = Now.Add(PasswordResetLifetime);
        var link = urls.Build($"/reset-password?token={Uri.EscapeDataString(user.PasswordResetToken)}");
        emails.Enqueue(new EmailEnvelope(user.Email, "Сброс пароля — Prismarket",
            EmailTemplates.Layout("Сброс пароля",
                $"<p>Здравствуйте, {EmailTemplates.Encode(user.Username)}!</p><p>Кто-то (надеемся, вы) запросил сброс пароля. " +
                "Ссылка действует 1 час. Если это были не вы — просто проигнорируйте письмо.</p>",
                "Задать новый пароль", link)));
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.PasswordResetToken == request.Token, ct);
        if (user is null || user.PasswordResetExpiresAt < Now)
            throw new BusinessRuleException("Ссылка для сброса пароля недействительна или истекла.");

        user.PasswordHash = hasher.Hash(request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresAt = null;

        // Security: revoke all active sessions after a password change.
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, Now), ct);

        emails.Enqueue(new EmailEnvelope(user.Email, "Пароль изменён — Prismarket",
            EmailTemplates.Layout("Пароль изменён",
                "<p>Пароль вашего аккаунта был успешно изменён. Если это были не вы — срочно напишите в поддержку.</p>")));
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResult> IssueTokensAsync(User user, CancellationToken ct)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user);
        var refresh = tokens.GenerateSecureToken(48);
        var refreshExpires = Now.Add(RefreshTokenLifetime);

        db.RefreshTokens.Add(new RefreshToken
        {
            User = user,
            TokenHash = tokens.Hash(refresh),
            CreatedAt = Now,
            ExpiresAt = refreshExpires,
            CreatedByIp = currentUser.IpAddress
        });
        await db.SaveChangesAsync(ct);

        return new AuthResult(false, access, accessExpires, refresh, refreshExpires, UserDto.From(user));
    }

    private void QueueConfirmationEmail(User user)
    {
        var link = urls.Build($"/verify-email?token={Uri.EscapeDataString(user.EmailConfirmationToken!)}");
        emails.Enqueue(new EmailEnvelope(user.Email, "Подтвердите e-mail — Prismarket",
            EmailTemplates.Layout("Добро пожаловать в Prismarket!",
                $"<p>Привет, {EmailTemplates.Encode(user.Username)}!</p><p>Подтвердите адрес почты, чтобы совершать покупки " +
                "и получать ключи на e-mail.</p>", "Подтвердить e-mail", link)));
    }
}
