using BusinessManagement.Domain.Common;
using BusinessManagement.Domain.Enums;

namespace BusinessManagement.Domain.Entities;

public class SalesOrder : ISoftDeletable
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long CustomerId { get; set; }
    public DateOnly OrderDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal FinalAmount { get; set; }
    public string? PlaceOfSupplyState { get; set; }
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Active;
    public long? InvoiceId { get; set; }
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public long? DeletedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Business? Business { get; set; }
    public Customer? Customer { get; set; }
    public ICollection<SalesOrderItem> Items { get; set; } = [];
    public ICollection<Payment> Payments { get; set; } = [];
}

public class SalesOrderItem
{
    public long Id { get; set; }
    public long SalesOrderId { get; set; }
    public long ProductId { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string? HsnCodeSnapshot { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public Product? Product { get; set; }
}

public class Payment
{
    public long Id { get; set; }
    public long SalesOrderId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMode PaymentMode { get; set; }
    public string? TransactionReference { get; set; }
    public long ReceivedBy { get; set; }
    public string? Notes { get; set; }
    public PaymentRecordStatus Status { get; set; } = PaymentRecordStatus.Valid;
    public DateTime? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public SalesOrder? SalesOrder { get; set; }
}

/// <summary>Read model over vw_SalesOrderPaymentSummary. Payment status is calculated, never stored (FRD s.10).</summary>
public class SalesOrderPaymentSummary
{
    public long SalesOrderId { get; set; }
    public long BusinessId { get; set; }
    public decimal FinalAmount { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal Outstanding { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}
