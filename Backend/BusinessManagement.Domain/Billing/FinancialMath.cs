namespace BusinessManagement.Domain.Billing;

/// <summary>One day's raw ledger totals for one business (no derived numbers).</summary>
public sealed record DayFigures(
    DateOnly Date,
    decimal GrossSales,          // sum of order subtotals: value before discount and GST
    decimal SalesTaxable,        // value after discounts, excluding GST
    decimal SalesGst,
    decimal SalesFinal,          // GST-inclusive invoice value
    int SalesCount,
    decimal QuantitySold,
    decimal ReturnsTaxable,
    decimal ReturnsGst,
    decimal ReturnsQuantity,
    decimal ExpensesTotal,       // as recorded (GST-inclusive where GST was paid)
    int ExpenseCount,
    decimal EligibleInputGst,    // input tax credit on eligible expenses
    decimal AdjustmentsIncrease,
    decimal AdjustmentsDecrease,
    decimal Received);           // valid payments received that day

/// <summary>Derived, reconcilable figures for any set of days (FRD s.12).</summary>
public sealed record PeriodFigures(
    decimal GrossSales, decimal SalesReturns, decimal NetSales, decimal TotalSalesAmount, int SalesCount, decimal QuantitySold,
    decimal OutputGst, decimal TotalExpenses, int ExpenseCount, decimal EligibleInputGst, decimal ExpensesNetOfInputGst,
    decimal OtherAdjustments, decimal NetProfit, decimal NetGstPayable, decimal AmountReceived, decimal CashFlow);

public static class FinancialMath
{
    public const string ProfitFormula =
        "Net profit = Net sales (excl. GST, after discounts and returns) - Expenses excluding eligible input GST + Other adjustments. " +
        "GST collected is a liability, not revenue; eligible input GST is recoverable, not a cost.";

    /// <remarks>
    /// Net sales = taxable sales - taxable returns. Output GST = GST on sales - GST on returns.
    /// Net GST payable = output GST - eligible input GST (negative means credit). Cash flow = money received - expenses paid.
    /// Every figure is a plain sum over days, so any split of a period into sub-periods adds back to the whole.
    /// </remarks>
    public static PeriodFigures Aggregate(IEnumerable<DayFigures> days)
    {
        decimal gross = 0, taxable = 0, gst = 0, final = 0, qty = 0, rTaxable = 0, rGst = 0, rQty = 0;
        decimal exp = 0, input = 0, inc = 0, dec = 0, received = 0;
        int sales = 0, expCount = 0;
        foreach (var d in days)
        {
            gross += d.GrossSales; taxable += d.SalesTaxable; gst += d.SalesGst; final += d.SalesFinal; sales += d.SalesCount; qty += d.QuantitySold;
            rTaxable += d.ReturnsTaxable; rGst += d.ReturnsGst; rQty += d.ReturnsQuantity;
            exp += d.ExpensesTotal; expCount += d.ExpenseCount; input += d.EligibleInputGst;
            inc += d.AdjustmentsIncrease; dec += d.AdjustmentsDecrease; received += d.Received;
        }
        var netSales = taxable - rTaxable;
        var outputGst = gst - rGst;
        var expNet = exp - input;
        var adjustments = inc - dec;
        return new PeriodFigures(gross, rTaxable, netSales, final, sales, qty - rQty, outputGst, exp, expCount, input, expNet,
            adjustments, netSales - expNet + adjustments, outputGst - input, received, received - exp);
    }
}

public enum ReportPeriod { Daily, Weekly, Monthly, Custom }

public sealed record ReportBucketRange(DateOnly Start, DateOnly End, string Label);

public static class ReportBuckets
{
    public const int MaxDays = 366 * 5;

    /// <summary>Splits [from,to] into calendar buckets, clipped to the range so the buckets always add up to the whole range.
    /// Weeks start on Monday.</summary>
    public static IReadOnlyList<ReportBucketRange> Build(ReportPeriod period, DateOnly from, DateOnly to)
    {
        if (to < from) throw new ArgumentException("'to' must not be before 'from'.");
        var list = new List<ReportBucketRange>();
        switch (period)
        {
            case ReportPeriod.Custom:
                list.Add(new ReportBucketRange(from, to, $"{from:dd MMM yyyy} - {to:dd MMM yyyy}"));
                break;
            case ReportPeriod.Daily:
                for (var d = from; d <= to; d = d.AddDays(1)) list.Add(new ReportBucketRange(d, d, d.ToString("dd MMM yyyy")));
                break;
            case ReportPeriod.Weekly:
                for (var start = from; start <= to;)
                {
                    var daysToSunday = ((int)DayOfWeek.Sunday - (int)start.DayOfWeek + 7) % 7;
                    var end = start.AddDays(daysToSunday);
                    if (end > to) end = to;
                    list.Add(new ReportBucketRange(start, end, $"{start:dd MMM} - {end:dd MMM yyyy}"));
                    start = end.AddDays(1);
                }
                break;
            case ReportPeriod.Monthly:
                for (var start = from; start <= to;)
                {
                    var end = new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
                    if (end > to) end = to;
                    list.Add(new ReportBucketRange(start, end, start.ToString("MMM yyyy")));
                    start = end.AddDays(1);
                }
                break;
        }
        return list;
    }
}
