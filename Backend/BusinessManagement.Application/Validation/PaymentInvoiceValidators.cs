using BusinessManagement.Application.Common;
using BusinessManagement.Application.Invoices;
using BusinessManagement.Application.Payments;
using BusinessManagement.Domain.Enums;
using FluentValidation;

namespace BusinessManagement.Application.Validation;

internal static class PaymentRules
{
    public static IRuleBuilderOptions<T, string> Mode<T>(this IRuleBuilder<T, string> r) =>
        r.NotEmpty().Must(m => Enum.TryParse<PaymentMode>(m, true, out var v) && Enum.IsDefined(v) && !int.TryParse(m, out _))
            .WithMessage("Payment mode must be one of: Cash, Upi, BankTransfer, Cheque, Card, Other.");
}

public sealed class RecordPaymentRequestValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentRequestValidator()
    {
        RuleFor(x => x.PaymentDate).BusinessDate();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.PaymentMode).Mode();
        RuleFor(x => x.TransactionReference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class UpdatePaymentRequestValidator : AbstractValidator<UpdatePaymentRequest>
{
    public UpdatePaymentRequestValidator()
    {
        RuleFor(x => x.PaymentDate).BusinessDate();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.PaymentMode).Mode();
        RuleFor(x => x.TransactionReference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class ReversePaymentRequestValidator : AbstractValidator<ReversePaymentRequest>
{
    public ReversePaymentRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public sealed class CancelInvoiceRequestValidator : AbstractValidator<CancelInvoiceRequest>
{
    public CancelInvoiceRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(300);
}

public sealed class InvoiceListQueryValidator : AbstractValidator<InvoiceListQuery>
{
    public InvoiceListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
    }
}
