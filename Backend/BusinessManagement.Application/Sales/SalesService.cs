using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Sales;

public sealed class SalesService(IAppDbContext db, IBusinessAccessService access, TimeProvider clock) : ISalesService
{
    private static readonly HashSet<string> Statuses = ["Unpaid", "PartiallyPaid", "Paid"];

    // ------------------------------------------------------------------ list / get
    public async Task<PagedResult<SalesListItemDto>> ListAsync(long businessId, SalesListQuery q, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.ViewSales, ct);
        if (!string.IsNullOrWhiteSpace(q.PaymentStatus) && !Statuses.Contains(q.PaymentStatus))
            throw Fail.Validation("paymentStatus", "Payment status must be Unpaid, PartiallyPaid or Paid.");

        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query =
            from so in db.SalesOrders.AsNoTracking()
            where so.BusinessId == businessId
            join ps in db.SalesOrderPaymentSummaries on so.Id equals ps.SalesOrderId
            join c in db.Customers.IgnoreQueryFilters() on so.CustomerId equals c.Id
            join u in db.Users.IgnoreQueryFilters() on so.CreatedBy equals u.Id
            select new { so, ps, c, CreatedByName = u.Name };

        if (q.From is DateOnly f) query = query.Where(x => x.so.OrderDate >= f);
        if (q.To is DateOnly t) query = query.Where(x => x.so.OrderDate <= t);
        if (q.CustomerId is long cid) query = query.Where(x => x.so.CustomerId == cid);
        if (q.ProductId is long pid) query = query.Where(x => x.so.Items.Any(i => i.ProductId == pid));
        if (!string.IsNullOrWhiteSpace(q.PaymentStatus)) query = query.Where(x => x.ps.PaymentStatus == q.PaymentStatus);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim(); var digits = MobileNumber.Normalize(term);
            query = query.Where(x => x.c.Name.Contains(term) || (digits.Length > 0 && x.c.Mobile.Contains(digits)));
        }

        query = q.Sort?.ToLowerInvariant() switch
        {
            "date" => query.OrderBy(x => x.so.OrderDate).ThenBy(x => x.so.Id),
            "amount" => query.OrderBy(x => x.so.FinalAmount).ThenBy(x => x.so.Id),
            "-amount" => query.OrderByDescending(x => x.so.FinalAmount).ThenBy(x => x.so.Id),
            _ => query.OrderByDescending(x => x.so.OrderDate).ThenByDescending(x => x.so.Id)
        };

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Select(x => new SalesListItemDto(
            x.so.Id, x.so.OrderDate, x.so.CustomerId, x.c.Name, x.c.Mobile, x.so.FinalAmount, x.ps.TotalReceived, x.ps.Outstanding,
            x.ps.PaymentStatus, x.so.CreatedBy, x.CreatedByName, x.so.Items.Count)).ToListAsync(ct);
        return new PagedResult<SalesListItemDto>(items, total, page, size);
    }

    public async Task<SalesOrderDto> GetAsync(long businessId, long id, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.ViewSales, ct);
        return await LoadDtoAsync(businessId, id, a, ct);
    }

    // ------------------------------------------------------------------ create
    public async Task<SalesOrderDto> CreateAsync(long businessId, CreateSaleRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.AddSales, ct);

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CustomerId, ct);
        var linked = customer is not null && await db.CustomerBusinesses.AnyAsync(t => t.CustomerId == r.CustomerId && t.BusinessId == businessId, ct);
        if (!linked) throw Fail.Validation("customerId", "Customer is not linked to this business. Look up or add the customer first.");

        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == businessId, ct);
        var (lines, totals) = await PriceAsync(businessId, r.Items, r.OrderDiscount, a, ct);

        var order = new SalesOrder
        {
            BusinessId = businessId, CustomerId = r.CustomerId, OrderDate = r.OrderDate, CreatedBy = a.UserId,
            PlaceOfSupplyState = customer!.StateCode ?? business.StateCode
        };
        ApplyTotals(order, totals);
        order.Items = BuildItems(lines, totals);
        db.SalesOrders.Add(order);
        await db.SaveChangesAsync(ct);
        return await LoadDtoAsync(businessId, order.Id, a, ct);
    }

    // ------------------------------------------------------------------ update / delete (own records unless Admin)
    public async Task<SalesOrderDto> UpdateAsync(long businessId, long id, UpdateSaleRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var order = await db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id && o.BusinessId == businessId, ct)
                    ?? throw new NotFoundException("Sale not found.");
        if (!a.CanModifyOwn(order.CreatedBy, Permission.EditOwnSales)) throw new ForbiddenException();
        if (order.InvoiceId is not null) throw new ConflictException("An invoice has been issued for this sale. Cancel the invoice before editing.");

        var (lines, totals) = await PriceAsync(businessId, r.Items, r.OrderDiscount, a, ct);
        var received = await ReceivedAsync(id, ct);
        if (totals.Final < received)
            throw new ConflictException($"The new total ({totals.Final:0.00}) is lower than the amount already received ({received:0.00}). Reverse a payment first.");

        await using var tx = await db.BeginTransactionAsync(ct);
        db.SalesOrderItems.RemoveRange(order.Items);           // previous lines stay in the audit log
        order.OrderDate = r.OrderDate;
        ApplyTotals(order, totals);
        order.Items = BuildItems(lines, totals);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await LoadDtoAsync(businessId, id, a, ct);
    }

    public async Task DeleteAsync(long businessId, long id, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var order = await db.SalesOrders.FirstOrDefaultAsync(o => o.Id == id && o.BusinessId == businessId, ct) ?? throw new NotFoundException("Sale not found.");
        if (!a.CanModifyOwn(order.CreatedBy, Permission.DeleteOwnSales)) throw new ForbiddenException();
        if (order.InvoiceId is not null) throw new ConflictException("An invoice has been issued for this sale. Cancel the invoice first.");
        if (await ReceivedAsync(id, ct) > 0) throw new ConflictException("This sale has payments. Reverse them before deleting the sale.");

        order.IsDeleted = true;                                 // soft delete; financial history is never erased
        order.DeletedAt = clock.GetUtcNow().UtcDateTime;
        order.DeletedBy = a.UserId;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ pricing
    private sealed record PricedLine(Product Product, SaleItemRequest Request, decimal UnitPrice);

    private async Task<(List<PricedLine>, SaleTotals)> PriceAsync(long businessId, IReadOnlyList<SaleItemRequest> items, decimal orderDiscount, BusinessAccess a, CancellationToken ct)
    {
        var ids = items.Select(i => i.ProductId).Distinct().ToList();
        // Sales must use the predefined catalogue: active products mapped to THIS business only (FRD s.7).
        var products = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.IsActive && p.BusinessMappings.Any(m => m.BusinessId == businessId)).ToListAsync(ct);
        if (products.Count != ids.Count) throw Fail.Validation("items", "One or more products are not available for this business.");

        var lines = new List<PricedLine>();
        foreach (var item in items)
        {
            var p = products.First(x => x.Id == item.ProductId);
            var price = p.SellingPrice;
            if (item.UnitPrice is decimal over && over != p.SellingPrice)
            {
                if (!a.IsAdmin) throw Fail.Validation("items", "Only the Admin can change the selling price.");
                price = over;
            }
            lines.Add(new PricedLine(p, item, price));
        }

        try
        {
            var totals = SalesCalculator.Calculate(lines.Select(l => new SaleLineInput(l.Request.Quantity, l.UnitPrice, l.Request.ItemDiscount, l.Product.GstRate)).ToList(), orderDiscount);
            return (lines, totals);
        }
        catch (DomainException ex) when (ex.Code == "invalid_amount")
        {
            throw Fail.Validation("items", ex.Message);
        }
    }

    private static void ApplyTotals(SalesOrder o, SaleTotals t)
    {
        o.Subtotal = t.Subtotal; o.DiscountAmount = t.Discount; o.TaxableAmount = t.Taxable; o.GstAmount = t.Gst; o.FinalAmount = t.Final;
    }

    /// <summary>Each item stores a snapshot of name, HSN, unit, price and GST rate so later catalogue edits never alter history.</summary>
    private static List<SalesOrderItem> BuildItems(List<PricedLine> lines, SaleTotals t) =>
        lines.Select((l, i) => new SalesOrderItem
        {
            ProductId = l.Product.Id, ProductNameSnapshot = l.Product.Name, HsnCodeSnapshot = l.Product.HsnCode, Unit = l.Product.Unit,
            Quantity = l.Request.Quantity, UnitPrice = l.UnitPrice, GstRate = l.Product.GstRate,
            DiscountAmount = t.Lines[i].Discount, TaxableAmount = t.Lines[i].Taxable, GstAmount = t.Lines[i].Gst, TotalAmount = t.Lines[i].Total
        }).ToList();

    private async Task<decimal> ReceivedAsync(long orderId, CancellationToken ct) =>
        await db.Payments.Where(p => p.SalesOrderId == orderId && p.Status == PaymentRecordStatus.Valid).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    private async Task<SalesOrderDto> LoadDtoAsync(long businessId, long id, BusinessAccess a, CancellationToken ct)
    {
        var o = await db.SalesOrders.AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.BusinessId == businessId, ct)
                ?? throw new NotFoundException("Sale not found.");
        var c = await db.Customers.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == o.CustomerId, ct);
        var creator = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == o.CreatedBy).Select(u => u.Name).FirstAsync(ct);
        var received = await ReceivedAsync(id, ct);
        var status = PaymentMath.StatusFor(o.FinalAmount, received).ToString();

        return new SalesOrderDto(o.Id, o.BusinessId, o.OrderDate, new SalesCustomerDto(c.Id, c.Name, c.Mobile, c.Address, c.StateCode, c.Gstin),
            o.Subtotal, o.DiscountAmount, o.TaxableAmount, o.GstAmount, o.FinalAmount, o.PlaceOfSupplyState,
            received, PaymentMath.Outstanding(o.FinalAmount, received), status, o.InvoiceId,
            o.CreatedBy, creator, o.CreatedAt,
            o.Items.OrderBy(i => i.Id).Select(i => new SalesItemDto(i.Id, i.ProductId, i.ProductNameSnapshot, i.HsnCodeSnapshot, i.Quantity, i.Unit,
                i.UnitPrice, i.DiscountAmount, i.GstRate, i.TaxableAmount, i.GstAmount, i.TotalAmount)).ToList(),
            a.CanModifyOwn(o.CreatedBy, Permission.EditOwnSales) && o.InvoiceId is null,
            a.CanModifyOwn(o.CreatedBy, Permission.DeleteOwnSales) && o.InvoiceId is null);
    }
}
