using BusinessManagement.Application.Auth;
using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Users;

public sealed record UserListQuery(string? Role = null, bool? IsActive = null, string? Q = null, int Page = 1, int PageSize = 20, string? Sort = null);

public sealed record UserListItemDto(long Id, string Name, string Username, string Role, string Mobile, string? Email,
    DateOnly? DateOfBirth, string? AvatarUrl, bool IsActive, IReadOnlyList<long> BusinessIds);

public sealed record CreateUserRequest(string? Role, string Name, string Username, string Mobile, string? Email,
    DateOnly? DateOfBirth, string? AvatarUrl, string Password, IReadOnlyList<long>? BusinessIds);

/// <summary>BusinessIds: when supplied, REPLACES the set of businesses this user participates in.</summary>
public sealed record UpdateUserRequest(string Name, string Mobile, string? Email, DateOnly? DateOfBirth,
    string? AvatarUrl, bool IsActive, IReadOnlyList<long>? BusinessIds);

public sealed record UpdateProfileRequest(string Name, string Mobile, string Email, string? AvatarUrl);

public interface IUserService
{
    Task<PagedResult<UserListItemDto>> ListAsync(UserListQuery query, CancellationToken ct);
    Task<UserListItemDto> GetAsync(long id, CancellationToken ct);
    Task<UserListItemDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserListItemDto> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<UserDto> UpdateMyProfileAsync(UpdateProfileRequest request, CancellationToken ct);
}
