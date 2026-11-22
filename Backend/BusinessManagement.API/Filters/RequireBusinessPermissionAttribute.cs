using BusinessManagement.Application.Authorization;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BusinessManagement.API.Filters;

/// <summary>
/// Enforces business membership + a specific permission on the server (hiding UI is never enough).
/// The route must contain a {businessId} value. Non-members get 404, members lacking the permission get 403.
/// </summary>
public sealed class RequireBusinessPermissionAttribute : TypeFilterAttribute
{
    public RequireBusinessPermissionAttribute(Permission permission) : base(typeof(BusinessPermissionFilter))
        => Arguments = [permission];
}

public static class BusinessAccessHttpContextExtensions
{
    internal const string ItemKey = "BusinessAccess";
    public static BusinessAccess GetBusinessAccess(this HttpContext context) =>
        context.Items[ItemKey] as BusinessAccess ?? throw new InvalidOperationException("RequireBusinessPermission was not applied.");
}

public sealed class BusinessPermissionFilter(Permission permission, IBusinessAccessService access) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        if (!context.RouteData.Values.TryGetValue("businessId", out var raw) || !long.TryParse(raw?.ToString(), out var businessId))
            throw new NotFoundException("Business not found.");

        context.HttpContext.Items[BusinessAccessHttpContextExtensions.ItemKey] =
            await access.RequireAsync(businessId, permission, context.HttpContext.RequestAborted);
    }
}
