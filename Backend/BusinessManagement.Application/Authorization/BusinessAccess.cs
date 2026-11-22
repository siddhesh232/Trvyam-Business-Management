using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Authorization;

/// <summary>What the current user may do inside one business. Admin: everything. Member: configured flags.
/// Employee (future): view permissions only, never write (FRD s.2.3).</summary>
public sealed record BusinessAccess(long BusinessId, long UserId, UserRole Role, BusinessPermission? Permissions)
{
    public bool IsAdmin => Role == UserRole.Admin;

    public bool Can(Permission permission) => Role switch
    {
        UserRole.Admin => true,
        UserRole.Member => Permissions?.Has(permission) == true,
        UserRole.Employee => IsViewPermission(permission) && Permissions?.Has(permission) == true,
        _ => false
    };

    /// <summary>Own-record rule: Admin always; Member only for records they created and only if the flag is on.
    /// A member-wise visibility permission never grants edit/delete rights over others' records.</summary>
    public bool CanModifyOwn(long recordCreatedBy, Permission ownPermission) =>
        IsAdmin || (Role == UserRole.Member && recordCreatedBy == UserId && Permissions?.Has(ownPermission) == true);

    public IReadOnlyDictionary<string, bool> Effective() =>
        Enum.GetValues<Permission>().ToDictionary(p => p.ToString(), Can);

    private static bool IsViewPermission(Permission p) => p.ToString().StartsWith("View", StringComparison.Ordinal);
}

public interface IBusinessAccessService
{
    /// <summary>Resolves access for the current user. Throws NotFound when the user cannot see the business at all
    /// (existence is not revealed) and Forbidden when a required permission is missing.</summary>
    Task<BusinessAccess> RequireAsync(long businessId, Permission? required, CancellationToken ct);
}

public sealed class BusinessAccessService(IAppDbContext db, ICurrentUser current) : IBusinessAccessService
{
    public async Task<BusinessAccess> RequireAsync(long businessId, Permission? required, CancellationToken ct)
    {
        if (current.UserId is not long uid || current.Role is not UserRole role)
            throw new UnauthorizedException();

        var exists = await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId, ct);   // soft-deleted filtered out
        if (!exists) throw new NotFoundException("Business not found.");

        BusinessAccess access;
        if (role == UserRole.Admin)
        {
            access = new BusinessAccess(businessId, uid, role, null);
        }
        else
        {
            var membership = await db.BusinessMembers.AsNoTracking()
                .Where(m => m.BusinessId == businessId && m.UserId == uid && m.IsActive)
                .Select(m => new { m.Permissions }).FirstOrDefaultAsync(ct);
            if (membership is null) throw new NotFoundException("Business not found.");   // not 403: hides existence
            access = new BusinessAccess(businessId, uid, role, membership.Permissions);
        }

        if (required is Permission p && !access.Can(p)) throw new ForbiddenException();
        return access;
    }
}
