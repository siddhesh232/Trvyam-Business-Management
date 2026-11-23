using BusinessManagement.Application.Abstractions;
using BusinessManagement.Application.Authorization;
using BusinessManagement.Application.Common;
using BusinessManagement.Domain.Billing;
using BusinessManagement.Domain.Entities;
using BusinessManagement.Domain.Enums;
using BusinessManagement.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace BusinessManagement.Application.Payments;

/// <remarks>
/// Payments are separate rows linked to the sale (many per sale). Nothing is ever deleted: "removing" a payment REVERSES it
/// (status, who, when, why). Status/outstanding are calculated from valid payments. Every change touches the sale row so its
/// RowVersion advances; two concurrent payments therefore cannot both pass the overpayment check.
/// </remarks>
public sealed class PaymentService(IAppDbContext db, IBusinessAccessService access, TimeProvider clock) : IPaymentService
{
    public async Task<PaymentHistoryDto> HistoryAsync(long businessId, long saleId, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.ViewPayments, ct);
        var order = await LoadOrderAsync(businessId, saleId, tracked: false, ct);
        return await BuildAsync(order, a, ct);
    }

    public async Task<PaymentHistoryDto> RecordAsync(long businessId, long saleId, RecordPaymentRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, Permission.RecordPayments, ct);
        var order = await LoadOrderAsync(businessId, saleId, tracked: true, ct);
        var mode = ParseMode(r.PaymentMode);
        EnsureDate(order, r.PaymentDate);

        var net = order.FinalAmount - await ReturnedAsync(saleId, ct);      // returns reduce what is due
        var received = await ReceivedAsync(saleId, null, ct);
        var outstanding = net - received;
        if (r.Amount > outstanding)
            throw new DomainException($"Payment ({r.Amount:0.00}) exceeds the outstanding amount ({outstanding:0.00}). Overpayments are not allowed.", "overpayment");

        db.Payments.Add(new Payment
        {
            SalesOrderId = saleId, PaymentDate = r.PaymentDate, Amount = r.Amount, PaymentMode = mode,
            TransactionReference = Clean(r.TransactionReference), Notes = Clean(r.Notes), ReceivedBy = a.UserId
        });
        order.UpdatedAt = clock.GetUtcNow().UtcDateTime;      // advances the sale's RowVersion: serialises concurrent payments
        await SaveAsync(ct);
        return await BuildAsync(order, a, ct);
    }

    public async Task<PaymentHistoryDto> UpdateAsync(long businessId, long saleId, long paymentId, UpdatePaymentRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var order = await LoadOrderAsync(businessId, saleId, tracked: true, ct);
        var p = await db.Payments.FirstOrDefaultAsync(x => x.Id == paymentId && x.SalesOrderId == saleId, ct) ?? throw new NotFoundException("Payment not found.");
        if (!a.CanModifyOwn(p.ReceivedBy, Permission.EditOwnPayments)) throw new ForbiddenException();
        if (p.Status != PaymentRecordStatus.Valid) throw new ConflictException("A reversed payment cannot be edited.");

        var mode = ParseMode(r.PaymentMode);
        EnsureDate(order, r.PaymentDate);
        var netDue = order.FinalAmount - await ReturnedAsync(saleId, ct);
        var othersReceived = await ReceivedAsync(saleId, paymentId, ct);
        if (othersReceived + r.Amount > netDue)
            throw new DomainException($"The payment would exceed the amount due. Maximum for this payment: {Math.Max(0m, netDue - othersReceived):0.00}.", "overpayment");

        p.PaymentDate = r.PaymentDate; p.Amount = r.Amount; p.PaymentMode = mode;
        p.TransactionReference = Clean(r.TransactionReference); p.Notes = Clean(r.Notes);
        order.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await SaveAsync(ct);
        return await BuildAsync(order, a, ct);
    }

    public async Task<PaymentHistoryDto> ReverseAsync(long businessId, long saleId, long paymentId, ReversePaymentRequest r, CancellationToken ct)
    {
        var a = await access.RequireAsync(businessId, null, ct);
        var order = await LoadOrderAsync(businessId, saleId, tracked: true, ct);
        var p = await db.Payments.FirstOrDefaultAsync(x => x.Id == paymentId && x.SalesOrderId == saleId, ct) ?? throw new NotFoundException("Payment not found.");
        if (!a.CanModifyOwn(p.ReceivedBy, Permission.DeleteOwnPayments)) throw new ForbiddenException();
        if (p.Status == PaymentRecordStatus.Reversed) throw new ConflictException("This payment has already been reversed.");

        var now = clock.GetUtcNow().UtcDateTime;
        p.Status = PaymentRecordStatus.Reversed;
        p.ReversedAt = now; p.ReversedBy = a.UserId; p.ReversalReason = r.Reason.Trim();
        order.UpdatedAt = now;
        await SaveAsync(ct);   // the AppDbContext audit log records the old and new values
        return await BuildAsync(order, a, ct);
    }

    // ------------------------------------------------------------------ helpers
    private async Task<SalesOrder> LoadOrderAsync(long businessId, long saleId, bool tracked, CancellationToken ct)
    {
        var q = tracked ? db.SalesOrders.AsQueryable() : db.SalesOrders.AsNoTracking();
        var o = await q.FirstOrDefaultAsync(x => x.Id == saleId && x.BusinessId == businessId, ct) ?? throw new NotFoundException("Sale not found.");
        if (o.Status != SalesOrderStatus.Active) throw new ConflictException("This sale is cancelled.");
        return o;
    }

    private static void EnsureDate(SalesOrder order, DateOnly paymentDate)
    {
        if (paymentDate < order.OrderDate) throw Fail.Validation("paymentDate", "Payment date cannot be before the sale date.");
    }

    private static PaymentMode ParseMode(string mode) =>
        Enum.TryParse<PaymentMode>(mode, true, out var m) && Enum.IsDefined(m) ? m : throw Fail.Validation("paymentMode", "Unknown payment mode.");

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<decimal> ReceivedAsync(long saleId, long? exceptPaymentId, CancellationToken ct) =>
        await db.Payments.Where(p => p.SalesOrderId == saleId && p.Status == PaymentRecordStatus.Valid && p.Id != exceptPaymentId)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    private async Task<decimal> ReturnedAsync(long saleId, CancellationToken ct) =>
        await db.SalesReturns.Where(r => r.SalesOrderId == saleId && r.Status == SalesReturnStatus.Accepted).SumAsync(r => (decimal?)r.TotalAmount, ct) ?? 0m;

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("This sale was changed at the same moment by someone else. Please reload and try again.");
        }
    }

    private async Task<PaymentHistoryDto> BuildAsync(SalesOrder order, BusinessAccess a, CancellationToken ct)
    {
        var rows = await db.Payments.AsNoTracking().Where(p => p.SalesOrderId == order.Id).OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync(ct);
        var ids = rows.SelectMany(r => new long?[] { r.ReceivedBy, r.ReversedBy }).Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var received = rows.Where(r => r.Status == PaymentRecordStatus.Valid).Sum(r => r.Amount);
        var items = rows.Select(r => new PaymentDto(r.Id, r.SalesOrderId, r.PaymentDate, r.Amount, r.PaymentMode.ToString(), r.TransactionReference,
            r.ReceivedBy, names.GetValueOrDefault(r.ReceivedBy, "Unknown"), r.Notes, r.Status.ToString(), r.ReversedAt,
            r.ReversedBy is long rb ? names.GetValueOrDefault(rb, "Unknown") : null, r.ReversalReason, r.CreatedAt,
            r.Status == PaymentRecordStatus.Valid && a.CanModifyOwn(r.ReceivedBy, Permission.EditOwnPayments),
            r.Status == PaymentRecordStatus.Valid && a.CanModifyOwn(r.ReceivedBy, Permission.DeleteOwnPayments))).ToList();

        var returned = await ReturnedAsync(order.Id, ct);
        var net = order.FinalAmount - returned;
        return new PaymentHistoryDto(order.Id, order.FinalAmount, received, Math.Max(0m, PaymentMath.Outstanding(net, received)),
            PaymentMath.StatusFor(net, received).ToString(), items, returned, net, Math.Max(0m, received - net));
    }
}
