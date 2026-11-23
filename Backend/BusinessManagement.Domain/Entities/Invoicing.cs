using BusinessManagement.Domain.Common;
using BusinessManagement.Domain.Enums;

namespace BusinessManagement.Domain.Entities;

public class SalesReturn
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SalesOrderId { get; set; }
    public DateOnly ReturnDate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Reason { get; set; }
    public SalesReturnStatus Status { get; set; } = SalesReturnStatus.Accepted;
    /// <summary>Credit note issued for this return (only when the sale had an issued tax invoice).</summary>
    public long? CreditNoteId { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<SalesReturnItem> Items { get; set; } = [];
}

public class SalesReturnItem
{
    public long Id { get; set; }
    public long SalesReturnId { get; set; }
    public long SalesOrderItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public SalesReturn? SalesReturn { get; set; }
}

/// <summary>Counter row, updated inside the invoice-issuing transaction; not audited (invoice rows are).</summary>
public class InvoiceSequence : INotAudited
{
    public long BusinessId { get; set; }
    public string FinancialYear { get; set; } = string.Empty;
    public InvoiceDocType DocType { get; set; }
    public int LastNumber { get; set; }
}

public class GstInvoice
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long SalesOrderId { get; set; }
    public InvoiceDocType DocType { get; set; } = InvoiceDocType.TaxInvoice;
    public string FinancialYear { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public bool IsInterState { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal Cgst { get; set; }
    public decimal Sgst { get; set; }
    public decimal Igst { get; set; }
    public decimal GrandTotal { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Issued;
    public long? OriginalInvoiceId { get; set; }
    public string? CancelReason { get; set; }
    public long IssuedBy { get; set; }
    public DateTime IssuedAt { get; set; }
}
