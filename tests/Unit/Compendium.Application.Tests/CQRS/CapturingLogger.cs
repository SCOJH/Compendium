// -----------------------------------------------------------------------
// <copyright file="CapturingLogger.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace Compendium.Application.Tests.CQRS;

/// <summary>
/// A minimal in-memory <see cref="ILogger{T}"/> implementation that records every log entry it
/// receives. Used to assert that the CQRS dispatchers log swallowed handler exceptions (P0-02)
/// before wrapping them into a <see cref="Result"/> failure. Avoids taking a dependency on
/// <c>Microsoft.Extensions.Diagnostics.Testing</c> just for the <c>FakeLogger</c> type.
/// </summary>
/// <typeparam name="T">The category type for the logger.</typeparam>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<CapturedLogEntry> _entries = [];

    /// <summary>
    /// Gets the captured log entries in the order they were recorded.
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> Entries => _entries;

    /// <inheritdoc />
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Add(new CapturedLogEntry(logLevel, exception, formatter(state, exception)));
    }

    /// <summary>
    /// Represents a single captured log entry.
    /// </summary>
    /// <param name="Level">The log level.</param>
    /// <param name="Exception">The exception associated with the entry, if any.</param>
    /// <param name="Message">The rendered log message.</param>
    public sealed record CapturedLogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
            // No-op.
        }
    }
}
