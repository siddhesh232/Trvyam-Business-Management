using BusinessManagement.API.Filters;
using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

[ApiController, Authorize]
[Route("api/v1/me")]
public sealed class MeController(IAuthService auth) : ControllerBase
{
    [HttpGet]
    public Task<UserDto> Get(CancellationToken ct) => auth.GetCurrentUserAsync(ct);
}

[ApiController, Authorize(Roles = "Admin")]
[Route("api/v1/users")]
public sealed class UsersController(IAuthService auth) : ControllerBase
{
    /// <summary>The only way a Member's password is reset: the Admin sets a new one (FRD s.2.2).</summary>
    [HttpPost("{id:long}/set-password")]
    public async Task<IActionResult> SetPassword(long id, SetPasswordRequest request, CancellationToken ct)
    {
        await auth.SetUserPasswordAsync(id, request, ct);
        return NoContent();
    }
}

public sealed record MyPermissionsResponse(long BusinessId, string Role, IReadOnlyDictionary<string, bool> Permissions);

[ApiController, Authorize]
[Route("api/v1/businesses/{businessId:long}")]
public sealed class BusinessAccessController : ControllerBase
{
    /// <summary>The caller's effective permissions in this business (used by the app to show/hide features).
    /// The server still re-checks every action.</summary>
    [HttpGet("my-permissions"), RequireBusinessPermission(Permission.ViewBusiness)]
    public MyPermissionsResponse MyPermissions(long businessId)
    {
        var access = HttpContext.GetBusinessAccess();
        return new MyPermissionsResponse(businessId, access.Role.ToString(), access.Effective());
    }
}
