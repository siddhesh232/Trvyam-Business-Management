using BusinessManagement.Domain.Billing;
using FluentAssertions;

namespace BusinessManagement.Tests.Unit;

public class FinancialMathTests
{
    private static DayFigures Day(string date, decimal gross = 0, decimal taxable = 0, decimal gst = 0, decimal final = 0, int sales = 0, decimal qty = 0,
        decimal rTax = 0, decimal rGst = 0, decimal rQty = 0, decimal exp = 0, int expCount = 0, decimal input = 0, decimal inc = 0, decimal dec = 0, decimal received = 0) =>
        new(DateOnly.Parse(date), gross, taxable, gst, final, sales, qty, rTax, rGst, rQty, exp, expCount, input, inc, dec, received);

    [Fact]
    public void Profit_excludes_gst_from_revenue_and_eligible_input_gst_from_costs_without_double_counting()
    {
        var f = FinancialMath.Aggregate([
            Day("2026-11-02", gross: 1200m, taxable: 1070m, gst: 167.31m, final: 1237.31m, sales: 1, qty: 3m, received: 600m),
            Day("2026-11-03", exp: 590m, expCount: 1, input: 90m),
            Day("2026-11-04", rTax: 295m, rGst: 53.10m, rQty: 1m, inc: 100m, dec: 40m)]);

        f.NetSales.Should().Be(775m);                    // 1070 - 295
        f.OutputGst.Should().Be(114.21m);                // 167.31 - 53.10 : a liability, not revenue
        f.ExpensesNetOfInputGst.Should().Be(500m);       // 590 - 90 recoverable ITC
        f.OtherAdjustments.Should().Be(60m);
        f.NetProfit.Should().Be(335m);                   // 775 - 500 + 60
        f.NetGstPayable.Should().Be(24.21m);             // 114.21 - 90
        f.CashFlow.Should().Be(10m);                     // 600 received - 590 spent
        (f.GrossSales, f.SalesReturns, f.QuantitySold, f.TotalSalesAmount).Should().Be((1200m, 295m, 2m, 1237.31m));
    }

    [Fact]
    public void Splitting_a_period_never_changes_the_totals()
    {
        var days = Enumerable.Range(0, 40).Select(i => Day(new DateOnly(2026, 10, 1).AddDays(i).ToString("yyyy-MM-dd"),
            gross: 100m + i, taxable: 90m + i, gst: 10.01m, final: 100m + i, sales: 1, exp: i % 3 * 7.77m, input: i % 3 == 0 ? 1.11m : 0m, received: 5.55m)).ToList();
        var whole = FinancialMath.Aggregate(days);
        var parts = ReportBuckets.Build(ReportPeriod.Weekly, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 9))
            .Select(b => FinancialMath.Aggregate(days.Where(d => d.Date >= b.Start && d.Date <= b.End))).ToList();
        parts.Sum(p => p.NetSales).Should().Be(whole.NetSales);
        parts.Sum(p => p.NetProfit).Should().Be(whole.NetProfit);
        parts.Sum(p => p.TotalExpenses).Should().Be(whole.TotalExpenses);
        parts.Sum(p => p.OutputGst).Should().Be(whole.OutputGst);
    }

    [Fact]
    public void Empty_periods_are_all_zero() => FinancialMath.Aggregate([]).NetProfit.Should().Be(0m);

    [Fact]
    public void Buckets_are_clipped_to_the_range_start_on_monday_and_cover_every_day_once()
    {
        var weeks = ReportBuckets.Build(ReportPeriod.Weekly, new DateOnly(2026, 11, 25), new DateOnly(2026, 12, 8));   // Wed .. Tue
        weeks.Select(w => (w.Start.ToString("MM-dd"), w.End.ToString("MM-dd"))).Should().Equal(("11-25", "11-29"), ("11-30", "12-06"), ("12-07", "12-08"));
        var months = ReportBuckets.Build(ReportPeriod.Monthly, new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 2));
        months.Should().HaveCount(3);
        months[0].End.Should().Be(new DateOnly(2026, 1, 31));
        months[1].End.Should().Be(new DateOnly(2026, 2, 28));
        ReportBuckets.Build(ReportPeriod.Daily, new DateOnly(2026, 2, 27), new DateOnly(2026, 3, 2)).Should().HaveCount(4);
        ReportBuckets.Build(ReportPeriod.Custom, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)).Should().ContainSingle();
        FluentActions.Invoking(() => ReportBuckets.Build(ReportPeriod.Daily, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 1))).Should().Throw<ArgumentException>();
    }
}

public class ReturnRulesTests
{
    [Fact]
    public void Successive_returns_of_a_line_add_up_exactly_to_the_line_amount()
    {
        decimal qty = 0, amt = 0; var parts = new List<decimal>();
        foreach (var rq in new[] { 1m, 1m, 1m })
        {
            var a = ReturnRules.Prorate(100m, 3m, rq, qty, amt);
            parts.Add(a); qty += rq; amt += a;
        }
        parts.Should().Equal(33.33m, 33.33m, 33.34m);
        parts.Sum().Should().Be(100m);
    }

    [Fact]
    public void Returning_the_whole_line_at_once_gives_the_whole_amount() => ReturnRules.Prorate(590m, 2m, 2m, 0, 0).Should().Be(590m);

    [Fact]
    public void Returning_more_than_is_left_is_rejected()
    {
        FluentActions.Invoking(() => ReturnRules.Prorate(100m, 3m, 4m, 0, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ReturnRules.Prorate(100m, 3m, 2m, 2m, 66.67m)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => ReturnRules.Prorate(100m, 3m, 0m, 0, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
