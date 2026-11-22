namespace BusinessManagement.Domain.Common;

/// <summary>Entities that are never physically removed; deletion sets IsDeleted (FRD s.15).</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

/// <summary>Opt-out marker: entity changes are not written to AuditLogs (tokens, the audit log itself).</summary>
public interface INotAudited;
