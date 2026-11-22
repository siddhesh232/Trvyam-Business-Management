using System.Linq.Expressions;
using System.Text.Json;
using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessManagement.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : DbContext(options), IAppDbContext
{
    private static readonly HashSet<string> AuditIgnoredProps =
        ["PasswordHash", "TokenHash", "RowVersion", "UpdatedAt", "UpdatedBy"];

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessMember> BusinessMembers => Set<BusinessMember>();
    public DbSet<BusinessPermission> BusinessPermissions => Set<BusinessPermission>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerBusiness> CustomerBusinesses => Set<CustomerBusiness>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BusinessProduct> BusinessProducts => Set<BusinessProduct>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SalesOrderPaymentSummary> SalesOrderPaymentSummaries => Set<SalesOrderPaymentSummary>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnItem> SalesReturnItems => Set<SalesReturnItem>();
    public DbSet<InvoiceSequence> InvoiceSequences => Set<InvoiceSequence>();
    public DbSet<GstInvoice> GstInvoices => Set<GstInvoice>();
    public DbSet<FinancialAdjustment> FinancialAdjustments => Set<FinancialAdjustment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<decimal>().HavePrecision(18, 2);   // money: fixed precision, never float (FRD s.15)
        cb.Properties<DateTime>().HaveColumnType("datetime2(0)");
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Soft-deleted rows are hidden from every query unless IgnoreQueryFilters() is used.
        foreach (var et in mb.Model.GetEntityTypes().Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(et.ClrType, "e");
            var notDeleted = Expression.Equal(Expression.Property(p, nameof(ISoftDeletable.IsDeleted)), Expression.Constant(false));
            et.SetQueryFilter(Expression.Lambda(notDeleted, p));
        }
    }

    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        await Database.BeginTransactionAsync(cancellationToken);

    // ---------------------------------------------------------------- audit + soft delete
    private sealed record PendingAudit(EntityEntry Entry, AuditAction Action, string? Old, string? New);

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var now = DateTime.UtcNow;
        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            var action = entry.State switch { EntityState.Added => AuditAction.Create, EntityState.Deleted => AuditAction.Delete, _ => AuditAction.Update };
            string? oldJson = null, newJson = null;

            if (entry.Entity is not INotAudited)
            {
                if (entry.State == EntityState.Added) newJson = Serialize(entry.Properties.Select(p => (p.Metadata.Name, p.CurrentValue)));
                else if (entry.State == EntityState.Deleted) oldJson = Serialize(entry.Properties.Select(p => (p.Metadata.Name, p.OriginalValue)));
            }

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable soft)
            {
                // Convert physical delete into soft delete; financial records are never silently removed.
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                TrySet(entry, "DeletedAt", now);
                TrySet(entry, "DeletedBy", currentUser.UserId);
            }
            else if (entry.State == EntityState.Modified && entry.Entity is not INotAudited)
            {
                var changed = entry.Properties.Where(p => p.IsModified && !AuditIgnoredProps.Contains(p.Metadata.Name)).ToList();
                if (changed.Count == 0) { Stamp(entry, now); continue; }
                oldJson = Serialize(changed.Select(p => (p.Metadata.Name, p.OriginalValue)));
                newJson = Serialize(changed.Select(p => (p.Metadata.Name, p.CurrentValue)));
                var becameDeleted = changed.Any(p => p.Metadata.Name == nameof(ISoftDeletable.IsDeleted) && p.CurrentValue is true);
                if (becameDeleted) action = AuditAction.Delete;
            }

            Stamp(entry, now);
            if (entry.Entity is not INotAudited) pending.Add(new PendingAudit(entry, action, oldJson, newJson));
        }

        if (pending.Count == 0) return await base.SaveChangesAsync(cancellationToken);

        // One transaction so the data change and its audit rows commit (or fail) together.
        var ownsTx = Database.CurrentTransaction is null;
        await using var tx = ownsTx ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var affected = await base.SaveChangesAsync(cancellationToken);   // generates keys for added rows

        foreach (var p in pending)
        {
            AuditLogs.Add(new AuditLog
            {
                OccurredAt = now,
                UserId = currentUser.UserId,
                BusinessId = ResolveBusinessId(p.Entry),
                EntityName = p.Entry.Metadata.ClrType.Name,
                EntityId = ResolveEntityId(p.Entry),
                Action = p.Action,
                OldValues = p.Old,
                NewValues = p.New,
                IpAddress = currentUser.IpAddress
            });
        }
        await base.SaveChangesAsync(cancellationToken);
        if (tx is not null) await tx.CommitAsync(cancellationToken);
        return affected;
    }

    private void Stamp(EntityEntry entry, DateTime now)
    {
        var uid = currentUser.UserId;
        if (entry.State == EntityState.Added)
        {
            if (entry.Metadata.FindProperty("CreatedAt") is not null && entry.Property("CreatedAt").CurrentValue is DateTime d && d == default)
                entry.Property("CreatedAt").CurrentValue = now;
            if (uid is not null && entry.Metadata.FindProperty("CreatedBy") is not null)
            {
                var cur = entry.Property("CreatedBy").CurrentValue;
                if (cur is null || cur is long l && l == 0) entry.Property("CreatedBy").CurrentValue = uid;
            }
        }
        else if (entry.State == EntityState.Modified)
        {
            TrySet(entry, "UpdatedAt", now);
            TrySet(entry, "UpdatedBy", uid);
        }
    }

    private static void TrySet(EntityEntry entry, string name, object? value)
    {
        if (entry.Metadata.FindProperty(name) is null) return;
        entry.Property(name).CurrentValue = value;
    }

    private static string Serialize(IEnumerable<(string Name, object? Value)> values) =>
        JsonSerializer.Serialize(values.Where(v => !AuditIgnoredProps.Contains(v.Name))
            .ToDictionary(v => v.Name, v => v.Value is Enum e ? e.ToString() : v.Value));

    private static long? ResolveBusinessId(EntityEntry e)
    {
        if (e.Entity is Business b) return b.Id;
        return e.Metadata.FindProperty("BusinessId") is null ? null : e.Property("BusinessId").CurrentValue as long?;
    }

    private static long ResolveEntityId(EntityEntry e)
    {
        var id = e.Metadata.FindProperty("Id");
        if (id is not null) return Convert.ToInt64(e.Property("Id").CurrentValue ?? 0L);
        // Composite-key link tables: use the "other" key part (e.g. UserId, CustomerId, ProductId).
        var keys = e.Metadata.FindPrimaryKey()!.Properties.Select(p => p.Name).ToList();
        var name = keys.FirstOrDefault(k => k != "BusinessId") ?? keys[0];
        return Convert.ToInt64(e.Property(name).CurrentValue ?? 0L);
    }
}
