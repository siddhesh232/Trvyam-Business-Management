using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Adjustments;

public sealed record AdjustmentListQuery(DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 20);

/// <param name="Type">OtherIncome | Loss | Adjustment</param>
/// <param name="Direction">IncreasesProfit | DecreasesProfit. OtherIncome must increase profit and Loss must decrease it.</param>
public sealed record CreateAdjustmentRequest(DateOnly AdjustmentDate, string Type, string Direction, decimal Amount, string Description);

public sealed record AdjustmentDto(long Id, long BusinessId, DateOnly AdjustmentDate, string Type, string Direction, decimal Amount,
    decimal SignedAmount, string Description, string CreatedByName, DateTime CreatedAt);

public interface IAdjustmentService
{
    Task<PagedResult<AdjustmentDto>> ListAsync(long businessId, AdjustmentListQuery query, CancellationToken ct);
    Task<AdjustmentDto> CreateAsync(long businessId, CreateAdjustmentRequest request, CancellationToken ct);
    Task DeleteAsync(long businessId, long id, CancellationToken ct);
}
