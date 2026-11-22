using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BusinessManagement.API.Filters;

/// <summary>Runs the registered FluentValidation validator for every action argument; failures become 400 ProblemDetails.</summary>
public sealed class ValidationActionFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var arg in context.ActionArguments.Values)
        {
            if (arg is null) continue;
            if (services.GetService(typeof(IValidator<>).MakeGenericType(arg.GetType())) is not IValidator validator) continue;
            var result = await validator.ValidateAsync(new ValidationContext<object>(arg), context.HttpContext.RequestAborted);
            if (!result.IsValid) throw new ValidationException(result.Errors);
        }
        await next();
    }
}
