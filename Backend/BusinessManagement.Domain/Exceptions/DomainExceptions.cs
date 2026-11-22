namespace BusinessManagement.Domain.Exceptions;

/// <summary>Base type for business-rule violations. Mapped to HTTP 422 by the API.</summary>
public class DomainException(string message, string code = "domain_error") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Entity missing OR not visible to the caller (never reveals which). HTTP 404.</summary>
public sealed class NotFoundException(string message) : DomainException(message, "not_found");

/// <summary>Caller is authenticated but lacks the required permission. HTTP 403.</summary>
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : DomainException(message, "forbidden");

/// <summary>State conflict such as duplicate key or concurrent modification. HTTP 409.</summary>
public sealed class ConflictException(string message) : DomainException(message, "conflict");

/// <summary>Authentication failure (bad credentials, expired/revoked token). HTTP 401.</summary>
public sealed class UnauthorizedException(string message = "Authentication failed.") : DomainException(message, "unauthorized");
