using BusinessManagement.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BusinessManagement.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    private string? Device => Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    /// <summary>Sign in with username and password.</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, Device, ct);

    /// <summary>Exchange a refresh token for a new access token. The refresh token is rotated; reuse revokes all sessions.</summary>
    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<AuthResponse> Refresh(RefreshRequest request, CancellationToken ct) => auth.RefreshAsync(request, Device, ct);

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request, ct);
        return NoContent();
    }

    /// <summary>Admin forgot-password: emails a temporary password (valid 6 hours, single use). Always returns 202.</summary>
    [HttpPost("admin/forgot-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        await auth.RequestAdminPasswordResetAsync(request, ct);
        return Accepted();
    }

    [HttpPost("admin/reset-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetAdminPasswordAsync(request, ct);
        return NoContent();
    }

    /// <summary>Admin changes their own password; all sessions are revoked, so sign in again afterwards.</summary>
    [HttpPost("change-password"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(request, ct);
        return NoContent();
    }
}
