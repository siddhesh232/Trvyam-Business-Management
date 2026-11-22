using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Customers;

public sealed record CustomerListQuery(string? Q = null, int Page = 1, int PageSize = 20, string? Sort = null);

/// <param name="BusinessIds">Business tags VISIBLE to the caller: Admin sees all; members see only their own businesses.</param>
public sealed record CustomerDto(long Id, string Mobile, string Name, string? Address, string? StateCode, string? Gstin,
    DateTime CreatedAt, IReadOnlyList<long> BusinessIds);

/// <summary>Find-or-create by mobile number, and tag the current business (FRD s.6).</summary>
public sealed record ResolveCustomerRequest(string Mobile, string? Name, string? Address, string? StateCode, string? Gstin);

public sealed record ResolveCustomerResponse(CustomerDto Customer, bool Created, bool NewlyLinked);

public sealed record UpdateCustomerRequest(string Name, string? Address, string? StateCode, string? Gstin);

public interface ICustomerService
{
    /// <summary>Read-only lookup by mobile; never creates or tags. Needs ViewCustomers or AddCustomers.</summary>
    Task<CustomerDto?> LookupAsync(long businessId, string mobile, CancellationToken ct);
    Task<ResolveCustomerResponse> ResolveAsync(long businessId, ResolveCustomerRequest request, CancellationToken ct);
    Task<PagedResult<CustomerDto>> ListForBusinessAsync(long businessId, CustomerListQuery query, CancellationToken ct);
    Task<CustomerDto> UpdateInBusinessAsync(long businessId, long customerId, UpdateCustomerRequest request, CancellationToken ct);
    Task<PagedResult<CustomerDto>> ListAllAsync(CustomerListQuery query, CancellationToken ct);
    Task<CustomerDto> GetAsync(long customerId, CancellationToken ct);
    Task<CustomerDto> UpdateAsync(long customerId, UpdateCustomerRequest request, CancellationToken ct);
}
