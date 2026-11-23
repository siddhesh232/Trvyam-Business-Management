namespace BusinessManagement.Application.Returns;

public sealed record ReturnItemRequest(long SalesOrderItemId, decimal Quantity);
public sealed record CreateReturnRequest(DateOnly ReturnDate, string? Reason, IReadOnlyList<ReturnItemRequest> Items);

public sealed record ReturnItemDto(long SalesOrderItemId, string ProductName, decimal Quantity, decimal TaxableAmount, decimal GstAmount);

/// <param name="CreditNoteId">Set when the sale had an issued tax invoice: a credit note is issued automatically.</param>
public sealed record SalesReturnDto(long Id, long SalesOrderId, DateOnly ReturnDate, decimal TaxableAmount, decimal GstAmount,
    decimal TotalAmount, string? Reason, string Status, string CreatedByName, DateTime CreatedAt,
    IReadOnlyList<ReturnItemDto> Items, long? CreditNoteId, string? CreditNoteNumber);

public interface IReturnService
{
    Task<SalesReturnDto> CreateAsync(long businessId, long saleId, CreateReturnRequest request, CancellationToken ct);
    Task<IReadOnlyList<SalesReturnDto>> ListAsync(long businessId, long saleId, CancellationToken ct);
}
