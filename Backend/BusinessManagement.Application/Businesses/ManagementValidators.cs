using BusinessManagement.Application.Businesses;
using BusinessManagement.Application.Common;
using BusinessManagement.Application.Users;
using BusinessManagement.Application.Auth;
using BusinessManagement.Domain.Enums;
using FluentValidation;

namespace BusinessManagement.Application.Validation;

internal static class Rules
{
    public static IRuleBuilderOptions<T, string> Mobile<T>(this IRuleBuilder<T, string> r) =>
        r.NotEmpty().Must(MobileNumber.IsValid).WithMessage("Enter a valid mobile number (10-15 digits).");

    public static IRuleBuilderOptions<T, string?> HttpsUrl<T>(this IRuleBuilder<T, string?> r) =>
        r.Must(u => string.IsNullOrWhiteSpace(u) || (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Must be an https URL.").MaximumLength(500);

    public static IRuleBuilderOptions<T, string?> GstinRule<T>(this IRuleBuilder<T, string?> r) =>
        r.Must(g => string.IsNullOrWhiteSpace(g) || Gstin.IsValid(g)).WithMessage("Enter a valid 15-character GSTIN.");
}

public sealed class UserListQueryValidator : AbstractValidator<UserListQuery>
{
    public UserListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Role).Must(r => string.IsNullOrWhiteSpace(r) ||
            (Enum.TryParse<UserRole>(r, true, out var role) && role is UserRole.Member or UserRole.Employee))
            .WithMessage("Role must be Member or Employee.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Username).NotEmpty().Length(3, 60).Matches("^[A-Za-z0-9._-]+$")
            .WithMessage("Username may contain letters, digits, '.', '_' and '-'.");
        RuleFor(x => x.Mobile).Mobile();
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DateOfBirth.HasValue)
            .WithMessage("Date of birth must be in the past.");
        RuleFor(x => x.AvatarUrl).HttpsUrl();
        RuleFor(x => x.Password).StrongPassword();
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Mobile).Mobile();
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.DateOfBirth).LessThan(DateOnly.FromDateTime(DateTime.UtcNow)).When(x => x.DateOfBirth.HasValue);
        RuleFor(x => x.AvatarUrl).HttpsUrl();
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Mobile).Mobile();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.AvatarUrl).HttpsUrl();
    }
}

public sealed class BusinessListQueryValidator : AbstractValidator<BusinessListQuery>
{
    public BusinessListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class CreateBusinessRequestValidator : AbstractValidator<CreateBusinessRequest>
{
    public CreateBusinessRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Category).MaximumLength(80);
        RuleFor(x => x.AddressLine).MaximumLength(400);
        RuleFor(x => x.GstNumber).GstinRule();
        RuleFor(x => x.LogoUrl).HttpsUrl();
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$").When(x => !string.IsNullOrWhiteSpace(x.StateCode)).WithMessage("State code must be 2 digits.");
        RuleFor(x => x.MemberIds).NotNull().NotEmpty().WithMessage("Select at least one member for the business.");
    }
}

public sealed class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.Category).MaximumLength(80);
        RuleFor(x => x.AddressLine).MaximumLength(400);
        RuleFor(x => x.GstNumber).GstinRule();
        RuleFor(x => x.LogoUrl).HttpsUrl();
        RuleFor(x => x.StateCode).Matches("^[0-9]{2}$").When(x => !string.IsNullOrWhiteSpace(x.StateCode));
    }
}

public sealed class SetBusinessMembersRequestValidator : AbstractValidator<SetBusinessMembersRequest>
{
    public SetBusinessMembersRequestValidator() =>
        RuleFor(x => x.MemberIds).NotNull().NotEmpty().WithMessage("Every business must have at least one assigned member.");
}
