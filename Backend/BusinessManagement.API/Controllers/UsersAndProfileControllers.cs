using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

[ApiController, Authorize]
[Route("api/v1/me")]
public sealed class MeController(IAuthService auth, IUserService users) : ControllerBase
{
    [HttpGet]
    public Task<UserDto> Get(CancellationToken ct) => auth.GetCurrentUserAsync(ct);

    /// <summary>Admin edits their own profile. Members' profiles are managed by the Admin.</summary>
    [HttpPut, Authorize(Roles = "Admin")]
    public Task<UserDto> Update(UpdateProfileRequest request, CancellationToken ct) => users.UpdateMyProfileAsync(request, ct);
}

/// <summary>Admin-only member/employee management.</summary>
[ApiController, Authorize(Roles = "Admin")]
[Route("api/v1/users")]
public sealed class UsersController(IAuthService auth, IUserService users, IBusinessService businesses) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserListItemDto>> List([FromQuery] UserListQuery query, CancellationToken ct) => users.ListAsync(query, ct);

    [HttpGet("{id:long}")]
    public Task<UserListItemDto> Get(long id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<UserListItemDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var created = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:long}")]
    public Task<UserListItemDto> Update(long id, UpdateUserRequest request, CancellationToken ct) => users.UpdateAsync(id, request, ct);

    /// <summary>Soft-deletes the user. Blocked if it would leave a business without a member.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await users.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Businesses this user participates in.</summary>
    [HttpGet("{id:long}/businesses")]
    public Task<IReadOnlyList<BusinessListItemDto>> Businesses(long id, CancellationToken ct) => businesses.ListForUserAsync(id, ct);

    /// <summary>The only way a Member's password is reset: the Admin sets a new one (FRD s.2.2).</summary>
    [HttpPost("{id:long}/set-password")]
    public async Task<IActionResult> SetPassword(long id, SetPasswordRequest request, CancellationToken ct)
    {
        await auth.SetUserPasswordAsync(id, request, ct);
        return NoContent();
    }
}
