using System.Text.Json;
using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Returns;

/// <remarks>
/// Admin-only. A return takes back quantities from a sale's lines; amounts are pro-rated per line to the paisa, with the final
/// return of a line taking the exact remainder. If the sale has an issued tax invoice, a credit note (own series CN/..)
/// referencing it is issued in the same transaction. A sale with returns can no longer be edited or deleted.
/// </remarks>
public sealed class ReturnService(IAppDbContext db, IBusinessAccessService access, InvoiceSequenceAllocator sequences, TimeProvider clock) : IReturnService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SalesReturnDto> CreateAsync(long businessId, long saleId, CreateReturnRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        if (!a.IsAdmin) throw new ForbiddenException();
        if (r.Items.Select(i => i.SalesOrderItemId).Distinct().Count() != r.Items.Count)
            throw Fail.Validation("items", "Each sale line can appear only once per return.");

        var now = clock.GetUtcNow().UtcDateTime;
        var date = InvoiceRules.TodayIst(now);
        var fy = InvoiceRules.FinancialYear(date);

        await using var tx = await db.BeginTransactionAsync(ct);

        // If the sale has an issued invoice a credit note is needed: allocate its number BEFORE loading tracked entities
        // (the allocator may reset the change tracker when it loses a race to create the counter row).
        var hasInvoice = await db.SalesOrders.AsNoTracking().Where(o => o.Id == saleId && o.BusinessId == businessId).Select(o => o.InvoiceId).FirstOrDefaultAsync(ct);
        int? creditSeq = hasInvoice is not null ? await sequences.NextAsync(businessId, fy, InvoiceDocType.CreditNote, ct) : null;

        var sale = await db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == saleId && o.BusinessId == businessId, ct)
                   ?? throw new NotFoundException("Sale not found.");
        if (sale.Status != SalesOrderStatus.Active) throw new ConflictException("This sale is cancelled.");
        if (r.ReturnDate < sale.OrderDate) throw Fail.Validation("returnDate", "Return date cannot be before the sale date.");

        var itemIds = sale.Items.Select(i => i.Id).ToList();
        var previous = await db.SalesReturnItems.Where(x => itemIds.Contains(x.SalesOrderItemId) && x.SalesReturn!.Status == SalesReturnStatus.Accepted)
            .GroupBy(x => x.SalesOrderItemId).Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity), Taxable = g.Sum(x => x.TaxableAmount), Gst = g.Sum(x => x.GstAmount) })
            .ToDictionaryAsync(x => x.ItemId, ct);

        var created = new List<(SalesOrderItem Item, decimal Qty, decimal Taxable, decimal Gst)>();
        foreach (var req in r.Items)
        {
            var item = sale.Items.FirstOrDefault(i => i.Id == req.SalesOrderItemId) ?? throw Fail.Validation("items", "A returned line does not belong to this sale.");
            previous.TryGetValue(item.Id, out var prev);
            var remaining = item.Quantity - (prev?.Qty ?? 0m);
            if (req.Quantity > remaining)
                throw Fail.Validation("items", $"Cannot return {req.Quantity} of '{item.ProductNameSnapshot}': only {remaining} left to return.");
            var taxable = ReturnRules.Prorate(item.TaxableAmount, item.Quantity, req.Quantity, prev?.Qty ?? 0m, prev?.Taxable ?? 0m);
            var gst = ReturnRules.Prorate(item.GstAmount, item.Quantity, req.Quantity, prev?.Qty ?? 0m, prev?.Gst ?? 0m);
            created.Add((item, req.Quantity, taxable, gst));
        }

        var ret = new SalesReturn
        {
            BusinessId = businessId, SalesOrderId = saleId, ReturnDate = r.ReturnDate, Reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim(),
            TaxableAmount = created.Sum(c => c.Taxable), GstAmount = created.Sum(c => c.Gst), CreatedBy = a.UserId,
            Items = created.Select(c => new SalesReturnItem { SalesOrderItemId = c.Item.Id, Quantity = c.Qty, TaxableAmount = c.Taxable, GstAmount = c.Gst }).ToList()
        };
        ret.TotalAmount = ret.TaxableAmount + ret.GstAmount;
        db.SalesReturns.Add(ret);
        await db.SaveChangesAsync(ct);

        // Credit note, only if the sale has an ISSUED tax invoice.
        GstInvoice? creditNote = null;
        var original = sale.InvoiceId is long invId ? await db.GstInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invId && i.Status == InvoiceStatus.Issued, ct) : null;
        if (original is not null && creditSeq is int seq)
        {
            var number = InvoiceRules.FormatNumber("CN", fy, seq);
            var origSnapshot = JsonSerializer.Deserialize<InvoiceSnapshot>(original.SnapshotJson, Json)!;

            var lines = created.Select((c, idx) =>
            {
                var (cgst, sgst, igst) = InvoiceRules.SplitGst(c.Gst, original.IsInterState);
                var gross = SalesCalculator.Round(c.Qty * c.Item.UnitPrice);
                return new InvoiceLine(idx + 1, c.Item.ProductNameSnapshot, c.Item.HsnCodeSnapshot, c.Qty, c.Item.Unit, c.Item.UnitPrice,
                    Math.Max(0m, gross - c.Taxable), c.Taxable, c.Item.GstRate, cgst, sgst, igst, c.Taxable + c.Gst);
            }).ToList();
            var snapshot = new InvoiceSnapshot("CREDIT NOTE", number, date, origSnapshot.Seller, origSnapshot.Buyer, origSnapshot.PlaceOfSupplyState,
                original.IsInterState, lines, lines.Sum(l => l.Taxable + l.Discount), lines.Sum(l => l.Discount), lines.Sum(l => l.Taxable),
                lines.Sum(l => l.Cgst), lines.Sum(l => l.Sgst), lines.Sum(l => l.Igst), ret.TotalAmount, saleId, sale.OrderDate, original.InvoiceNumber);

            creditNote = new GstInvoice
            {
                BusinessId = businessId, SalesOrderId = saleId, DocType = InvoiceDocType.CreditNote, FinancialYear = fy, SequenceNumber = seq,
                InvoiceNumber = number, InvoiceDate = date, IsInterState = original.IsInterState, TaxableAmount = snapshot.Taxable,
                Cgst = snapshot.Cgst, Sgst = snapshot.Sgst, Igst = snapshot.Igst, GrandTotal = ret.TotalAmount,
                SnapshotJson = JsonSerializer.Serialize(snapshot, Json), Status = InvoiceStatus.Issued, OriginalInvoiceId = original.Id,
                IssuedBy = a.UserId, IssuedAt = now
            };
            db.GstInvoices.Add(creditNote);
            await db.SaveChangesAsync(ct);
            ret.CreditNoteId = creditNote.Id;
        }

        sale.UpdatedAt = now;      // advances the sale's RowVersion (serialises against concurrent payments)
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The sale was changed at the same moment. Please try again."); }
        await tx.CommitAsync(ct);
        return (await ListAsync(businessId, saleId, ct)).First(x => x.Id == ret.Id);
    }

    public async Task<IReadOnlyList<SalesReturnDto>> ListAsync(long businessId, long saleId, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.ViewSales, ct);
        if (!await db.SalesOrders.AnyAsync(o => o.Id == saleId && o.BusinessId == businessId, ct)) throw new NotFoundException("Sale not found.");

        var rows = await db.SalesReturns.AsNoTracking().Include(x => x.Items).Where(x => x.SalesOrderId == saleId).OrderBy(x => x.Id).ToListAsync(ct);
        var itemIds = rows.SelectMany(x => x.Items.Select(i => i.SalesOrderItemId)).Distinct().ToList();
        var names = await db.SalesOrderItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.ProductNameSnapshot, ct);
        var creators = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => rows.Select(r => r.CreatedBy).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var noteIds = rows.Where(x => x.CreditNoteId.HasValue).Select(x => x.CreditNoteId!.Value).ToList();
        var notes = await db.GstInvoices.AsNoTracking().Where(i => noteIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, ct);

        return rows.Select(x => new SalesReturnDto(x.Id, x.SalesOrderId, x.ReturnDate, x.TaxableAmount, x.GstAmount, x.TotalAmount, x.Reason, x.Status.ToString(),
            creators.GetValueOrDefault(x.CreatedBy, "Unknown"),
            x.CreatedAt, x.Items.Select(i => new ReturnItemDto(i.SalesOrderItemId, names.GetValueOrDefault(i.SalesOrderItemId, "?"), i.Quantity, i.TaxableAmount, i.GstAmount)).ToList(),
            x.CreditNoteId, x.CreditNoteId is long id ? notes.GetValueOrDefault(id) : null)).ToList();
    }
}
