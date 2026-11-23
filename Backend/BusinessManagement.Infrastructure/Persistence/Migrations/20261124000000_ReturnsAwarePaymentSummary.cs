using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessManagement.Infrastructure.Persistence.Migrations;

/// <summary>
/// SQL-only migration: the calculated payment summary now measures what is DUE after accepted sales returns.
/// Outstanding = Final - Returns - Received (negative means a refund is owed); status is computed against the net due.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261124000000_ReturnsAwarePaymentSummary")]
public partial class ReturnsAwarePaymentSummary : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.vw_SalesOrderPaymentSummary AS
SELECT so.Id AS SalesOrderId, so.BusinessId, so.FinalAmount,
       ISNULL(pay.Received, 0) AS TotalReceived,
       so.FinalAmount - ISNULL(ret.Returned, 0) - ISNULL(pay.Received, 0) AS Outstanding,
       CASE WHEN ISNULL(pay.Received, 0) >= so.FinalAmount - ISNULL(ret.Returned, 0) THEN 'Paid'
            WHEN ISNULL(pay.Received, 0) <= 0 THEN 'Unpaid'
            ELSE 'PartiallyPaid' END AS PaymentStatus
FROM dbo.SalesOrders so
OUTER APPLY (SELECT SUM(p.Amount) AS Received FROM dbo.Payments p WHERE p.SalesOrderId = so.Id AND p.Status = 1) pay
OUTER APPLY (SELECT SUM(r.TotalAmount) AS Returned FROM dbo.SalesReturns r WHERE r.SalesOrderId = so.Id AND r.Status = 1) ret
WHERE so.IsDeleted = 0 AND so.Status = 1;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE OR ALTER VIEW dbo.vw_SalesOrderPaymentSummary AS
SELECT so.Id AS SalesOrderId, so.BusinessId, so.FinalAmount,
       ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) AS TotalReceived,
       so.FinalAmount - ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) AS Outstanding,
       CASE WHEN ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) = 0 THEN 'Unpaid'
            WHEN ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) < so.FinalAmount THEN 'PartiallyPaid'
            ELSE 'Paid' END AS PaymentStatus
FROM dbo.SalesOrders so LEFT JOIN dbo.Payments p ON p.SalesOrderId = so.Id
WHERE so.IsDeleted = 0 AND so.Status = 1
GROUP BY so.Id, so.BusinessId, so.FinalAmount;");
    }
}
