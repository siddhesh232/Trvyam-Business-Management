using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Expenses;

public sealed record ExpenseListQuery(DateOnly? From = null, DateOnly? To = null, long? PurchasedBy = null, string? Q = null,
    int Page = 1, int PageSize = 20, string? Sort = null);

public sealed record ExpenseRangeQuery(DateOnly? From = null, DateOnly? To = null);

/// <param name="Basis">purchasedBy (member who bought the item) or createdBy (user who entered the record).</param>
public sealed record ExpenseByMemberQuery(DateOnly? From = null, DateOnly? To = null, string? Basis = null);

public sealed record ExpenseDto(long Id, long BusinessId, DateOnly ExpenseDate, string Name, decimal Quantity, decimal UnitPrice,
    decimal TotalAmount, decimal InputGstAmount, bool IsInputGstEligible,
    long PurchasedByUserId, string PurchasedByName, long CreatedByUserId, string CreatedByName, DateTime CreatedAt,
    bool CanEdit, bool CanDelete);

public sealed record SaveExpenseRequest(DateOnly ExpenseDate, string Name, decimal Quantity, decimal UnitPrice,
    long? PurchasedByUserId, decimal InputGstAmount, bool IsInputGstEligible);

public sealed record ExpenseSummaryDto(long BusinessId, DateOnly? From, DateOnly? To, decimal TotalAmount, int Count, decimal EligibleInputGst);

public sealed record MemberExpenseRowDto(long UserId, string Name, decimal TotalAmount, int Count);

public sealed record ExpenseByMemberDto(long BusinessId, string Basis, string BasisLabel, DateOnly? From, DateOnly? To,
    decimal GrandTotal, IReadOnlyList<MemberExpenseRowDto> Rows);

public interface IExpenseService
{
    Task<PagedResult<ExpenseDto>> ListAsync(long businessId, ExpenseListQuery query, CancellationToken ct);
    Task<ExpenseDto> CreateAsync(long businessId, SaveExpenseRequest request, CancellationToken ct);
    Task<ExpenseDto> UpdateAsync(long businessId, long id, SaveExpenseRequest request, CancellationToken ct);
    Task DeleteAsync(long businessId, long id, CancellationToken ct);
    Task<ExpenseSummaryDto> SummaryAsync(long businessId, ExpenseRangeQuery query, CancellationToken ct);
    Task<ExpenseByMemberDto> ByMemberAsync(long businessId, ExpenseByMemberQuery query, CancellationToken ct);
}
