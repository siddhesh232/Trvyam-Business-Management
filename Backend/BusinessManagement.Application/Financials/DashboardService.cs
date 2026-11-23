using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Expenses;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Financials;

/// <remarks>
/// A member sees only the sections their permissions allow (sales, payments, expense total, reports/profit, member-wise
/// breakdown); other sections come back null. Admin sees everything. All numbers derive from FinancialDataLoader.
/// </remarks>
public sealed class DashboardService(IAppDbContext db, IBusinessAccessService access, ICurrentUser current,
    FinancialDataLoader loader, IExpenseService expenses) : IDashboardService
{
    public async Task<DashboardDto> GetAsync(long businessId, DashboardQuery q, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.ViewBusiness, ct);
        var name = await db.Businesses.AsNoTracking().Where(b => b.Id == businessId).Select(b => b.Name).FirstAsync(ct);

        var days = (await loader.LoadDaysAsync([businessId], q.From, q.To, ct))[businessId];
        var f = FinancialMath.Aggregate(days);
        var (owed, refund) = (await loader.LoadReceivablesAsync([businessId], q.From, q.To, ct))[businessId];
        var customers = (await loader.LoadCustomerCountsAsync([businessId], ct))[businessId];

        var sales = a.Can(Permission.ViewSales)
            ? new SalesFigures(f.GrossSales, f.SalesReturns, f.NetSales, f.TotalSalesAmount, f.SalesCount, f.QuantitySold, f.OutputGst) : null;
        var payments = a.Can(Permission.ViewPayments) ? new PaymentFigures(f.AmountReceived, owed, refund) : null;
        var exp = a.Can(Permission.ViewExpenseTotal) || a.Can(Permission.ViewMemberWiseExpenses)
            ? new ExpenseFigures(f.TotalExpenses, f.ExpenseCount, f.EligibleInputGst, f.ExpensesNetOfInputGst) : null;
        var profit = a.Can(Permission.ViewReports)
            ? new ProfitFigures(f.NetSales, f.ExpensesNetOfInputGst, f.OtherAdjustments, f.NetProfit, f.OutputGst, f.EligibleInputGst, f.NetGstPayable, f.CashFlow, FinancialMath.ProfitFormula) : null;

        ExpenseByMemberDto? byMember = a.Can(Permission.ViewMemberWiseExpenses)
            ? await expenses.ByMemberAsync(businessId, new ExpenseByMemberQuery(q.From, q.To, q.Basis), ct) : null;

        IReadOnlyList<RecentSaleDto>? recentSales = null;
        if (a.Can(Permission.ViewSales))
        {
            var sq = db.SalesOrders.AsNoTracking().Where(o => o.BusinessId == businessId && o.Status == SalesOrderStatus.Active);
            if (q.From is DateOnly sf) sq = sq.Where(o => o.OrderDate >= sf);
            if (q.To is DateOnly st) sq = sq.Where(o => o.OrderDate <= st);
            recentSales = await (from o in sq.OrderByDescending(o => o.OrderDate).ThenByDescending(o => o.Id).Take(5)
                                 join c in db.Customers.IgnoreQueryFilters() on o.CustomerId equals c.Id
                                 orderby o.OrderDate descending, o.Id descending
                                 select new RecentSaleDto(o.Id, o.OrderDate, c.Name, o.FinalAmount)).ToListAsync(ct);
        }

        // Recent expenses follow the same visibility rules as the expense list itself.
        IReadOnlyList<ExpenseDto>? recentExpenses = null;
        try { recentExpenses = (await expenses.ListAsync(businessId, new ExpenseListQuery(q.From, q.To, null, null, 1, 5), ct)).Items; }
        catch (ForbiddenException) { /* no expense visibility: leave null */ }

        return new DashboardDto(businessId, name, q.From, q.To, customers, sales, payments, exp, profit, byMember, recentSales, recentExpenses);
    }

    public async Task<ConsolidatedDashboardDto> GetConsolidatedAsync(DashboardQuery q, CancellationToken ct)
    {
        if (!current.IsAdmin) throw new ForbiddenException();
        var businesses = await db.Businesses.AsNoTracking().OrderBy(b => b.Name).Select(b => new { b.Id, b.Name, b.IsActive }).ToListAsync(ct);
        var ids = businesses.Select(b => b.Id).ToList();

        var perBusiness = await loader.LoadDaysAsync(ids, q.From, q.To, ct);
        var receivables = await loader.LoadReceivablesAsync(ids, q.From, q.To, ct);
        var customers = await loader.LoadCustomerCountsAsync(ids, ct);

        var total = FinancialMath.Aggregate(perBusiness.Values.SelectMany(d => d));
        var rows = businesses.Select(b =>
        {
            var f = FinancialMath.Aggregate(perBusiness[b.Id]);
            return new BusinessSummaryRow(b.Id, b.Name, b.IsActive, f.NetSales, f.TotalExpenses, f.NetProfit, f.AmountReceived, receivables[b.Id].Outstanding);
        }).ToList();

        return new ConsolidatedDashboardDto(q.From, q.To, businesses.Count, customers.Values.Sum(),
            new SalesFigures(total.GrossSales, total.SalesReturns, total.NetSales, total.TotalSalesAmount, total.SalesCount, total.QuantitySold, total.OutputGst),
            new PaymentFigures(total.AmountReceived, receivables.Values.Sum(r => r.Outstanding), receivables.Values.Sum(r => r.RefundsDue)),
            new ExpenseFigures(total.TotalExpenses, total.ExpenseCount, total.EligibleInputGst, total.ExpensesNetOfInputGst),
            new ProfitFigures(total.NetSales, total.ExpensesNetOfInputGst, total.OtherAdjustments, total.NetProfit, total.OutputGst, total.EligibleInputGst,
                total.NetGstPayable, total.CashFlow, FinancialMath.ProfitFormula), rows);
    }
}
