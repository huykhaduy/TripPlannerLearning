using Microsoft.Extensions.Logging;

namespace TripPlanner.Application.Tests.TestDoubles;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps what it was told, so tests can assert on
/// the log itself. Several classes in this solution treat logging as part of their
/// contract rather than as a side effect — <c>ResilientDistributedCache</c> swallows
/// failures and the Warning is the ONLY trace left, and the adapters promise to log
/// at the source before rethrowing. Moq can verify the generic <c>Log</c> call, but
/// only through an unreadable expression-tree matcher; this keeps the assertions
/// legible and lets them check the rendered message.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    public List<Entry> Entries { get; } = [];

    public IEnumerable<Entry> Warnings => Entries.Where(e => e.Level == LogLevel.Warning);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception), exception));
}
