using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TripPlanner.Application.Common.Exceptions;
using TripPlanner.Domain.Exceptions;
using TripPlanner.WebApi.Middleware;
using Xunit;

namespace TripPlanner.WebApi.Tests;

/// <summary>
/// The endpoint tests already prove the status mapping over real HTTP. What they
/// cannot reach is the pair of security decisions inside this middleware: hiding an
/// unmapped exception's message outside Development (it may carry SQL, table names,
/// or a connection string), and logging at Error for the 500 branch ONLY — 400/404/409
/// are an API's normal traffic, and logging them as errors buries the real ones.
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TripPlanner.WebApi";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class RecordingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> f) =>
            Entries.Add((level, ex));
    }

    private sealed record Handled(HttpStatusCode Status, string ContentType, JsonElement Body, RecordingLogger Logger);

    /// <summary>Runs the middleware over a pipeline that throws <paramref name="thrown"/> and reads the response back.</summary>
    private static async Task<Handled> RunAsync(Exception? thrown, string environment = "Production")
    {
        var logger = new RecordingLogger();
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        var sut = new ExceptionHandlingMiddleware(
            _ => thrown is null ? Task.CompletedTask : Task.FromException(thrown),
            logger,
            new StubHostEnvironment(environment));

        await sut.InvokeAsync(context);

        body.Position = 0;
        var json = body.Length == 0
            ? default
            : JsonDocument.Parse(body).RootElement;

        return new Handled((HttpStatusCode)context.Response.StatusCode, context.Response.ContentType ?? "", json, logger);
    }

    // ------------------------------------------------------------------
    // Status mapping
    // ------------------------------------------------------------------

    [Fact]
    public async Task WhenNothingThrows_ItLeavesTheResponseAlone()
    {
        // Paired with everything below: a middleware that always wrote a
        // ProblemDetails would still satisfy each individual mapping assertion.
        var result = await RunAsync(thrown: null);

        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Empty(result.ContentType);
        Assert.Empty(result.Logger.Entries);
    }

    [Fact]
    public async Task ValidationException_Becomes400()
    {
        var result = await RunAsync(new ValidationException("bad input"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Equal("Validation failed", result.Body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task DomainException_AlsoBecomes400_ButWithItsOwnTitle()
    {
        // A broken business rule (end date before start) is a 400 like a validation
        // failure, but the title says which kind so the two stay tellable apart.
        var result = await RunAsync(new DomainException("Trip start date must be on or before the end date."));

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Equal("Business rule violated", result.Body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnauthorizedException_Becomes401()
    {
        var result = await RunAsync(new UnauthorizedException("Invalid email or password."));

        Assert.Equal(HttpStatusCode.Unauthorized, result.Status);
        Assert.Equal("Authentication failed", result.Body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task ForbiddenException_Becomes403()
    {
        var result = await RunAsync(new ForbiddenException("Please verify your email address."));

        Assert.Equal(HttpStatusCode.Forbidden, result.Status);
    }

    [Fact]
    public async Task NotFoundException_Becomes404()
    {
        var result = await RunAsync(new NotFoundException("Trip not found."));

        Assert.Equal(HttpStatusCode.NotFound, result.Status);
    }

    [Fact]
    public async Task ConflictException_Becomes409()
    {
        var result = await RunAsync(new ConflictException("Already in that part of the trip."));

        Assert.Equal(HttpStatusCode.Conflict, result.Status);
    }

    [Fact]
    public async Task NotImplementedException_Becomes501()
    {
        // Nothing in the codebase throws this today; the branch stays for new
        // feature work that starts from a stub.
        var result = await RunAsync(new NotImplementedException());

        Assert.Equal(HttpStatusCode.NotImplemented, result.Status);
    }

    [Fact]
    public async Task AnythingElseBecomes500()
    {
        var result = await RunAsync(new InvalidOperationException("something internal"));

        Assert.Equal(HttpStatusCode.InternalServerError, result.Status);
    }

    [Fact]
    public async Task ConcurrencyException_IsNotMapped_AndFallsThroughTo500()
    {
        // Deliberate: callers are supposed to catch it and translate it into
        // something the user can act on. Reaching the middleware means one did not,
        // which is a bug and should look like one.
        var result = await RunAsync(new ConcurrencyException("conflict", new InvalidOperationException()));

        Assert.Equal(HttpStatusCode.InternalServerError, result.Status);
    }

    // ------------------------------------------------------------------
    // Response shape
    // ------------------------------------------------------------------

    [Fact]
    public async Task ItRepliesAsProblemJson()
    {
        var result = await RunAsync(new NotFoundException("Trip not found."));

        Assert.StartsWith("application/problem+json", result.ContentType);
        Assert.Equal(404, result.Body.GetProperty("status").GetInt32());
        Assert.Equal("Trip not found.", result.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AValidationFailureCarriesItsFieldErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Password"] = ["Password must be at least 8 characters."],
        };

        var result = await RunAsync(new ValidationException(errors));

        var field = result.Body.GetProperty("errors").GetProperty("Password");
        Assert.Equal("Password must be at least 8 characters.", field[0].GetString());
    }

    [Fact]
    public async Task AValidationExceptionWithNoFieldErrorsOmitsTheErrorsObject()
    {
        // The single-message constructor leaves Errors empty; emitting "errors": {}
        // would have the frontend render an empty list of field problems.
        var result = await RunAsync(new ValidationException("bad input"));

        Assert.False(result.Body.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task AMappedExceptionsMessageIsShownAsIs()
    {
        // These messages are written for the user — the frontend's getErrorMessage
        // reads exactly this field.
        var result = await RunAsync(new ConflictException("This destination is already in that part of the trip."));

        Assert.Equal(
            "This destination is already in that part of the trip.",
            result.Body.GetProperty("detail").GetString());
    }

    // ------------------------------------------------------------------
    // The two security decisions
    // ------------------------------------------------------------------

    [Fact]
    public async Task OutsideDevelopment_AnUnmappedExceptionsMessageIsHidden()
    {
        // The message may hold a SQL statement, a table name, or a connection string.
        var result = await RunAsync(
            new InvalidOperationException("Npgsql: relation \"users\" does not exist; Host=db;Password=hunter2"),
            environment: "Production");

        var detail = result.Body.GetProperty("detail").GetString();
        Assert.Equal("An unexpected error occurred.", detail);
        Assert.DoesNotContain("hunter2", detail);
    }

    [Fact]
    public async Task InDevelopment_AnUnmappedExceptionsMessageIsShown()
    {
        var result = await RunAsync(
            new InvalidOperationException("Npgsql: relation \"users\" does not exist"),
            environment: "Development");

        Assert.Contains("relation \"users\" does not exist", result.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AMappedExceptionKeepsItsMessageEvenInProduction()
    {
        // Only the unmapped branch is redacted; redacting the rest would replace
        // every useful 404/409 message with a generic one.
        var result = await RunAsync(new NotFoundException("Trip not found."), environment: "Production");

        Assert.Equal("Trip not found.", result.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task OnlyTheUnmappedBranchLogsAnError()
    {
        var result = await RunAsync(new InvalidOperationException("boom"));

        var entry = Assert.Single(result.Logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    [Fact]
    public async Task ExpectedFailuresAreNotLoggedAtAll()
    {
        // 404/409/400 are an API's ordinary traffic. Logging them at Error is how a
        // log stops being read, and how a genuine 500 gets missed.
        foreach (var expected in new Exception[]
                 {
                     new NotFoundException("Trip not found."),
                     new ConflictException("Already added."),
                     new ValidationException("bad input"),
                     new UnauthorizedException("Invalid email or password."),
                     new ForbiddenException("Verify your email."),
                 })
        {
            var result = await RunAsync(expected);

            Assert.Empty(result.Logger.Entries);
        }
    }
}
