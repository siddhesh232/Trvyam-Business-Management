namespace BusinessManagement.Application.Auth;

public sealed record LoginRequest(string Username, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ForgotPasswordRequest(string Identifier);
public sealed record ResetPasswordRequest(string Identifier, string TemporaryPassword, string NewPassword, string ConfirmPassword);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
public sealed record SetPasswordRequest(string NewPassword);

public sealed record UserDto(long Id, string Name, string Username, string Role, string Mobile, string? Email, DateOnly? DateOfBirth, string? AvatarUrl);

public sealed record AuthResponse(
    string AccessToken, DateTime AccessTokenExpiresAt,
    string RefreshToken, DateTime RefreshTokenExpiresAt,
    UserDto User);

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, string? deviceInfo, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, string? deviceInfo, CancellationToken ct);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct);
    /// <summary>Always completes silently, whether or not the identifier matches the Admin (no account enumeration).</summary>
    Task RequestAdminPasswordResetAsync(ForgotPasswordRequest request, CancellationToken ct);
    Task ResetAdminPasswordAsync(ResetPasswordRequest request, CancellationToken ct);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct);
    Task SetUserPasswordAsync(long userId, SetPasswordRequest request, CancellationToken ct);
    Task<UserDto> GetCurrentUserAsync(CancellationToken ct);
}
