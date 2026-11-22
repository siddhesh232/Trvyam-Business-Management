using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Users;

/// <summary>Admin-only user management (members, employees). Authorisation is enforced at the controller AND here.</summary>
public sealed class UserService(IAppDbContext db, IPasswordHasher hasher, ICurrentUser current, MembershipManager membership,
    TimeProvider clock) : IUserService
{
    private void RequireAdmin() { if (!current.IsAdmin) throw new ForbiddenException(); }

    public async Task<PagedResult<UserListItemDto>> ListAsync(UserListQuery q, CancellationToken ct)
    {
        RequireAdmin();
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.Users.AsNoTracking().Where(u => u.Role != UserRole.Admin);

        if (!string.IsNullOrWhiteSpace(q.Role))
        {
            if (!Enum.TryParse<UserRole>(q.Role, true, out var role) || role == UserRole.Admin)
                throw Fail.Validation("role", "Role must be Member or Employee.");
            query = query.Where(u => u.Role == role);
        }
        if (q.IsActive is bool active) query = query.Where(u => u.IsActive == active);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(u => u.Name.Contains(term) || u.Username.Contains(term) || u.Mobile.Contains(term) || (u.Email != null && u.Email.Contains(term)));
        }

        query = q.Sort?.ToLowerInvariant() switch
        {
            "-name" => query.OrderByDescending(u => u.Name),
            "created" => query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id),
            "-created" => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id),
            "username" => query.OrderBy(u => u.Username),
            _ => query.OrderBy(u => u.Name).ThenBy(u => u.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Select(u => new UserListItemDto(
            u.Id, u.Name, u.Username, u.Role.ToString(), u.Mobile, u.Email, u.DateOfBirth, u.AvatarUrl, u.IsActive,
            u.BusinessMemberships.Where(m => m.IsActive).Select(m => m.BusinessId).ToList())).ToListAsync(ct);
        return new PagedResult<UserListItemDto>(items, total, page, size);
    }

    public async Task<UserListItemDto> GetAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        return await db.Users.AsNoTracking().Where(u => u.Id == id && u.Role != UserRole.Admin).Select(u => new UserListItemDto(
                u.Id, u.Name, u.Username, u.Role.ToString(), u.Mobile, u.Email, u.DateOfBirth, u.AvatarUrl, u.IsActive,
                u.BusinessMemberships.Where(m => m.IsActive).Select(m => m.BusinessId).ToList()))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("User not found.");
    }

    public async Task<UserListItemDto> CreateAsync(CreateUserRequest r, CancellationToken ct)
    {
        RequireAdmin();
        var role = string.IsNullOrWhiteSpace(r.Role) ? UserRole.Member : Enum.Parse<UserRole>(r.Role, true);   // validated earlier
        var username = r.Username.Trim();
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw new ConflictException("Username is already taken.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var user = new User
        {
            Role = role, Name = r.Name.Trim(), Username = username, Mobile = MobileNumber.Normalize(r.Mobile),
            Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim(), DateOfBirth = r.DateOfBirth,
            AvatarUrl = r.AvatarUrl, PasswordHash = hasher.Hash(r.Password), IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        if (r.BusinessIds is { Count: > 0 })
        {
            await membership.ApplyUserBusinessesAsync(user.Id, r.BusinessIds, ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(user.Id, ct);
    }

    public async Task<UserListItemDto> UpdateAsync(long id, UpdateUserRequest r, CancellationToken ct)
    {
        RequireAdmin();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.Admin, ct)
                   ?? throw new NotFoundException("User not found.");

        await using var tx = await db.BeginTransactionAsync(ct);
        user.Name = r.Name.Trim();
        user.Mobile = MobileNumber.Normalize(r.Mobile);
        user.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim();
        user.DateOfBirth = r.DateOfBirth;
        user.AvatarUrl = r.AvatarUrl;
        user.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);

        if (r.BusinessIds is not null)
        {
            var before = await db.BusinessMembers.Where(m => m.UserId == id && m.IsActive).Select(m => m.BusinessId).ToListAsync(ct);
            await membership.ApplyUserBusinessesAsync(id, r.BusinessIds, ct);
            await db.SaveChangesAsync(ct);
            // Removing someone must never leave a business without a member.
            await membership.EnsureMinimumMemberAsync(before, ct);
        }
        if (!r.IsActive) await RevokeSessionsAsync(id, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        RequireAdmin();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role != UserRole.Admin, ct)
                   ?? throw new NotFoundException("User not found.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var businesses = await db.BusinessMembers.Where(m => m.UserId == id && m.IsActive).Select(m => m.BusinessId).ToListAsync(ct);
        foreach (var m in await db.BusinessMembers.Where(m => m.UserId == id && m.IsActive).ToListAsync(ct)) m.IsActive = false;
        db.Users.Remove(user);                 // becomes a soft delete; history (expenses, sales) keeps pointing at the row
        await db.SaveChangesAsync(ct);
        await membership.EnsureMinimumMemberAsync(businesses, ct);   // cannot orphan a business
        await RevokeSessionsAsync(id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<UserDto> UpdateMyProfileAsync(UpdateProfileRequest r, CancellationToken ct)
    {
        // Members' profiles are managed by the Admin (FRD s.2.2); only the Admin edits their own profile.
        if (!current.IsAdmin || current.UserId is not long uid) throw new ForbiddenException();
        var user = await db.Users.FirstAsync(u => u.Id == uid, ct);
        user.Name = r.Name.Trim();
        user.Mobile = MobileNumber.Normalize(r.Mobile);
        user.Email = r.Email.Trim();
        user.AvatarUrl = r.AvatarUrl;
        await db.SaveChangesAsync(ct);
        return new UserDto(user.Id, user.Name, user.Username, user.Role.ToString(), user.Mobile, user.Email, user.DateOfBirth, user.AvatarUrl);
    }

    private Task RevokeSessionsAsync(long userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTime?)now), ct);
    }
}
