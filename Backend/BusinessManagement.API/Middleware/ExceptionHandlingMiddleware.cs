using BusinessManagement.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace BusinessManagement.API.Middleware;

/// <summary>Centralised exception handling -> RFC 7807 ProblemDetails. Internal details are never leaked.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499; // client closed request
        }
        catch (Exception ex)
        {
            await WriteProblemAsync(context, ex);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex)
    {
        var (status, title, code, errors) = ex switch
        {
            ValidationException ve => (StatusCodes.Status400BadRequest, "Validation failed", "validation_error",
                ve.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
            NotFoundException => (StatusCodes.Status404NotFound, ex.Message, "not_found", null),
            ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message, "forbidden", null),
            UnauthorizedException => (StatusCodes.Status401Unauthorized, ex.Message, "unauthorized", null),
            ConflictException => (StatusCodes.Status409Conflict, ex.Message, "conflict", null),
            DomainException de => (StatusCodes.Status422UnprocessableEntity, de.Message, de.Code, null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "server_error", null)
        };

        if (status >= 500)
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("Request rejected ({Status} {Code}) for {Method} {Path}", status, code, context.Request.Method, context.Request.Path);

        if (context.Response.HasStarted) return;
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails { Status = status, Title = title, Instance = context.Request.Path };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (errors is not null) problem.Extensions["errors"] = errors;
        await context.Response.WriteAsJsonAsync(problem);
    }
}
