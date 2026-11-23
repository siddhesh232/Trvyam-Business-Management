using BusinessManagement.Application.Abstractions;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Financials;

/// <summary>
/// Reads the ledgers once per source and folds them into per-business, per-day figures. Dashboards, reports and the
/// consolidated view all derive from these same rows, so they reconcile by construction.
/// Dates are inclusive. Soft-deleted records are excluded by the global query filters.
/// </summary>
public sealed class FinancialDataLoader(IAppDbContext db)
{
    private sealed class Cell
    {
        public decimal Gross, Taxable, Gst, Final, Qty, RTaxable, RGst, RQty, Exp, Input, Inc, Dec, Received;
        public int Sales, ExpCount;
    }

    public async Task<Dictionary<long, List<DayFigures>>> LoadDaysAsync(IReadOnlyCollection<long> ids, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var cells = new Dictionary<(long, DateOnly), Cell>();
        Cell At(long bid, DateOnly d) { if (!cells.TryGetValue((bid, d), out var c)) cells[(bid, d)] = c = new Cell(); return c; }

        // ---- sales (active orders by order date)
        var so = db.SalesOrders.AsNoTracking().Where(o => ids.Contains(o.BusinessId) && o.Status == SalesOrderStatus.Active);
        if (from is DateOnly f1) so = so.Where(o => o.OrderDate >= f1);
        if (to is DateOnly t1) so = so.Where(o => o.OrderDate <= t1);
        foreach (var r in await so.GroupBy(o => new { o.BusinessId, o.OrderDate }).Select(g => new
        { g.Key.BusinessId, g.Key.OrderDate, Gross = g.Sum(x => x.Subtotal), Taxable = g.Sum(x => x.TaxableAmount), Gst = g.Sum(x => x.GstAmount), Final = g.Sum(x => x.FinalAmount), Count = g.Count() }).ToListAsync(ct))
        { var c = At(r.BusinessId, r.OrderDate); c.Gross += r.Gross; c.Taxable += r.Taxable; c.Gst += r.Gst; c.Final += r.Final; c.Sales += r.Count; }

        var items = db.SalesOrderItems.AsNoTracking().Where(i => ids.Contains(i.SalesOrder!.BusinessId) && i.SalesOrder.Status == SalesOrderStatus.Active);
        if (from is DateOnly f2) items = items.Where(i => i.SalesOrder!.OrderDate >= f2);
        if (to is DateOnly t2) items = items.Where(i => i.SalesOrder!.OrderDate <= t2);
        foreach (var r in await items.GroupBy(i => new { i.SalesOrder!.BusinessId, i.SalesOrder.OrderDate }).Select(g => new { g.Key.BusinessId, g.Key.OrderDate, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            At(r.BusinessId, r.OrderDate).Qty += r.Qty;

        // ---- returns (accepted, by return date)
        var rets = db.SalesReturns.AsNoTracking().Where(r => ids.Contains(r.BusinessId) && r.Status == SalesReturnStatus.Accepted);
        if (from is DateOnly f3) rets = rets.Where(r => r.ReturnDate >= f3);
        if (to is DateOnly t3) rets = rets.Where(r => r.ReturnDate <= t3);
        foreach (var r in await rets.GroupBy(x => new { x.BusinessId, x.ReturnDate }).Select(g => new { g.Key.BusinessId, g.Key.ReturnDate, Taxable = g.Sum(x => x.TaxableAmount), Gst = g.Sum(x => x.GstAmount) }).ToListAsync(ct))
        { var c = At(r.BusinessId, r.ReturnDate); c.RTaxable += r.Taxable; c.RGst += r.Gst; }

        var retItems = db.SalesReturnItems.AsNoTracking().Where(i => i.SalesReturn!.Status == SalesReturnStatus.Accepted && ids.Contains(i.SalesReturn.BusinessId));
        if (from is DateOnly f4) retItems = retItems.Where(i => i.SalesReturn!.ReturnDate >= f4);
        if (to is DateOnly t4) retItems = retItems.Where(i => i.SalesReturn!.ReturnDate <= t4);
        foreach (var r in await retItems.GroupBy(i => new { i.SalesReturn!.BusinessId, i.SalesReturn.ReturnDate }).Select(g => new { g.Key.BusinessId, g.Key.ReturnDate, Qty = g.Sum(x => x.Quantity) }).ToListAsync(ct))
            At(r.BusinessId, r.ReturnDate).RQty += r.Qty;

        // ---- expenses (by expense date)
        var ex = db.Expenses.AsNoTracking().Where(e => ids.Contains(e.BusinessId));
        if (from is DateOnly f5) ex = ex.Where(e => e.ExpenseDate >= f5);
        if (to is DateOnly t5) ex = ex.Where(e => e.ExpenseDate <= t5);
        foreach (var r in await ex.GroupBy(e => new { e.BusinessId, e.ExpenseDate }).Select(g => new
        { g.Key.BusinessId, g.Key.ExpenseDate, Total = g.Sum(x => x.TotalAmount), Count = g.Count(), Input = g.Sum(x => x.IsInputGstEligible ? x.InputGstAmount : 0m) }).ToListAsync(ct))
        { var c = At(r.BusinessId, r.ExpenseDate); c.Exp += r.Total; c.ExpCount += r.Count; c.Input += r.Input; }

        // ---- payments received (valid only, by payment date)
        var pay = db.Payments.AsNoTracking().Where(p => p.Status == PaymentRecordStatus.Valid && ids.Contains(p.SalesOrder!.BusinessId) && p.SalesOrder.Status == SalesOrderStatus.Active);
        if (from is DateOnly f6) pay = pay.Where(p => p.PaymentDate >= f6);
        if (to is DateOnly t6) pay = pay.Where(p => p.PaymentDate <= t6);
        foreach (var r in await pay.GroupBy(p => new { p.SalesOrder!.BusinessId, p.PaymentDate }).Select(g => new { g.Key.BusinessId, g.Key.PaymentDate, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct))
            At(r.BusinessId, r.PaymentDate).Received += r.Amount;

        // ---- other adjustments
        var adj = db.FinancialAdjustments.AsNoTracking().Where(a => ids.Contains(a.BusinessId));
        if (from is DateOnly f7) adj = adj.Where(a => a.AdjustmentDate >= f7);
        if (to is DateOnly t7) adj = adj.Where(a => a.AdjustmentDate <= t7);
        foreach (var r in await adj.GroupBy(a => new { a.BusinessId, a.AdjustmentDate }).Select(g => new
        {
            g.Key.BusinessId, g.Key.AdjustmentDate,
            Inc = g.Sum(x => x.Direction == AdjustmentDirection.IncreasesProfit ? x.Amount : 0m),
            Dec = g.Sum(x => x.Direction == AdjustmentDirection.DecreasesProfit ? x.Amount : 0m)
        }).ToListAsync(ct))
        { var c = At(r.BusinessId, r.AdjustmentDate); c.Inc += r.Inc; c.Dec += r.Dec; }

        var result = ids.ToDictionary(i => i, _ => new List<DayFigures>());
        foreach (var ((bid, date), c) in cells.OrderBy(k => k.Key.Item2))
            result[bid].Add(new DayFigures(date, c.Gross, c.Taxable, c.Gst, c.Final, c.Sales, c.Qty, c.RTaxable, c.RGst, c.RQty,
                c.Exp, c.ExpCount, c.Input, c.Inc, c.Dec, c.Received));
        return result;
    }

    /// <summary>Receivables on sales dated in the period, measured against the amount due after accepted returns.</summary>
    public async Task<Dictionary<long, (decimal Outstanding, decimal RefundsDue)>> LoadReceivablesAsync(IReadOnlyCollection<long> ids, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = from ps in db.SalesOrderPaymentSummaries.AsNoTracking()
                join o in db.SalesOrders.AsNoTracking() on ps.SalesOrderId equals o.Id
                where ids.Contains(ps.BusinessId)
                select new { ps.BusinessId, o.OrderDate, ps.Outstanding };
        if (from is DateOnly f) q = q.Where(x => x.OrderDate >= f);
        if (to is DateOnly t) q = q.Where(x => x.OrderDate <= t);
        var rows = await q.GroupBy(x => x.BusinessId).Select(g => new
        {
            BusinessId = g.Key,
            Owed = g.Sum(x => x.Outstanding > 0 ? x.Outstanding : 0m),
            Refund = g.Sum(x => x.Outstanding < 0 ? -x.Outstanding : 0m)
        }).ToListAsync(ct);
        var dict = ids.ToDictionary(i => i, _ => (0m, 0m));
        foreach (var r in rows) dict[r.BusinessId] = (r.Owed, r.Refund);
        return dict;
    }

    public async Task<Dictionary<long, int>> LoadCustomerCountsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var rows = await db.CustomerBusinesses.AsNoTracking().Where(t => ids.Contains(t.BusinessId))
            .GroupBy(t => t.BusinessId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var dict = ids.ToDictionary(i => i, _ => 0);
        foreach (var r in rows) dict[r.Key] = r.Count;
        return dict;
    }
}
