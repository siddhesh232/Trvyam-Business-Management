using BusinessManagement.Domain.Common;

namespace BusinessManagement.Domain.Entities;

public class Expense : ISoftDeletable
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public DateOnly ExpenseDate { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    /// <summary>Database-computed persisted column: ROUND(Quantity * UnitPrice, 2). Read-only in code.</summary>
    public decimal TotalAmount { get; private set; }
    public decimal InputGstAmount { get; set; }
    public bool IsInputGstEligible { get; set; }
    /// <summary>Member who made the purchase.</summary>
    public long PurchasedByUserId { get; set; }
    /// <summary>User who entered the record (may differ from purchaser).</summary>
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public long? DeletedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Business? Business { get; set; }
    public User? PurchasedBy { get; set; }

    public static decimal ComputeTotal(decimal quantity, decimal unitPrice) =>
        Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
}
