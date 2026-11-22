using System.Security.Cryptography;
using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BusinessManagement.Application.Auth;

public sealed class AuthService(
    IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, IEmailSender email,
    ICurrentUser current, TimeProvider clock, ILogger<AuthService> log) : IAuthService
{
    // Verified against when the username is unknown so response time does not reveal which usernames exist.
    private static string? _dummyHash;
    private const string InvalidCredentials = "Invalid username or password.";
    private const string InvalidTemporaryPassword = "Invalid or expired temporary password.";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ login / refresh / logout
    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? deviceInfo, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        bool valid;
        if (user is null)
        {
            hasher.Verify(request.Password, _dummyHash ??= hasher.Hash("dummy-password-for-timing-equalisation"));
            valid = false;
        }
        else valid = hasher.Verify(request.Password, user.PasswordHash);

        if (user is null || !valid || !user.IsActive)
        {
            log.LogWarning("Failed login attempt for username {Username}", username);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var (response, _) = await IssueTokensAsync(user, deviceInfo, ct);
        await WriteEventAsync(user.Id, AuditAction.Login, "User", user.Id, "login", ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, string? deviceInfo, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                     ?? throw new UnauthorizedException("Invalid refresh token.");
        var now = Now;

        if (stored.RevokedAt is not null)
        {
            // A rotated token being presented again means it was copied: revoke the user's whole session family.
            if (stored.ReplacedByTokenId is not null)
            {
                log.LogWarning("Refresh token reuse detected for user {UserId}; revoking all sessions", stored.UserId);
                await RevokeAllAsync(stored.UserId, now, ct);
            }
            throw new UnauthorizedException("Invalid refresh token.");
        }

        if (stored.ExpiresAt <= now || stored.User is null || !stored.User.IsActive)
            throw new UnauthorizedException("Session expired. Please sign in again.");

        await using var tx = await db.BeginTransactionAsync(ct);
        var (response, newToken) = await IssueTokensAsync(stored.User, deviceInfo, ct);
        stored.RevokedAt = now;
        stored.ReplacedByTokenId = newToken.Id;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        var uid = RequireUserId();
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == uid, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = Now;
            await db.SaveChangesAsync(ct);
        }
    }

    // ------------------------------------------------------------------ admin password reset (FRD s.2.1)
    public async Task RequestAdminPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        var admin = await FindAdminAsync(request.Identifier, ct);
        if (admin?.Email is null)
        {
            log.LogInformation("Password reset requested for an identifier that is not the Admin account");
            return;   // identical outward behaviour: no enumeration
        }

        var now = Now;
        await db.PasswordResetTokens.Where(t => t.UserId == admin.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, (DateTime?)now), ct);   // only the newest temp password is valid

        var temporary = GenerateTemporaryPassword();
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = admin.Id,
            TokenHash = hasher.Hash(temporary),
            CreatedAt = now,
            ExpiresAt = now + PasswordResetToken.Lifetime
        });
        await db.SaveChangesAsync(ct);
        await WriteEventAsync(admin.Id, AuditAction.Update, "User", admin.Id, "password_reset_requested", ct);

        try
        {
            await email.SendAsync(admin.Email, "Trvyam Management - temporary password",
                "A password reset was requested for the Admin account.\n\n" +
                $"Temporary password: {temporary}\n\n" +
                "It is valid for 6 hours and can be used once. Enter it in the app together with your new password.\n" +
                "If you did not request this, you can ignore this email.", ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to send the Admin temporary-password email. Check the Email:* configuration.");
        }
    }

    public async Task ResetAdminPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var admin = await FindAdminAsync(request.Identifier, ct) ?? throw new UnauthorizedException(InvalidTemporaryPassword);
        var now = Now;

        var candidates = await db.PasswordResetTokens
            .Where(t => t.UserId == admin.Id && t.UsedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        var match = candidates.FirstOrDefault(t => hasher.Verify(request.TemporaryPassword, t.TokenHash))
                    ?? throw new UnauthorizedException(InvalidTemporaryPassword);

        await using var tx = await db.BeginTransactionAsync(ct);
        match.UsedAt = now;                                   // single use
        admin.PasswordHash = hasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(ct);
        await RevokeAllAsync(admin.Id, now, ct);              // existing sessions end
        await WriteEventAsync(admin.Id, AuditAction.Update, "User", admin.Id, "password_reset_completed", ct);
        await tx.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ password changes
    /// <summary>Admin changes their own password. Members have no self-service option (FRD s.2.2).</summary>
    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct)
    {
        var uid = RequireUserId();
        if (!current.IsAdmin) throw new ForbiddenException("Only the Admin can change passwords.");
        var user = await db.Users.FirstAsync(u => u.Id == uid, ct);
        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedException("Current password is incorrect.");

        user.PasswordHash = hasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(ct);
        await RevokeAllAsync(uid, Now, ct);
        await WriteEventAsync(uid, AuditAction.Update, "User", uid, "password_changed", ct);
    }

    public async Task SetUserPasswordAsync(long userId, SetPasswordRequest request, CancellationToken ct)
    {
        if (!current.IsAdmin) throw new ForbiddenException();
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException("User not found.");
        if (target.Role == UserRole.Admin)
            throw new ForbiddenException("Use the Admin password change/reset flow for the Admin account.");

        target.PasswordHash = hasher.Hash(request.NewPassword);
        await db.SaveChangesAsync(ct);
        await RevokeAllAsync(userId, Now, ct);                // member must sign in again with the new password
        await WriteEventAsync(current.UserId, AuditAction.Update, "User", userId, "password_set_by_admin", ct);
    }

    public async Task<UserDto> GetCurrentUserAsync(CancellationToken ct)
    {
        var uid = RequireUserId();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct)
                   ?? throw new UnauthorizedException();
        return ToDto(user);
    }

    // ------------------------------------------------------------------ helpers
    private long RequireUserId() => current.UserId ?? throw new UnauthorizedException();

    private Task<User?> FindAdminAsync(string identifier, CancellationToken ct)
    {
        var id = identifier.Trim();
        return db.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin && u.IsActive && (u.Username == id || u.Email == id), ct);
    }

    private async Task<(AuthResponse Response, RefreshToken Stored)> IssueTokensAsync(User user, string? deviceInfo, CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken();
        var stored = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.Hash,
            CreatedAt = Now,
            ExpiresAt = refresh.ExpiresAt,
            DeviceInfo = deviceInfo is { Length: > 200 } ? deviceInfo[..200] : deviceInfo
        };
        db.RefreshTokens.Add(stored);
        await db.SaveChangesAsync(ct);
        return (new AuthResponse(access.Token, access.ExpiresAt, refresh.Raw, refresh.ExpiresAt, ToDto(user)), stored);
    }

    private Task RevokeAllAsync(long userId, DateTime now, CancellationToken ct) =>
        db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, (DateTime?)now), ct);

    private async Task WriteEventAsync(long? actorId, AuditAction action, string entity, long entityId, string note, CancellationToken ct)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = Now, UserId = actorId, EntityName = entity, EntityId = entityId, Action = action,
            NewValues = $"{{\"event\":\"{note}\"}}", IpAddress = current.IpAddress
        });
        await db.SaveChangesAsync(ct);
    }

    private static UserDto ToDto(User u) =>
        new(u.Id, u.Name, u.Username, u.Role.ToString(), u.Mobile, u.Email, u.DateOfBirth, u.AvatarUrl);

    /// <summary>14 chars, no look-alike characters, always contains upper, lower and digit.</summary>
    internal static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ", lower = "abcdefghijkmnopqrstuvwxyz", digits = "23456789";
        var all = upper + lower + digits;
        var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits) };
        while (chars.Count < 14) chars.Add(Pick(all));
        for (var i = chars.Count - 1; i > 0; i--)   // Fisher-Yates with a CSPRNG
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars.ToArray());

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }
}
