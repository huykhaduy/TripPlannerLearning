using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
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
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
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
            ForbiddenException => (HttpStatusCode.Forbidden, "Forbidden"),
            NotFoundException => (HttpStatusCode.NotFound, "Resource not found"),
            ConflictException => (HttpStatusCode.Conflict, "Conflict"),
            NotImplementedException => (HttpStatusCode.NotImplemented, "Not implemented yet"),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred"),
        };

        if (status == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception");
        }

        // exception.Message is safe to echo back for the exceptions above: each is
        // thrown deliberately from the Application layer with a client-facing
        // message. An exception that falls through to the generic 500 branch was
        // NOT written for client consumption — it may be a raw DbUpdateException,
        // NullReferenceException, etc. whose message can contain SQL/internal
        // details, so only expose it outside Development.
        var detail = status == HttpStatusCode.InternalServerError && !_environment.IsDevelopment()
            ? "An unexpected error occurred."
            : exception.Message;

        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Detail = detail,
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
