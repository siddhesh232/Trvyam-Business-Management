using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Audit;

/// <param name="Action">Create | Update | Delete | Reverse | Cancel | Login | PermissionChange</param>
public sealed record AuditLogQuery(string? EntityName = null, long? EntityId = null, long? BusinessId = null, long? UserId = null,
    string? Action = null, DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 50);

public sealed record AuditLogDto(long Id, DateTime OccurredAt, long? UserId, string? UserName, long? BusinessId, string EntityName,
    long EntityId, string Action, string? OldValues, string? NewValues, string? IpAddress);

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct);
}
