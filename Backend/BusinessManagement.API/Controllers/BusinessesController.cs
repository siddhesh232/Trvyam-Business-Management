using BusinessManagement.API.Filters;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Controllers;

public sealed record MyPermissionsResponse(long BusinessId, string Role, IReadOnlyDictionary<string, bool> Permissions);

[ApiController, Authorize]
[Route("api/v1/businesses")]
public sealed class BusinessesController(IBusinessService businesses) : ControllerBase
{
    /// <summary>Admin: all businesses. Others: only the businesses they are assigned to.</summary>
    [HttpGet]
    public Task<PagedResult<BusinessListItemDto>> List([FromQuery] BusinessListQuery query, CancellationToken ct) => businesses.ListAsync(query, ct);

    [HttpGet("{businessId:long}"), RequireBusinessPermission(Permission.ViewBusiness)]
    public Task<BusinessDetailDto> Get(long businessId, CancellationToken ct) => businesses.GetAsync(businessId, ct);

    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<ActionResult<BusinessDetailDto>> Create(CreateBusinessRequest request, CancellationToken ct)
    {
        var created = await businesses.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { businessId = created.Id }, created);
    }

    [HttpPut("{businessId:long}"), Authorize(Roles = "Admin")]
    public Task<BusinessDetailDto> Update(long businessId, UpdateBusinessRequest request, CancellationToken ct) =>
        businesses.UpdateAsync(businessId, request, ct);

    [HttpDelete("{businessId:long}"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(long businessId, CancellationToken ct)
    {
        await businesses.DeleteAsync(businessId, ct);
        return NoContent();
    }

    /// <summary>Replaces the business's participants. At least one Member is mandatory.</summary>
    [HttpPut("{businessId:long}/members"), Authorize(Roles = "Admin")]
    public Task<BusinessDetailDto> SetMembers(long businessId, SetBusinessMembersRequest request, CancellationToken ct) =>
        businesses.SetMembersAsync(businessId, request, ct);

    [HttpGet("{businessId:long}/members/{userId:long}/permissions"), Authorize(Roles = "Admin")]
    public Task<BusinessPermissionsDto> GetPermissions(long businessId, long userId, CancellationToken ct) =>
        businesses.GetPermissionsAsync(businessId, userId, ct);

    [HttpPut("{businessId:long}/members/{userId:long}/permissions"), Authorize(Roles = "Admin")]
    public Task<BusinessPermissionsDto> SetPermissions(long businessId, long userId, BusinessPermissionsDto permissions, CancellationToken ct) =>
        businesses.SetPermissionsAsync(businessId, userId, permissions, ct);

    /// <summary>The caller's effective permissions in this business (used by the app to show/hide features).
    /// The server still re-checks every action.</summary>
    [HttpGet("{businessId:long}/my-permissions"), RequireBusinessPermission(Permission.ViewBusiness)]
    public MyPermissionsResponse MyPermissions(long businessId)
    {
        var access = HttpContext.GetBusinessAccess();
        return new MyPermissionsResponse(businessId, access.Role.ToString(), access.Effective());
    }
}
