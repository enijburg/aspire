using Microsoft.Extensions.Logging;

namespace Aspire.Testing.MSTest;

/// <summary>
/// An <see cref="ILoggerProvider"/> that writes log messages to <see cref="Console.Out"/>,
/// which MSTest captures per-test and includes in the test detail summary.
/// </summary>
/// <remarks>
/// Used in standalone mode so that test logging output (e.g. endpoint reports) appears
/// in the Test Explorer detail view. The format is intentionally minimal -- just the
/// log level, category, and message -- to keep test output readable.
/// </remarks>
internal sealed class TestContextLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(categoryName);

    public void Dispose()
    {
        // Nothing to dispose.
    }

    private sealed class ConsoleLogger(string categoryName) : ILogger
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
            Console.WriteLine($"[{logLevel}] {shortCategory}: {message}");

            if (exception is not null)
            {
                Console.WriteLine(exception.ToString());
            }
        }

        private static string GetShortCategory(string category)
        {
            var lastDot = category.LastIndexOf('.');
            return lastDot >= 0 ? category[(lastDot + 1)..] : category;
        }
    }
}
