using System.Net;
using Microsoft.AspNetCore.Mvc;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Domain.Exceptions;

namespace TripPlanner.WebApi.Middleware;

/// <summary>
/// Central exception handler (masterplan: "Error handling, validation, and
/// observability"). Converts thrown exceptions into consistent
/// <see cref="ProblemDetails"/> responses so controllers stay clean and never
/// need try/catch blocks. Register it early in the pipeline.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, title) = exception switch
        {
            ValidationException => (HttpStatusCode.BadRequest, "Validation failed"),
            DomainException => (HttpStatusCode.BadRequest, "Business rule violated"),
            UnauthorizedException => (HttpStatusCode.Unauthorized, "Authentication failed"),
            NotFoundException => (HttpStatusCode.NotFound, "Resource not found"),
            ConflictException => (HttpStatusCode.Conflict, "Conflict"),
            NotImplementedException => (HttpStatusCode.NotImplemented, "Not implemented yet"),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred"),
        };

        if (status == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception");
        }

        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = exception.Message,
        };

        // Attach field-level errors for validation failures.
        if (exception is ValidationException { Errors.Count: > 0 } validation)
        {
            problem.Extensions["errors"] = validation.Errors;
        }

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
