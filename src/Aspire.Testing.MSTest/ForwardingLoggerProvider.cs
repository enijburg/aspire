using Microsoft.Extensions.Logging;

namespace Aspire.Testing.MSTest;

/// <summary>
/// An <see cref="ILoggerProvider"/> that forwards all log messages to an existing
/// <see cref="ILoggerFactory"/>.  Used internally to route resource stdout from the
/// <see cref="Aspire.Hosting.Testing.IDistributedApplicationTestingBuilder"/> into
/// the test's logging pipeline.
/// </summary>
internal sealed class ForwardingLoggerProvider(ILoggerFactory targetFactory) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => targetFactory.CreateLogger(categoryName);

    public void Dispose()
    {
        // The target factory is owned externally; do not dispose it here.
    }
}
