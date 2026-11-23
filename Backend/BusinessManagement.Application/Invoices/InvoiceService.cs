using System.Text.Json;
using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using BusinessManagement.Application.Authorization;

namespace BusinessManagement.Application.Invoices;

/// <remarks>
/// Issued invoices are immutable (also enforced by DB triggers). Corrections happen by cancellation (and, in the next slice,
/// credit notes), never by editing. Numbers are sequential per business + financial year; allocation happens inside the same
/// transaction as the invoice insert under a row lock, so concurrent issuers cannot duplicate or skip a number.
/// </remarks>
public sealed class InvoiceService(IAppDbContext db, IBusinessAccessService access, IInvoicePdfGenerator pdf, InvoiceSequenceAllocator sequences, TimeProvider clock) : IInvoiceService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ------------------------------------------------------------------ issue
    public async Task<InvoiceDto> IssueAsync(long businessId, long saleId, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.GenerateInvoices, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var date = InvoiceRules.TodayIst(now);
        var fy = InvoiceRules.FinancialYear(date);

        await using var tx = await db.BeginTransactionAsync(ct);

        // 1) Next gapless number for this business/year, allocated under a row lock inside this transaction.
        var seq = await sequences.NextAsync(businessId, fy, InvoiceDocType.TaxInvoice, ct);

        // 2) Validate the sale and the parties.
        var business = await db.Businesses.AsNoTracking().FirstAsync(b => b.Id == businessId, ct);
        if (string.IsNullOrWhiteSpace(business.GstNumber) || string.IsNullOrWhiteSpace(business.AddressLine) || string.IsNullOrWhiteSpace(business.StateCode))
            throw new DomainException("The business needs a GSTIN, an address and a state before GST invoices can be issued. Update the business details first.", "business_incomplete");

        var order = await db.SalesOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == saleId && o.BusinessId == businessId, ct)
                    ?? throw new NotFoundException("Sale not found.");
        if (order.Status != SalesOrderStatus.Active) throw new ConflictException("This sale is cancelled.");
        if (order.InvoiceId is not null || await db.GstInvoices.AnyAsync(i => i.SalesOrderId == saleId && i.DocType == InvoiceDocType.TaxInvoice && i.Status == InvoiceStatus.Issued, ct))
            throw new ConflictException("An invoice has already been issued for this sale.");
        var customer = await db.Customers.IgnoreQueryFilters().AsNoTracking().FirstAsync(c => c.Id == order.CustomerId, ct);

        // 3) Place of supply decides CGST+SGST (same state) or IGST (different state).
        var place = order.PlaceOfSupplyState ?? customer.StateCode ?? business.StateCode;
        var interState = place != business.StateCode;

        var number = InvoiceRules.FormatNumber("INV", fy, seq);
        var lines = order.Items.OrderBy(i => i.Id).Select((i, idx) =>
        {
            var (cgst, sgst, igst) = InvoiceRules.SplitGst(i.GstAmount, interState);
            return new InvoiceLine(idx + 1, i.ProductNameSnapshot, i.HsnCodeSnapshot, i.Quantity, i.Unit, i.UnitPrice,
                i.DiscountAmount, i.TaxableAmount, i.GstRate, cgst, sgst, igst, i.TotalAmount);
        }).ToList();

        var snapshot = new InvoiceSnapshot("TAX INVOICE", number, date,
            new InvoiceParty(business.Name, business.GstNumber, business.AddressLine, business.StateCode, null),
            new InvoiceParty(customer.Name, customer.Gstin, customer.Address, customer.StateCode, customer.Mobile),
            place, interState, lines, order.Subtotal, order.DiscountAmount, order.TaxableAmount,
            lines.Sum(l => l.Cgst), lines.Sum(l => l.Sgst), lines.Sum(l => l.Igst), order.FinalAmount, order.Id, order.OrderDate);

        if (snapshot.Taxable + snapshot.Cgst + snapshot.Sgst + snapshot.Igst != order.FinalAmount)
            throw new InvalidOperationException("Invoice totals do not reconcile with the sale.");   // cannot happen; guards the invariant

        var invoice = new GstInvoice
        {
            BusinessId = businessId, SalesOrderId = saleId, DocType = InvoiceDocType.TaxInvoice, FinancialYear = fy, SequenceNumber = seq,
            InvoiceNumber = number, InvoiceDate = date, IsInterState = interState, TaxableAmount = snapshot.Taxable,
            Cgst = snapshot.Cgst, Sgst = snapshot.Sgst, Igst = snapshot.Igst, GrandTotal = snapshot.GrandTotal,
            SnapshotJson = JsonSerializer.Serialize(snapshot, Json), Status = InvoiceStatus.Issued, IssuedBy = a.UserId, IssuedAt = now
        };
        db.GstInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        order.InvoiceId = invoice.Id;                 // locks the sale against edits/deletes
        order.UpdatedAt = now;                        // advances the sale's RowVersion (serialises against payment changes)
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The sale was changed at the same moment. Please try again."); }
        await tx.CommitAsync(ct);
        return await GetAsync(businessId, invoice.Id, ct, requireView: false);
    }

    // ------------------------------------------------------------------ read
    public async Task<PagedResult<InvoiceListItemDto>> ListAsync(long businessId, InvoiceListQuery q, CancellationToken ct)
    {
        await access.RequireAsync(businessId, Permission.ViewInvoices, ct);
        InvoiceStatus? status = null;
        if (!string.IsNullOrWhiteSpace(q.Status))
        {
            if (!Enum.TryParse<InvoiceStatus>(q.Status, true, out var s) || !Enum.IsDefined(s)) throw Fail.Validation("status", "Status must be Issued, Cancelled or Amended.");
            status = s;
        }
        var (page, size) = Paging.Normalize(q.Page, q.PageSize);
        var query =
            from i in db.GstInvoices.AsNoTracking()
            where i.BusinessId == businessId
            join o in db.SalesOrders.IgnoreQueryFilters() on i.SalesOrderId equals o.Id
            join c in db.Customers.IgnoreQueryFilters() on o.CustomerId equals c.Id
            select new { i, CustomerName = c.Name };

        if (q.From is DateOnly f) query = query.Where(x => x.i.InvoiceDate >= f);
        if (q.To is DateOnly t) query = query.Where(x => x.i.InvoiceDate <= t);
        if (status is InvoiceStatus st) query = query.Where(x => x.i.Status == st);
        if (!string.IsNullOrWhiteSpace(q.Q)) { var term = q.Q.Trim(); query = query.Where(x => x.i.InvoiceNumber.Contains(term) || x.CustomerName.Contains(term)); }

        query = q.Sort?.ToLowerInvariant() switch
        {
            "number" => query.OrderBy(x => x.i.FinancialYear).ThenBy(x => x.i.SequenceNumber),
            "amount" => query.OrderBy(x => x.i.GrandTotal).ThenBy(x => x.i.Id),
            "-amount" => query.OrderByDescending(x => x.i.GrandTotal).ThenBy(x => x.i.Id),
            _ => query.OrderByDescending(x => x.i.InvoiceDate).ThenByDescending(x => x.i.Id)
        };
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Select(x => new InvoiceListItemDto(
            x.i.Id, x.i.InvoiceNumber, x.i.InvoiceDate, x.i.DocType.ToString(), x.i.Status.ToString(), x.i.SalesOrderId, x.CustomerName, x.i.GrandTotal)).ToListAsync(ct);
        return new PagedResult<InvoiceListItemDto>(items, total, page, size);
    }

    public Task<InvoiceDto> GetAsync(long businessId, long invoiceId, CancellationToken ct) => GetAsync(businessId, invoiceId, ct, requireView: true);

    private async Task<InvoiceDto> GetAsync(long businessId, long invoiceId, CancellationToken ct, bool requireView)
    {
        if (requireView) await access.RequireAsync(businessId, Permission.ViewInvoices, ct);
        var i = await db.GstInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == invoiceId && x.BusinessId == businessId, ct)
                ?? throw new NotFoundException("Invoice not found.");
        var issuer = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == i.IssuedBy).Select(u => u.Name).FirstAsync(ct);
        var snapshot = JsonSerializer.Deserialize<InvoiceSnapshot>(i.SnapshotJson, Json)!;
        return new InvoiceDto(i.Id, i.BusinessId, i.DocType.ToString(), i.InvoiceNumber, i.InvoiceDate, i.FinancialYear, i.Status.ToString(), i.IsInterState,
            i.TaxableAmount, i.Cgst, i.Sgst, i.Igst, i.GrandTotal, i.SalesOrderId, i.OriginalInvoiceId, i.CancelReason, i.IssuedAt, issuer, snapshot);
    }

    public async Task<InvoicePdf> GetPdfAsync(long businessId, long invoiceId, CancellationToken ct)
    {
        var inv = await GetAsync(businessId, invoiceId, ct, requireView: true);
        var bytes = pdf.Generate(inv.Snapshot, inv.Status == InvoiceStatus.Cancelled.ToString());
        return new InvoicePdf(bytes, inv.InvoiceNumber.Replace('/', '-') + ".pdf");
    }

    // ------------------------------------------------------------------ cancel (Admin)
    public async Task<InvoiceDto> CancelAsync(long businessId, long invoiceId, CancelInvoiceRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        if (!a.IsAdmin) throw new ForbiddenException();

        var inv = await db.GstInvoices.FirstOrDefaultAsync(x => x.Id == invoiceId && x.BusinessId == businessId, ct) ?? throw new NotFoundException("Invoice not found.");
        if (inv.DocType != InvoiceDocType.TaxInvoice) throw new ConflictException("Only tax invoices can be cancelled.");
        if (inv.Status != InvoiceStatus.Issued) throw new ConflictException("This invoice is already cancelled.");
        if (await db.GstInvoices.AnyAsync(x => x.OriginalInvoiceId == inv.Id && x.DocType == InvoiceDocType.CreditNote && x.Status == InvoiceStatus.Issued, ct))
            throw new ConflictException("Credit notes have been issued against this invoice, so it cannot be cancelled.");

        await using var tx = await db.BeginTransactionAsync(ct);
        inv.Status = InvoiceStatus.Cancelled;           // the number stays in the series as a cancelled document
        inv.CancelReason = r.Reason.Trim();
        await db.SaveChangesAsync(ct);

        var order = await db.SalesOrders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.Id == inv.SalesOrderId, ct);
        if (order is not null && order.InvoiceId == inv.Id)
        {
            order.InvoiceId = null;                     // the sale can be corrected and re-invoiced under a NEW number
            order.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await GetAsync(businessId, invoiceId, ct, requireView: false);
    }
}
