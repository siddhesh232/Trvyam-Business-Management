using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Expenses;

public sealed class ExpenseService(IAppDbContext db, IBusinessAccessService access, TimeProvider clock) : IExpenseService
{
    // ------------------------------------------------------------------ list
    /// <remarks>
    /// Who sees which records: Admin and members with ViewMemberWiseExpenses see every record (including who bought what).
    /// Other members see only records they created or purchased, so the record list can never be used to rebuild
    /// the member-wise totals the Admin has hidden from them (FRD s.4, s.8).
    /// </remarks>
    public async Task<PagedResult<ExpenseDto>> ListAsync(long businessId, ExpenseListQuery q, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var seesAll = a.IsAdmin || a.Can(Permission.ViewMemberWiseExpenses);
        if (!seesAll && !a.Can(Permission.ViewExpenseTotal) && !a.Can(Permission.AddExpenses)) throw new ForbiddenException();
        if (!seesAll && q.PurchasedBy is long other && other != a.UserId) throw new ForbiddenException();

        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query = db.Expenses.AsNoTracking().Where(e => e.BusinessId == businessId);
        if (!seesAll) query = query.Where(e => e.CreatedBy == a.UserId || e.PurchasedByUserId == a.UserId);
        query = ApplyRange(query, q.From, q.To);
        if (q.PurchasedBy is long pb) query = query.Where(e => e.PurchasedByUserId == pb);
        if (!string.IsNullOrWhiteSpace(q.Q)) { var t = q.Q.Trim(); query = query.Where(e => e.Name.Contains(t)); }

        query = q.Sort?.ToLowerInvariant() switch
        {
            "date" => query.OrderBy(e => e.ExpenseDate).ThenBy(e => e.Id),
            "amount" => query.OrderBy(e => e.TotalAmount).ThenBy(e => e.Id),
            "-amount" => query.OrderByDescending(e => e.TotalAmount).ThenBy(e => e.Id),
            _ => query.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id)
        };

        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.PurchasedByUserId, r.CreatedBy }).Distinct().ToList(), ct);
        return new PagedResult<ExpenseDto>(rows.Select(r => ToDto(r, names, a)).ToList(), total, page, size);
    }

    // ------------------------------------------------------------------ create / update / delete
    public async Task<ExpenseDto> CreateAsync(long businessId, SaveExpenseRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.AddExpenses, ct);
        var purchaser = await ResolvePurchaserAsync(businessId, r.PurchasedByUserId, a, ct);
        var e = new Expense { BusinessId = businessId, CreatedBy = a.UserId };
        Apply(e, r, purchaser);
        db.Expenses.Add(e);
        await db.SaveChangesAsync(ct);
        return await GetAsync(e.Id, a, ct);
    }

    public async Task<ExpenseDto> UpdateAsync(long businessId, long id, SaveExpenseRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId, ct) ?? throw new NotFoundException("Expense not found.");
        if (!a.CanModifyOwn(e.CreatedBy, Permission.EditOwnExpenses)) throw new ForbiddenException();   // own records only; Admin all
        var purchaser = await ResolvePurchaserAsync(businessId, r.PurchasedByUserId ?? e.PurchasedByUserId, a, ct);
        Apply(e, r, purchaser);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, a, ct);
    }

    public async Task DeleteAsync(long businessId, long id, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var e = await db.Expenses.FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId, ct) ?? throw new NotFoundException("Expense not found.");
        if (!a.CanModifyOwn(e.CreatedBy, Permission.DeleteOwnExpenses)) throw new ForbiddenException();
        e.IsDeleted = true;                       // soft delete keeps the record for audit and reconciliation
        e.DeletedAt = clock.GetUtcNow().UtcDateTime;
        e.DeletedBy = a.UserId;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ totals
    public async Task<ExpenseSummaryDto> SummaryAsync(long businessId, ExpenseRangeQuery q, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        // Members who can see the member-wise breakdown can derive the total anyway, so they may see it too.
        if (!a.Can(Permission.ViewExpenseTotal) && !a.Can(Permission.ViewMemberWiseExpenses)) throw new ForbiddenException();

        var query = ApplyRange(db.Expenses.AsNoTracking().Where(e => e.BusinessId == businessId), q.From, q.To);
        var agg = await query.GroupBy(_ => 1).Select(g => new
        {
            Total = g.Sum(e => e.TotalAmount),
            Count = g.Count(),
            Gst = g.Where(e => e.IsInputGstEligible).Sum(e => e.InputGstAmount)
        }).FirstOrDefaultAsync(ct);
        return new ExpenseSummaryDto(businessId, q.From, q.To, agg?.Total ?? 0m, agg?.Count ?? 0, agg?.Gst ?? 0m);
    }

    /// <remarks>Requires the dedicated permission; without it NO per-member data is returned (not even via this route).
    /// Grouping happens on the expense rows themselves, so Sum(Rows) always equals the Summary total for the same filters.</remarks>
    public async Task<ExpenseByMemberDto> ByMemberAsync(long businessId, ExpenseByMemberQuery q, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.ViewMemberWiseExpenses, ct);
        var byPurchaser = !string.Equals(q.Basis, "createdBy", StringComparison.OrdinalIgnoreCase);

        var query = ApplyRange(db.Expenses.AsNoTracking().Where(e => e.BusinessId == businessId), q.From, q.To);
        var groups = byPurchaser
            ? await query.GroupBy(e => e.PurchasedByUserId).Select(g => new { UserId = g.Key, Total = g.Sum(e => e.TotalAmount), Count = g.Count() }).ToListAsync(ct)
            : await query.GroupBy(e => e.CreatedBy).Select(g => new { UserId = g.Key, Total = g.Sum(e => e.TotalAmount), Count = g.Count() }).ToListAsync(ct);

        var names = await NamesAsync(groups.Select(g => g.UserId).ToList(), ct);
        var rows = groups.Select(g => new MemberExpenseRowDto(g.UserId, names.GetValueOrDefault(g.UserId, "Unknown"), g.Total, g.Count))
            .OrderByDescending(r => r.TotalAmount).ThenBy(r => r.Name).ToList();
        return new ExpenseByMemberDto(businessId, byPurchaser ? "PurchasedBy" : "CreatedBy",
            byPurchaser ? "Grouped by the member who PURCHASED the item" : "Grouped by the user who ENTERED the record",
            q.From, q.To, rows.Sum(r => r.TotalAmount), rows);
    }

    // ------------------------------------------------------------------ helpers
    private static IQueryable<Expense> ApplyRange(IQueryable<Expense> q, DateOnly? from, DateOnly? to)
    {
        if (from is DateOnly f) q = q.Where(e => e.ExpenseDate >= f);
        if (to is DateOnly t) q = q.Where(e => e.ExpenseDate <= t);
        return q;
    }

    private async Task<long> ResolvePurchaserAsync(long businessId, long? requested, BusinessAccess a, CancellationToken ct)
    {
        long id;
        if (requested is long r) id = r;
        else if (a.Role == UserRole.Member) id = a.UserId;
        else throw Fail.Validation("purchasedByUserId", "Select the member who purchased the item.");

        if (id == a.UserId && a.Role == UserRole.Member) return id;
        var ok = await db.Users.AnyAsync(u => u.Id == id && u.IsActive && (u.Role == UserRole.Admin ||
                 (u.Role == UserRole.Member && u.BusinessMemberships.Any(m => m.BusinessId == businessId && m.IsActive))), ct);
        if (!ok) throw Fail.Validation("purchasedByUserId", "The purchaser must be a member assigned to this business.");
        return id;
    }

    private void Apply(Expense e, SaveExpenseRequest r, long purchaser)
    {
        var total = Expense.ComputeTotal(r.Quantity, r.UnitPrice);
        if (r.InputGstAmount > total) throw Fail.Validation("inputGstAmount", "Input GST cannot exceed the expense amount.");
        e.ExpenseDate = r.ExpenseDate;
        e.Name = r.Name.Trim();
        e.Quantity = r.Quantity;
        e.UnitPrice = r.UnitPrice;
        e.InputGstAmount = r.InputGstAmount;
        e.IsInputGstEligible = r.IsInputGstEligible;
        e.PurchasedByUserId = purchaser;
    }

    private async Task<ExpenseDto> GetAsync(long id, BusinessAccess a, CancellationToken ct)
    {
        var e = await db.Expenses.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var names = await NamesAsync([e.PurchasedByUserId, e.CreatedBy], ct);
        return ToDto(e, names, a);
    }

    /// <summary>Names are looked up ignoring soft-delete filters: history must still show who bought something.</summary>
    private async Task<Dictionary<long, string>> NamesAsync(List<long> ids, CancellationToken ct) =>
        await db.Users.IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

    private static ExpenseDto ToDto(Expense e, Dictionary<long, string> names, BusinessAccess a) => new(
        e.Id, e.BusinessId, e.ExpenseDate, e.Name, e.Quantity, e.UnitPrice, e.TotalAmount, e.InputGstAmount, e.IsInputGstEligible,
        e.PurchasedByUserId, names.GetValueOrDefault(e.PurchasedByUserId, "Unknown"),
        e.CreatedBy, names.GetValueOrDefault(e.CreatedBy, "Unknown"), e.CreatedAt,
        a.CanModifyOwn(e.CreatedBy, Permission.EditOwnExpenses), a.CanModifyOwn(e.CreatedBy, Permission.DeleteOwnExpenses));
}
