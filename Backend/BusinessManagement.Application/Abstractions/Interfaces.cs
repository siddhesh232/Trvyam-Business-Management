using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

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
    ChangeTracker ChangeTracker { get; }
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);
public sealed record RefreshTokenValue(string Raw, string Hash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    RefreshTokenValue CreateRefreshToken();
    /// <summary>SHA-256 hex of a raw refresh token. Raw tokens are never stored.</summary>
    string HashRefreshToken(string rawToken);
}

public interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string textBody, CancellationToken cancellationToken = default);
}
