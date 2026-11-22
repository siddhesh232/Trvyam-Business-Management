using BusinessManagement.Domain.Common;
using BusinessManagement.Domain.Enums;

namespace BusinessManagement.Domain.Entities;

public class FinancialAdjustment : ISoftDeletable
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public DateOnly AdjustmentDate { get; set; }
    public AdjustmentType Type { get; set; }
    public AdjustmentDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public long? DeletedBy { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class AuditLog : INotAudited
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public long? UserId { get; set; }
    public long? BusinessId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public long EntityId { get; set; }
    public AuditAction Action { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string? IpAddress { get; set; }
}
