namespace BusinessManagement.Application.Payments;

public sealed record RecordPaymentRequest(DateOnly PaymentDate, decimal Amount, string PaymentMode, string? TransactionReference, string? Notes);
public sealed record UpdatePaymentRequest(DateOnly PaymentDate, decimal Amount, string PaymentMode, string? TransactionReference, string? Notes);
public sealed record ReversePaymentRequest(string Reason);

/// <param name="Status">Valid | Reversed. Reversed payments stay in the history but are not counted.</param>
public sealed record PaymentDto(long Id, long SalesOrderId, DateOnly PaymentDate, decimal Amount, string PaymentMode,
    string? TransactionReference, long ReceivedBy, string ReceivedByName, string? Notes, string Status,
    DateTime? ReversedAt, string? ReversedByName, string? ReversalReason, DateTime CreatedAt, bool CanEdit, bool CanReverse);

/// <param name="Outstanding">Still to collect after accepted returns (never negative).</param>
/// <param name="RefundDue">Received in excess of what is now due after returns.</param>
/// <param name="PaymentStatus">Unpaid | PartiallyPaid | Paid (calculated against the net amount due, never stored).</param>
public sealed record PaymentHistoryDto(long SalesOrderId, decimal FinalAmount, decimal TotalReceived, decimal Outstanding,
    string PaymentStatus, IReadOnlyList<PaymentDto> Payments, decimal ReturnedAmount, decimal NetAmount, decimal RefundDue);

public interface IPaymentService
{
    Task<PaymentHistoryDto> HistoryAsync(long businessId, long saleId, CancellationToken ct);
    Task<PaymentHistoryDto> RecordAsync(long businessId, long saleId, RecordPaymentRequest request, CancellationToken ct);
    Task<PaymentHistoryDto> UpdateAsync(long businessId, long saleId, long paymentId, UpdatePaymentRequest request, CancellationToken ct);
    Task<PaymentHistoryDto> ReverseAsync(long businessId, long saleId, long paymentId, ReversePaymentRequest request, CancellationToken ct);
}
