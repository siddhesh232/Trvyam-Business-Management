using BusinessManagement.Application.Expenses;

namespace BusinessManagement.Application.Financials;

public sealed record DashboardQuery(DateOnly? From = null, DateOnly? To = null, string? Basis = null);

/// <param name="GrossSales">Order value before discounts and GST.</param>
/// <param name="SalesReturns">Taxable value of accepted returns.</param>
/// <param name="NetSales">Sales excluding GST, after discounts and returns.</param>
/// <param name="TotalSalesAmount">GST-inclusive value of sales in the period (before returns).</param>
public sealed record SalesFigures(decimal GrossSales, decimal SalesReturns, decimal NetSales, decimal TotalSalesAmount,
    int SalesCount, decimal QuantitySold, decimal OutputGst);

/// <param name="AmountReceived">Valid payments received in the period.</param>
/// <param name="OutstandingAmount">Still to collect on sales dated in the period (after returns).</param>
/// <param name="RefundsDue">Received in excess of what is due after returns, on sales dated in the period.</param>
public sealed record PaymentFigures(decimal AmountReceived, decimal OutstandingAmount, decimal RefundsDue);

/// <param name="TotalExpenses">All recorded expenses; reconciles with /expenses/summary and the member-wise breakdown.</param>
public sealed record ExpenseFigures(decimal TotalExpenses, int Count, decimal EligibleInputGst, decimal ExpensesNetOfInputGst);

public sealed record ProfitFigures(decimal NetSales, decimal ExpensesNetOfInputGst, decimal OtherAdjustments, decimal NetProfit,
    decimal OutputGst, decimal EligibleInputGst, decimal NetGstPayable, decimal CashFlow, string Formula);

public sealed record RecentSaleDto(long Id, DateOnly OrderDate, string CustomerName, decimal FinalAmount);

/// <remarks>Sections are null when the caller lacks the matching permission (never zero-filled).</remarks>
public sealed record DashboardDto(long BusinessId, string BusinessName, DateOnly? From, DateOnly? To, int TotalCustomers,
    SalesFigures? Sales, PaymentFigures? Payments, ExpenseFigures? Expenses, ProfitFigures? Profit,
    ExpenseByMemberDto? ExpenseByMember, IReadOnlyList<RecentSaleDto>? RecentSales, IReadOnlyList<ExpenseDto>? RecentExpenses);

public sealed record BusinessSummaryRow(long BusinessId, string Name, bool IsActive, decimal NetSales, decimal TotalExpenses,
    decimal NetProfit, decimal AmountReceived, decimal OutstandingAmount);

public sealed record ConsolidatedDashboardDto(DateOnly? From, DateOnly? To, int BusinessCount, int TotalCustomers,
    SalesFigures Sales, PaymentFigures Payments, ExpenseFigures Expenses, ProfitFigures Profit, IReadOnlyList<BusinessSummaryRow> Businesses);

/// <param name="Period">daily | weekly | monthly | custom</param>
public sealed record ReportQuery(string? Period = null, DateOnly? From = null, DateOnly? To = null);

public sealed record ReportBucketDto(DateOnly Start, DateOnly End, string Label, decimal GrossSales, decimal SalesReturns, decimal NetSales,
    int SalesCount, decimal OutputGst, decimal TotalExpenses, decimal EligibleInputGst, decimal OtherAdjustments, decimal AmountReceived,
    decimal NetProfit, decimal NetGstPayable);

public sealed record ReportDto(long BusinessId, string Period, DateOnly From, DateOnly To, IReadOnlyList<ReportBucketDto> Buckets,
    ReportBucketDto Totals, string ProfitFormula);

public interface IDashboardService
{
    Task<DashboardDto> GetAsync(long businessId, DashboardQuery query, CancellationToken ct);
    Task<ConsolidatedDashboardDto> GetConsolidatedAsync(DashboardQuery query, CancellationToken ct);
}

public interface IReportService
{
    Task<ReportDto> GetAsync(long businessId, ReportQuery query, CancellationToken ct);
}
