using BusinessManagement.Application.Common;
using BusinessManagement.Application.Customers;
using BusinessManagement.Application.Products;
using FluentValidation;

namespace BusinessManagement.Application.Validation;

public sealed class ProductListQueryValidator : AbstractValidator<ProductListQuery>
{
    public ProductListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Category).MaximumLength(80);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, false);
        RuleFor(x => x.GstRate).InclusiveBetween(0, 100).PrecisionScale(5, 2, false);   // slabs change by law; only the range is fixed here
        RuleFor(x => x.HsnCode).Matches("^[0-9]{4,8}$").When(x => !string.IsNullOrWhiteSpace(x.HsnCode)).WithMessage("HSN/SAC must be 4-8 digits.");
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(20);
    }
}

public sealed class SetProductBusinessesRequestValidator : AbstractValidator<SetProductBusinessesRequest>
{
    public SetProductBusinessesRequestValidator() => RuleFor(x => x.BusinessIds).NotNull();
}

public sealed class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    public CustomerListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class ResolveCustomerRequestValidator : AbstractValidator<ResolveCustomerRequest>
{
    public ResolveCustomerRequestValidator()
    {
        RuleFor(x => x.Mobile).Mobile();
        RuleFor(x => x.Name).MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(400);
        RuleFor(x => x.Gstin).GstinRule();
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$").When(x => !string.IsNullOrWhiteSpace(x.StateCode));
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(400);
        RuleFor(x => x.Gstin).GstinRule();
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$").When(x => !string.IsNullOrWhiteSpace(x.StateCode));
    }
}
