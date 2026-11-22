using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// SQL-only migration (no model change): creates the calculated payment-status view and enforces
/// issued-invoice immutability at database level (FRD s.10, s.11, s.15).
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261123000000_AddPaymentSummaryViewAndInvoiceGuard")]
public partial class AddPaymentSummaryViewAndInvoiceGuard : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE VIEW dbo.vw_SalesOrderPaymentSummary AS
SELECT so.Id AS SalesOrderId, so.BusinessId, so.FinalAmount,
       ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) AS TotalReceived,
       so.FinalAmount - ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) AS Outstanding,
       CASE WHEN ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) = 0 THEN 'Unpaid'
            WHEN ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) < so.FinalAmount THEN 'PartiallyPaid'
            ELSE 'Paid' END AS PaymentStatus
FROM dbo.SalesOrders so LEFT JOIN dbo.Payments p ON p.SalesOrderId = so.Id
WHERE so.IsDeleted = 0 AND so.Status = 1
GROUP BY so.Id, so.BusinessId, so.FinalAmount;");

        migrationBuilder.Sql(@"
CREATE TRIGGER dbo.TR_GstInvoices_Immutable ON dbo.GstInvoices AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id WHERE
           i.BusinessId <> d.BusinessId OR i.SalesOrderId <> d.SalesOrderId OR i.DocType <> d.DocType
        OR i.FinancialYear <> d.FinancialYear OR i.SequenceNumber <> d.SequenceNumber OR i.InvoiceNumber <> d.InvoiceNumber
        OR i.InvoiceDate <> d.InvoiceDate OR i.IsInterState <> d.IsInterState OR i.TaxableAmount <> d.TaxableAmount
        OR i.Cgst <> d.Cgst OR i.Sgst <> d.Sgst OR i.Igst <> d.Igst OR i.GrandTotal <> d.GrandTotal
        OR i.SnapshotJson <> d.SnapshotJson OR ISNULL(i.OriginalInvoiceId, 0) <> ISNULL(d.OriginalInvoiceId, 0)
        OR i.IssuedBy <> d.IssuedBy OR i.IssuedAt <> d.IssuedAt)
    BEGIN
        THROW 51000, 'Issued invoices are immutable. Use cancellation or a credit note.', 1;
    END
END");

        migrationBuilder.Sql(@"
CREATE TRIGGER dbo.TR_GstInvoices_NoDelete ON dbo.GstInvoices INSTEAD OF DELETE AS
BEGIN
    THROW 51001, 'Invoices cannot be deleted. Use cancellation or a credit note.', 1;
END");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_GstInvoices_NoDelete;");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_GstInvoices_Immutable;");
        migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_SalesOrderPaymentSummary;");
    }
}
