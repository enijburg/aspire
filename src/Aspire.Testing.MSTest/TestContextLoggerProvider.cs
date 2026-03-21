using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Aspire.Testing.MSTest;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes log messages to <see cref="Console.Out"/>,
/// which MSTest captures per-test and includes in the test detail summary.
/// </summary>
/// <remarks>
/// <para>
/// Used in standalone mode so that test logging output (e.g. endpoint reports) appears
/// in the Test Explorer detail view. The format is intentionally minimal — just the
/// log level, category, and message — to keep test output readable.
/// </para>
/// <para>
/// When constructed with a <see cref="ConcurrentQueue{T}"/> buffer, log lines are
/// captured <strong>only</strong> into the buffer (not written to <see cref="Console.Out"/>).
/// This prevents startup logs from leaking into the first test method's output.
/// Call <see cref="StopBuffering"/> after startup completes; subsequent log entries are
/// then written to <see cref="Console.Out"/> where MSTest captures them per-test.
/// </para>
/// </remarks>
internal sealed class TestContextLoggerProvider(ConcurrentQueue<string>? buffer = null) : ILoggerProvider
{
    private volatile ConcurrentQueue<string>? _buffer = buffer;

    /// <summary>
    /// Stops capturing log lines into the buffer. Subsequent log entries are
    /// still written to <see cref="Console.Out"/> but no longer buffered.
    /// </summary>
    internal void StopBuffering() => _buffer = null;

    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName, this);

    public void Dispose()
    {
        // Nothing to dispose.
    }

    private sealed class ConsoleLogger(string categoryName, TestContextLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var shortCategory = GetShortCategory(categoryName);
            var message = formatter(state, exception);
            var line = $"[{logLevel}] {shortCategory}: {message}";

            var buffer = provider._buffer;
            if (buffer is not null)
            {
                buffer.Enqueue(line);
            }
            else
            {
                Console.WriteLine(line);
            }

            if (exception is not null)
            {
                var exceptionText = exception.ToString();
                if (buffer is not null)
                {
                    buffer.Enqueue(exceptionText);
                }
                else
                {
                    Console.WriteLine(exceptionText);
                }
            }
        }

        private static string GetShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }
    }
}
