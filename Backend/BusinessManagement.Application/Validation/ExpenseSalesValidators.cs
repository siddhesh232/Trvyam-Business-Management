using BusinessManagement.Application.Common;
using BusinessManagement.Application.Expenses;
using BusinessManagement.Application.Sales;
using FluentValidation;

namespace BusinessManagement.Application.Validation;

internal static class DateRules
{
    /// <summary>Business dates may not be absurdly old or in the future (1 day of slack for time zones).</summary>
    public static IRuleBuilderOptions<T, DateOnly> BusinessDate<T>(this IRuleBuilder<T, DateOnly> r) =>
        r.Must(d => d >= new DateOnly(2000, 1, 1) && d <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            .WithMessage("Date must be between 2000-01-01 and tomorrow.");
}

public sealed class ExpenseListQueryValidator : AbstractValidator<ExpenseListQuery>
{
    public ExpenseListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
    }
}

public sealed class ExpenseRangeQueryValidator : AbstractValidator<ExpenseRangeQuery>
{
    public ExpenseRangeQueryValidator() =>
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
}

public sealed class ExpenseByMemberQueryValidator : AbstractValidator<ExpenseByMemberQuery>
{
    public ExpenseByMemberQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
        RuleFor(x => x.Basis).Must(b => string.IsNullOrWhiteSpace(b) || b.Equals("purchasedBy", StringComparison.OrdinalIgnoreCase) || b.Equals("createdBy", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Basis must be 'purchasedBy' or 'createdBy'.");
    }
}

public sealed class SaveExpenseRequestValidator : AbstractValidator<SaveExpenseRequest>
{
    public SaveExpenseRequestValidator()
    {
        RuleFor(x => x.ExpenseDate).BusinessDate();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 3, false);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.InputGstAmount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
    }
}

public sealed class SalesListQueryValidator : AbstractValidator<SalesListQuery>
{
    public SalesListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From!.Value).When(x => x.From.HasValue && x.To.HasValue).WithMessage("'To' must not be before 'From'.");
    }
}

public sealed class SaleItemRequestValidator : AbstractValidator<SaleItemRequest>
{
    public SaleItemRequestValidator()
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 3, false);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false).When(x => x.UnitPrice.HasValue);
        RuleFor(x => x.ItemDiscount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
    }
}

public sealed class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequest>
{
    public CreateSaleRequestValidator()
    {
        RuleFor(x => x.CustomerId).GreaterThan(0);
        RuleFor(x => x.OrderDate).BusinessDate();
        RuleFor(x => x.Items).NotNull().NotEmpty().WithMessage("Add at least one product.");
        RuleFor(x => x.Items.Count).LessThanOrEqualTo(200).When(x => x.Items is not null).WithMessage("A sale can have at most 200 lines.");
        RuleForEach(x => x.Items).SetValidator(new SaleItemRequestValidator());
        RuleFor(x => x.OrderDiscount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
    }
}

public sealed class UpdateSaleRequestValidator : AbstractValidator<UpdateSaleRequest>
{
    public UpdateSaleRequestValidator()
    {
        RuleFor(x => x.OrderDate).BusinessDate();
        RuleFor(x => x.Items).NotNull().NotEmpty().WithMessage("Add at least one product.");
        RuleForEach(x => x.Items).SetValidator(new SaleItemRequestValidator());
        RuleFor(x => x.OrderDiscount).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
    }
}
