using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Financials;

/// <summary>Daily / weekly / monthly / custom period reports (FRD s.12). Needs the ViewReports permission (Admin always).
/// Buckets are clipped to the requested range and are plain sums, so they add up exactly to the totals row.</summary>
public sealed class ReportService(IAppDbContext db, IBusinessAccessService access, FinancialDataLoader loader, TimeProvider clock) : IReportService
{
    public async Task<ReportDto> GetAsync(long businessId, ReportQuery q, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.ViewReports, ct);
        if (!Enum.TryParse<ReportPeriod>(q.Period ?? "monthly", true, out var period) || !Enum.IsDefined(period))
            throw Fail.Validation("period", "Period must be daily, weekly, monthly or custom.");

        var today = InvoiceRules.TodayIst(clock.GetUtcNow().UtcDateTime);
        DateOnly from, to;
        if (period == ReportPeriod.Custom)
        {
            if (q.From is not DateOnly cf || q.To is not DateOnly ct2) throw Fail.Validation("from", "A custom report needs both 'from' and 'to'.");
            (from, to) = (cf, ct2);
        }
        else
        {
            to = q.To ?? today;
            from = q.From ?? period switch
            {
                ReportPeriod.Daily => to.AddDays(-29),
                ReportPeriod.Weekly => to.AddDays(-7 * 12 + 1),
                _ => new DateOnly(to.Year, to.Month, 1).AddMonths(-11)
            };
        }
        if (to < from) throw Fail.Validation("to", "'To' must not be before 'From'.");
        if (to.DayNumber - from.DayNumber + 1 > ReportBuckets.MaxDays) throw Fail.Validation("to", "The report range is too long (maximum 5 years).");
        var ranges = ReportBuckets.Build(period, from, to);
        if (period == ReportPeriod.Daily && ranges.Count > 366) throw Fail.Validation("to", "A daily report can cover at most 366 days.");

        var days = (await loader.LoadDaysAsync([businessId], from, to, ct))[businessId];

        var buckets = ranges.Select(r => ToDto(r, FinancialMath.Aggregate(days.Where(d => d.Date >= r.Start && d.Date <= r.End)))).ToList();
        var totals = ToDto(new ReportBucketRange(from, to, $"{from:dd MMM yyyy} - {to:dd MMM yyyy}"), FinancialMath.Aggregate(days));
        return new ReportDto(businessId, period.ToString(), from, to, buckets, totals, FinancialMath.ProfitFormula);
    }

    private static ReportBucketDto ToDto(ReportBucketRange r, PeriodFigures f) =>
        new(r.Start, r.End, r.Label, f.GrossSales, f.SalesReturns, f.NetSales, f.SalesCount, f.OutputGst, f.TotalExpenses,
            f.EligibleInputGst, f.OtherAdjustments, f.AmountReceived, f.NetProfit, f.NetGstPayable);
}
