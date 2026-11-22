using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Enums;

namespace BusinessManagement.API.Services;

/// <summary>Resolves the caller from JWT claims ("sub" = user id, "role" = Admin|Member|Employee).</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public long? UserId => long.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public UserRole? Role => Enum.TryParse<UserRole>(Principal?.FindFirst("role")?.Value, out var r) ? r : null;

    public bool IsAdmin => Role == UserRole.Admin;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
