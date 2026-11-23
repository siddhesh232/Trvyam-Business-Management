using BusinessManagement.Application.Common;

namespace BusinessManagement.Application.Invoices;

public sealed record InvoiceParty(string Name, string? Gstin, string? Address, string? StateCode, string? Mobile);

public sealed record InvoiceLine(int No, string Description, string? Hsn, decimal Quantity, string Unit, decimal UnitPrice,
    decimal Discount, decimal Taxable, decimal GstRate, decimal Cgst, decimal Sgst, decimal Igst, decimal Total);

/// <summary>Frozen copy of everything printed on the invoice. Later edits to the business, customer or catalogue never change it.</summary>
public sealed record InvoiceSnapshot(string DocTitle, string InvoiceNumber, DateOnly InvoiceDate, InvoiceParty Seller, InvoiceParty Buyer,
    string? PlaceOfSupplyState, bool IsInterState, IReadOnlyList<InvoiceLine> Lines,
    decimal Subtotal, decimal TotalDiscount, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal GrandTotal,
    long SalesOrderId, DateOnly OrderDate, string? OriginalInvoiceNumber = null);

public sealed record InvoiceListQuery(DateOnly? From = null, DateOnly? To = null, string? Status = null, string? Q = null,
    int Page = 1, int PageSize = 20, string? Sort = null);

public sealed record InvoiceListItemDto(long Id, string InvoiceNumber, DateOnly InvoiceDate, string DocType, string Status,
    long SalesOrderId, string CustomerName, decimal GrandTotal);

/// <param name="Status">Issued | Cancelled | Amended</param>
public sealed record InvoiceDto(long Id, long BusinessId, string DocType, string InvoiceNumber, DateOnly InvoiceDate, string FinancialYear,
    string Status, bool IsInterState, decimal TaxableAmount, decimal Cgst, decimal Sgst, decimal Igst, decimal GrandTotal,
    long SalesOrderId, long? OriginalInvoiceId, string? CancelReason, DateTime IssuedAt, string IssuedByName, InvoiceSnapshot Snapshot);

public sealed record CancelInvoiceRequest(string Reason);

public sealed record InvoicePdf(byte[] Content, string FileName);

public interface IInvoicePdfGenerator
{
    byte[] Generate(InvoiceSnapshot invoice, bool cancelled);
}

public interface IInvoiceService
{
    Task<InvoiceDto> IssueAsync(long businessId, long saleId, CancellationToken ct);
    Task<PagedResult<InvoiceListItemDto>> ListAsync(long businessId, InvoiceListQuery query, CancellationToken ct);
    Task<InvoiceDto> GetAsync(long businessId, long invoiceId, CancellationToken ct);
    Task<InvoicePdf> GetPdfAsync(long businessId, long invoiceId, CancellationToken ct);
    Task<InvoiceDto> CancelAsync(long businessId, long invoiceId, CancelInvoiceRequest request, CancellationToken ct);
}
