using BusinessManagement.Application.Adjustments;
using BusinessManagement.Application.Audit;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Financials;
using BusinessManagement.Application.Returns;
using FluentValidation;

namespace BusinessManagement.Application.Validation;

public sealed class ReturnItemRequestValidator : AbstractValidator<ReturnItemRequest>
{
    public ReturnItemRequestValidator()
    {
        RuleFor(x => x.SalesOrderItemId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 3, false);
    }
}

public sealed class CreateReturnRequestValidator : AbstractValidator<CreateReturnRequest>
{
    public CreateReturnRequestValidator()
    {
        RuleFor(x => x.ReturnDate).BusinessDate();
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.Items).NotNull().NotEmpty().WithMessage("Select at least one item to return.");
        RuleForEach(x => x.Items).SetValidator(new ReturnItemRequestValidator());
    }
}

public sealed class CreateAdjustmentRequestValidator : AbstractValidator<CreateAdjustmentRequest>
{
    public CreateAdjustmentRequestValidator()
    {
        RuleFor(x => x.AdjustmentDate).BusinessDate();
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.Direction).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(400);
    }
}

public sealed class AdjustmentListQueryValidator : AbstractValidator<AdjustmentListQuery>
{
    public AdjustmentListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
    }
}

public sealed class AuditLogQueryValidator : AbstractValidator<AuditLogQuery>
{
    public AuditLogQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.EntityName).MaximumLength(60);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
    }
}

public sealed class DashboardQueryValidator : AbstractValidator<DashboardQuery>
{
    public DashboardQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
        RuleFor(x => x.Basis).Must(b => string.IsNullOrWhiteSpace(b) || b.Equals("purchasedBy", StringComparison.OrdinalIgnoreCase) || b.Equals("createdBy", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Basis must be 'purchasedBy' or 'createdBy'.");
    }
}

public sealed class ReportQueryValidator : AbstractValidator<ReportQuery>
{
    public ReportQueryValidator() =>
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
}
