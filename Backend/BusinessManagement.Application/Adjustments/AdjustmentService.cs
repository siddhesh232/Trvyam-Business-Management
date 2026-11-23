using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Adjustments;

/// <summary>Admin-only other income / losses / accounting adjustments that feed the net-profit figure (FRD s.12).</summary>
public sealed class AdjustmentService(IAppDbContext db, IBusinessAccessService access, TimeProvider clock) : IAdjustmentService
{
    private async Task<BusinessAccess> AdminAsync(long businessId, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        return a.IsAdmin ? a : throw new ForbiddenException();
    }

    public async Task<PagedResult<AdjustmentDto>> ListAsync(long businessId, AdjustmentListQuery q, CancellationToken ct)
    {
        await AdminAsync(businessId, ct);
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.FinancialAdjustments.AsNoTracking().Where(x => x.BusinessId == businessId);
        if (q.From is DateOnly f) query = query.Where(x => x.AdjustmentDate >= f);
        if (q.To is DateOnly t) query = query.Where(x => x.AdjustmentDate <= t);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.AdjustmentDate).ThenByDescending(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => rows.Select(r => r.CreatedBy).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return new PagedResult<AdjustmentDto>(rows.Select(r => ToDto(r, names)).ToList(), total, page, size);
    }

    public async Task<AdjustmentDto> CreateAsync(long businessId, CreateAdjustmentRequest r, CancellationToken ct)
    {
        var a = await AdminAsync(businessId, ct);
        if (!Enum.TryParse<AdjustmentType>(r.Type, true, out var type) || !Enum.IsDefined(type)) throw Fail.Validation("type", "Type must be OtherIncome, Loss or Adjustment.");
        if (!Enum.TryParse<AdjustmentDirection>(r.Direction, true, out var dir) || !Enum.IsDefined(dir)) throw Fail.Validation("direction", "Direction must be IncreasesProfit or DecreasesProfit.");
        if (type == AdjustmentType.OtherIncome && dir != AdjustmentDirection.IncreasesProfit) throw Fail.Validation("direction", "Other income must increase profit.");
        if (type == AdjustmentType.Loss && dir != AdjustmentDirection.DecreasesProfit) throw Fail.Validation("direction", "A loss must decrease profit.");

        var e = new FinancialAdjustment
        {
            BusinessId = businessId, AdjustmentDate = r.AdjustmentDate, Type = type, Direction = dir, Amount = r.Amount,
            Description = r.Description.Trim(), CreatedBy = a.UserId
        };
        db.FinancialAdjustments.Add(e);
        await db.SaveChangesAsync(ct);
        return ToDto(e, new Dictionary<long, string> { [a.UserId] = (await db.Users.IgnoreQueryFilters().AsNoTracking().FirstAsync(u => u.Id == a.UserId, ct)).Name });
    }

    public async Task DeleteAsync(long businessId, long id, CancellationToken ct)
    {
        var a = await AdminAsync(businessId, ct);
        var e = await db.FinancialAdjustments.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId, ct) ?? throw new NotFoundException("Adjustment not found.");
        e.IsDeleted = true;                                 // kept for audit; no longer counted
        e.DeletedBy = a.UserId;
        e.DeletedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
    }

    private static AdjustmentDto ToDto(FinancialAdjustment x, Dictionary<long, string> names) =>
        new(x.Id, x.BusinessId, x.AdjustmentDate, x.Type.ToString(), x.Direction.ToString(), x.Amount,
            x.Direction == AdjustmentDirection.IncreasesProfit ? x.Amount : -x.Amount, x.Description,
            names.GetValueOrDefault(x.CreatedBy, "Unknown"), x.CreatedAt);
}
