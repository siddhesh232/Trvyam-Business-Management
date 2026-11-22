using BusinessManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BusinessManagement.Infrastructure.Persistence.Configurations;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("Expenses", t => t.HasCheckConstraint("CK_Exp_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [InputGstAmount] >= 0"));
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2)
            .HasComputedColumnSql("CAST(ROUND([Quantity] * [UnitPrice], 2) AS DECIMAL(18,2))", stored: true);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchasedBy).WithMany().HasForeignKey(x => x.PurchasedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(Expense.CreatedBy), nameof(Expense.UpdatedBy), nameof(Expense.DeletedBy));
        b.HasIndex(x => new { x.BusinessId, x.ExpenseDate })
            .IncludeProperties(x => new { x.PurchasedByUserId, x.CreatedBy, x.TotalAmount })
            .HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("IX_Exp_BizDate");
        b.HasIndex(x => new { x.BusinessId, x.PurchasedByUserId, x.ExpenseDate })
            .HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("IX_Exp_Purchaser");
    }
}

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("SalesOrders", t =>
        {
            t.HasCheckConstraint("CK_SO_Amounts",
                "[Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [DiscountAmount] <= [Subtotal] AND [GstAmount] >= 0 AND [FinalAmount] = [TaxableAmount] + [GstAmount]");
            t.HasCheckConstraint("CK_SO_Status", "[Status] IN (1,2)");
        });
        b.Property(x => x.PlaceOfSupplyState).HasColumnType("char(2)");
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Business).WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GstInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(SalesOrder.CreatedBy), nameof(SalesOrder.UpdatedBy), nameof(SalesOrder.DeletedBy));
        b.HasIndex(x => new { x.BusinessId, x.OrderDate }).HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("IX_SO_BizDate");
        b.HasIndex(x => x.CustomerId).HasDatabaseName("IX_SO_Customer");
        b.HasIndex(x => new { x.BusinessId, x.CreatedBy }).HasDatabaseName("IX_SO_Creator");
    }
}

public sealed class SalesOrderItemConfiguration : IEntityTypeConfiguration<SalesOrderItem>
{
    public void Configure(EntityTypeBuilder<SalesOrderItem> b)
    {
        b.ToTable("SalesOrderItems", t => t.HasCheckConstraint("CK_SOI_Amounts",
            "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [TotalAmount] = [TaxableAmount] + [GstAmount]"));
        b.Property(x => x.ProductNameSnapshot).HasMaxLength(150).IsRequired();
        b.Property(x => x.HsnCodeSnapshot).HasMaxLength(8).IsUnicode(false);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        b.Property(x => x.GstRate).HasPrecision(5, 2);
        b.HasOne(x => x.SalesOrder).WithMany(x => x.Items).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SalesOrderId).HasDatabaseName("IX_SOI_Order");
        b.HasIndex(x => x.ProductId).HasDatabaseName("IX_SOI_Product");
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Pay_Amount", "[Amount] > 0");
            t.HasCheckConstraint("CK_Pay_Reversal",
                "([Status] = 1 AND [ReversedAt] IS NULL) OR ([Status] = 2 AND [ReversedAt] IS NOT NULL AND [ReversalReason] IS NOT NULL)");
        });
        b.Property(x => x.TransactionReference).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(500);
        b.Property(x => x.ReversalReason).HasMaxLength(300);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.SalesOrder).WithMany(x => x.Payments).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(Payment.ReceivedBy), nameof(Payment.ReversedBy), nameof(Payment.UpdatedBy));
        b.HasIndex(x => x.SalesOrderId).IncludeProperties(x => new { x.Amount, x.Status }).HasDatabaseName("IX_Pay_Order");
        b.HasIndex(x => x.PaymentDate).HasDatabaseName("IX_Pay_Date");
    }
}

public sealed class SalesOrderPaymentSummaryConfiguration : IEntityTypeConfiguration<SalesOrderPaymentSummary>
{
    public void Configure(EntityTypeBuilder<SalesOrderPaymentSummary> b)
    {
        b.HasNoKey();
        // The view itself is created by a SQL migration (see AddPaymentSummaryViewAndInvoiceGuard).
        b.ToView("vw_SalesOrderPaymentSummary");
        b.Property(x => x.PaymentStatus).HasMaxLength(20);
    }
}

public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> b)
    {
        b.ToTable("SalesReturns");
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(SalesReturn.CreatedBy));
        b.HasIndex(x => new { x.BusinessId, x.ReturnDate }).HasDatabaseName("IX_SR_BizDate");
    }
}

public sealed class SalesReturnItemConfiguration : IEntityTypeConfiguration<SalesReturnItem>
{
    public void Configure(EntityTypeBuilder<SalesReturnItem> b)
    {
        b.ToTable("SalesReturnItems", t => t.HasCheckConstraint("CK_SRI_Qty", "[Quantity] > 0"));
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.HasOne(x => x.SalesReturn).WithMany(x => x.Items).HasForeignKey(x => x.SalesReturnId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesOrderItem>().WithMany().HasForeignKey(x => x.SalesOrderItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> b)
    {
        b.ToTable("InvoiceSequences");
        b.HasKey(x => new { x.BusinessId, x.FinancialYear, x.DocType });
        b.Property(x => x.FinancialYear).HasColumnType("char(7)");
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class GstInvoiceConfiguration : IEntityTypeConfiguration<GstInvoice>
{
    public void Configure(EntityTypeBuilder<GstInvoice> b)
    {
        b.ToTable("GstInvoices", t =>
        {
            t.HasCheckConstraint("CK_Inv_Tax", "([IsInterState] = 1 AND [Cgst] = 0 AND [Sgst] = 0) OR ([IsInterState] = 0 AND [Igst] = 0)");
            t.HasCheckConstraint("CK_Inv_Total", "[GrandTotal] = [TaxableAmount] + [Cgst] + [Sgst] + [Igst]");
        });
        b.Property(x => x.FinancialYear).HasColumnType("char(7)").IsRequired();
        b.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.SnapshotJson).IsRequired();
        b.Property(x => x.CancelReason).HasMaxLength(300);
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<GstInvoice>().WithMany().HasForeignKey(x => x.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(GstInvoice.IssuedBy));
        b.HasIndex(x => new { x.BusinessId, x.InvoiceNumber }).IsUnique().HasDatabaseName("UX_Inv_Number");
        b.HasIndex(x => new { x.BusinessId, x.FinancialYear, x.DocType, x.SequenceNumber }).IsUnique().HasDatabaseName("UX_Inv_Seq");
        // One active tax invoice per sales order; corrections go through credit notes / amendments.
        b.HasIndex(x => x.SalesOrderId).IsUnique().HasFilter("[DocType] = 1 AND [Status] = 1").HasDatabaseName("UX_Inv_OneActivePerOrder");
    }
}

public sealed class FinancialAdjustmentConfiguration : IEntityTypeConfiguration<FinancialAdjustment>
{
    public void Configure(EntityTypeBuilder<FinancialAdjustment> b)
    {
        b.ToTable("FinancialAdjustments", t => t.HasCheckConstraint("CK_FA_Amount", "[Amount] > 0"));
        b.Property(x => x.Description).HasMaxLength(400).IsRequired();
        b.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        b.UserFk(nameof(FinancialAdjustment.CreatedBy), nameof(FinancialAdjustment.DeletedBy));
        b.HasIndex(x => new { x.BusinessId, x.AdjustmentDate }).HasFilter(ConfigurationHelpers.NotDeleted).HasDatabaseName("IX_FA_BizDate");
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.OccurredAt).HasColumnType("datetime2(3)");
        b.Property(x => x.EntityName).HasMaxLength(60).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.EntityName, x.EntityId }).HasDatabaseName("IX_Audit_Entity");
        b.HasIndex(x => new { x.BusinessId, x.OccurredAt }).HasDatabaseName("IX_Audit_BizTime");
    }
}
