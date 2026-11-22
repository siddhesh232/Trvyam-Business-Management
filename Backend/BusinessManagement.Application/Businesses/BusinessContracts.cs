using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Businesses;

public sealed record BusinessListQuery(string? Q = null, bool? IsActive = null, int Page = 1, int PageSize = 20, string? Sort = null);

public sealed record BusinessListItemDto(long Id, string Name, string? Category, string? LogoUrl, string? GstNumber, bool IsActive, int MemberCount);

public sealed record BusinessMemberDto(long UserId, string Name, string Username, string Role, DateTime AssignedAt);

public sealed record BusinessDetailDto(long Id, string Name, string? Description, string? GstNumber, string? Category,
    string? LogoUrl, string? AddressLine, string? StateCode, bool IsActive, IReadOnlyList<BusinessMemberDto> Members);

public sealed record CreateBusinessRequest(string Name, string? Description, string? GstNumber, string? Category,
    string? LogoUrl, string? AddressLine, string? StateCode, IReadOnlyList<long> MemberIds, IReadOnlyList<long>? EmployeeIds);

public sealed record UpdateBusinessRequest(string Name, string? Description, string? GstNumber, string? Category,
    string? LogoUrl, string? AddressLine, string? StateCode, bool IsActive);

public sealed record SetBusinessMembersRequest(IReadOnlyList<long> MemberIds, IReadOnlyList<long>? EmployeeIds);

/// <summary>Individual capabilities for one member in one business (FRD s.5). Deny by default.</summary>
public sealed record BusinessPermissionsDto(
    bool CanViewBusiness, bool CanViewCustomers, bool CanAddCustomers, bool CanEditCustomers, bool CanViewProducts,
    bool CanViewSales, bool CanAddSales, bool CanEditOwnSales, bool CanDeleteOwnSales,
    bool CanViewExpenseTotal, bool CanAddExpenses, bool CanEditOwnExpenses, bool CanDeleteOwnExpenses, bool CanViewMemberWiseExpenses,
    bool CanRecordPayments, bool CanViewPayments, bool CanEditOwnPayments, bool CanDeleteOwnPayments,
    bool CanGenerateInvoices, bool CanViewInvoices, bool CanViewReports);

public interface IBusinessService
{
    Task<PagedResult<BusinessListItemDto>> ListAsync(BusinessListQuery query, CancellationToken ct);
    Task<IReadOnlyList<BusinessListItemDto>> ListForUserAsync(long userId, CancellationToken ct);
    Task<BusinessDetailDto> GetAsync(long id, CancellationToken ct);
    Task<BusinessDetailDto> CreateAsync(CreateBusinessRequest request, CancellationToken ct);
    Task<BusinessDetailDto> UpdateAsync(long id, UpdateBusinessRequest request, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<BusinessDetailDto> SetMembersAsync(long id, SetBusinessMembersRequest request, CancellationToken ct);
    Task<BusinessPermissionsDto> GetPermissionsAsync(long businessId, long userId, CancellationToken ct);
    Task<BusinessPermissionsDto> SetPermissionsAsync(long businessId, long userId, BusinessPermissionsDto permissions, CancellationToken ct);
}
