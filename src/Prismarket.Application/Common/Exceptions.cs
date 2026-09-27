namespace Prismarket.Application.Common;

/// <summary>Base class for expected business errors, translated to ProblemDetails by the API.</summary>
public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message) : AppException(message, 404)
{
    public static NotFoundException For(string entity, object id) => new($"{entity} #{id} не найден(а).");
}

public sealed class BusinessRuleException(string message) : AppException(message, 400);

public sealed class ConflictException(string message) : AppException(message, 409);

public sealed class ForbiddenException(string message = "Недостаточно прав.") : AppException(message, 403);

public sealed class UnauthorizedException(string message = "Требуется авторизация.") : AppException(message, 401);
