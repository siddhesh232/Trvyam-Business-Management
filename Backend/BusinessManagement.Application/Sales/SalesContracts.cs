using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Sales;

/// <param name="PaymentStatus">Unpaid | PartiallyPaid | Paid</param>
public sealed record SalesListQuery(DateOnly? From = null, DateOnly? To = null, long? CustomerId = null, long? ProductId = null,
    string? PaymentStatus = null, string? Q = null, int Page = 1, int PageSize = 20, string? Sort = null);

public sealed record SalesListItemDto(long Id, DateOnly OrderDate, long CustomerId, string CustomerName, string CustomerMobile,
    decimal FinalAmount, decimal TotalReceived, decimal Outstanding, string PaymentStatus, long CreatedBy, string CreatedByName, int ItemCount);

public sealed record SalesCustomerDto(long Id, string Name, string Mobile, string? Address, string? StateCode, string? Gstin);

public sealed record SalesItemDto(long Id, long ProductId, string ProductName, string? HsnCode, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountAmount, decimal GstRate, decimal TaxableAmount, decimal GstAmount, decimal TotalAmount);

public sealed record SalesOrderDto(long Id, long BusinessId, DateOnly OrderDate, SalesCustomerDto Customer,
    decimal Subtotal, decimal DiscountAmount, decimal TaxableAmount, decimal GstAmount, decimal FinalAmount, string? PlaceOfSupplyState,
    decimal TotalReceived, decimal Outstanding, string PaymentStatus, long? InvoiceId,
    long CreatedBy, string CreatedByName, DateTime CreatedAt, IReadOnlyList<SalesItemDto> Items, bool CanEdit, bool CanDelete);

/// <param name="UnitPrice">Optional price override. Members always sell at the catalogue price; only the Admin may override.</param>
/// <param name="ItemDiscount">Discount on this line (amount, before GST).</param>
public sealed record SaleItemRequest(long ProductId, decimal Quantity, decimal? UnitPrice, decimal ItemDiscount);

/// <param name="OrderDiscount">Discount on the whole order (amount, before GST); spread over the lines pro rata.</param>
public sealed record CreateSaleRequest(long CustomerId, DateOnly OrderDate, IReadOnlyList<SaleItemRequest> Items, decimal OrderDiscount);

public sealed record UpdateSaleRequest(DateOnly OrderDate, IReadOnlyList<SaleItemRequest> Items, decimal OrderDiscount);

public interface ISalesService
{
    Task<PagedResult<SalesListItemDto>> ListAsync(long businessId, SalesListQuery query, CancellationToken ct);
    Task<SalesOrderDto> GetAsync(long businessId, long id, CancellationToken ct);
    Task<SalesOrderDto> CreateAsync(long businessId, CreateSaleRequest request, CancellationToken ct);
    Task<SalesOrderDto> UpdateAsync(long businessId, long id, UpdateSaleRequest request, CancellationToken ct);
    Task DeleteAsync(long businessId, long id, CancellationToken ct);
}
