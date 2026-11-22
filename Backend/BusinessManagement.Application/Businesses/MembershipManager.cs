using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Businesses;

/// <summary>
/// Business-to-member mapping rules shared by user and business management.
/// Mapping grants ACCESS; permissions (separate) control capabilities (FRD s.3).
/// </summary>
public sealed class MembershipManager(IAppDbContext db, ICurrentUser current, TimeProvider clock)
{
    /// <summary>Validates that every id is an active Member (or Employee when allowed) and returns them.</summary>
    public async Task<List<User>> LoadAssignableUsersAsync(IReadOnlyCollection<long> ids, UserRole role, string field, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var users = await db.Users.Where(u => distinct.Contains(u.Id) && u.Role == role && u.IsActive).ToListAsync(ct);
        if (users.Count != distinct.Count)
            throw Fail.Validation(field, $"One or more selected {role.ToString().ToLowerInvariant()}s do not exist or are inactive.");
        return users;
    }

    /// <summary>Makes the active mapping of a business exactly equal to <paramref name="desiredUserIds"/>.
    /// Removed users are deactivated (not deleted) so history stays intact. Caller saves and checks the minimum-member rule.</summary>
    public async Task ApplyBusinessMembersAsync(long businessId, IReadOnlyCollection<long> desiredUserIds, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await db.BusinessMembers.Where(m => m.BusinessId == businessId).ToListAsync(ct);

        foreach (var uid in desiredUserIds)
        {
            var m = existing.FirstOrDefault(x => x.UserId == uid);
            if (m is null) AddMember(businessId, uid, now);
            else if (!m.IsActive) m.IsActive = true;
        }
        foreach (var m in existing.Where(x => x.IsActive && !desiredUserIds.Contains(x.UserId))) m.IsActive = false;
    }

    /// <summary>Makes the set of businesses a single user participates in exactly equal to <paramref name="desiredBusinessIds"/>.</summary>
    public async Task ApplyUserBusinessesAsync(long userId, IReadOnlyCollection<long> desiredBusinessIds, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var valid = await db.Businesses.Where(b => desiredBusinessIds.Contains(b.Id)).Select(b => b.Id).ToListAsync(ct);
        if (valid.Count != desiredBusinessIds.Distinct().Count())
            throw Fail.Validation("businessIds", "One or more selected businesses do not exist.");

        var existing = await db.BusinessMembers.Where(m => m.UserId == userId).ToListAsync(ct);
        foreach (var bid in valid)
        {
            var m = existing.FirstOrDefault(x => x.BusinessId == bid);
            if (m is null) AddMember(bid, userId, now);
            else if (!m.IsActive) m.IsActive = true;
        }
        foreach (var m in existing.Where(x => x.IsActive && !valid.Contains(x.BusinessId))) m.IsActive = false;
    }

    /// <summary>Every business must keep at least one active Member (FRD s.13). Call after SaveChanges, inside the transaction.</summary>
    public async Task EnsureMinimumMemberAsync(IEnumerable<long> businessIds, CancellationToken ct)
    {
        foreach (var bid in businessIds.Distinct())
        {
            var count = await db.BusinessMembers.CountAsync(m => m.BusinessId == bid && m.IsActive && m.User!.Role == UserRole.Member && m.User.IsActive, ct);
            if (count == 0)
                throw Fail.Validation("memberIds", "Every business must have at least one assigned member.");
        }
    }

    private void AddMember(long businessId, long userId, DateTime now) =>
        db.BusinessMembers.Add(new BusinessMember
        {
            BusinessId = businessId, UserId = userId, AssignedAt = now, AssignedBy = current.UserId ?? 0, IsActive = true,
            // New participants start with view-business only; the Admin grants everything else explicitly.
            Permissions = new BusinessPermission { BusinessId = businessId, UserId = userId }
        });
}
