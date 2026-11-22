using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BusinessManagement.Application.Abstractions;

/// <summary>The authenticated caller, resolved from the JWT. Used for audit stamping and authorization.</summary>
public interface ICurrentUser
{
    long? UserId { get; }
    UserRole? Role { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    string? IpAddress { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<Business> Businesses { get; }
    DbSet<BusinessMember> BusinessMembers { get; }
    DbSet<BusinessPermission> BusinessPermissions { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerBusiness> CustomerBusinesses { get; }
    DbSet<Product> Products { get; }
    DbSet<BusinessProduct> BusinessProducts { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesOrderItem> SalesOrderItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<SalesOrderPaymentSummary> SalesOrderPaymentSummaries { get; }
    DbSet<SalesReturn> SalesReturns { get; }
    DbSet<SalesReturnItem> SalesReturnItems { get; }
    DbSet<InvoiceSequence> InvoiceSequences { get; }
    DbSet<GstInvoice> GstInvoices { get; }
    DbSet<FinancialAdjustment> FinancialAdjustments { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
