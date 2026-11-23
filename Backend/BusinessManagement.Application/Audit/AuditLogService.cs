using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Audit;

public sealed class AuditLogService(IAppDbContext db, ICurrentUser current) : IAuditLogService
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery q, CancellationToken ct)
    {
        if (!current.IsAdmin) throw new ForbiddenException();
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);

        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.EntityName)) query = query.Where(a => a.EntityName == q.EntityName);
        if (q.EntityId is long eid) query = query.Where(a => a.EntityId == eid);
        if (q.BusinessId is long bid) query = query.Where(a => a.BusinessId == bid);
        if (q.UserId is long uid) query = query.Where(a => a.UserId == uid);
        if (!string.IsNullOrWhiteSpace(q.Action))
        {
            if (!Enum.TryParse<AuditAction>(q.Action, true, out var action) || !Enum.IsDefined(action)) throw Fail.Validation("action", "Unknown audit action.");
            query = query.Where(a => a.Action == action);
        }
        if (q.From is DateOnly f) { var from = f.ToDateTime(TimeOnly.MinValue); query = query.Where(a => a.OccurredAt >= from); }
        if (q.To is DateOnly t) { var to = t.AddDays(1).ToDateTime(TimeOnly.MinValue); query = query.Where(a => a.OccurredAt < to); }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var items = rows.Select(r => new AuditLogDto(r.Id, r.OccurredAt, r.UserId, r.UserId is long u ? names.GetValueOrDefault(u) : null,
            r.BusinessId, r.EntityName, r.EntityId, r.Action.ToString(), r.OldValues, r.NewValues, r.IpAddress)).ToList();
        return new PagedResult<AuditLogDto>(items, total, page, size);
    }
}
