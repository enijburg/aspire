using Aspire.Hosting.Testing;
using Microsoft.Extensions.Hosting;

namespace Aspire.Testing.MSTest;

/// <summary>
/// Configuration options for <see cref="AspireIntegrationTestHost"/>.
/// </summary>
public sealed class AspireIntegrationTestHostOptions
{
    /// <summary>
    /// The name of the environment variable whose presence indicates the test is running
    /// under an Aspire AppHost (orchestrator mode). When the variable is absent or empty
    /// the host operates in standalone mode.
    /// </summary>
    /// <remarks>
    /// The default value is <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> which the Aspire orchestrator
    /// always sets for managed resources.
    /// </remarks>
    public string AppHostDetectionEnvVar { get; set; } = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// The list of resources to register named <see cref="System.Net.Http.HttpClient"/>
    /// instances for. In standalone mode the host also waits for each resource to reach
    /// the <c>Running</c> state and resolves its endpoint.
    /// </summary>
    public IReadOnlyList<ResourceEndpoint> Resources { get; set; } = [];

    /// <summary>
    /// A factory that creates and returns the <see cref="IDistributedApplicationTestingBuilder"/>
    /// used in standalone mode. Typically the consumer calls
    /// <c>DistributedApplicationTestingBuilder.CreateAsync&lt;TEntryPoint&gt;()</c>
    /// inside this callback.
    /// </summary>
    /// <remarks>
    /// Required when running in standalone mode. Ignored in AppHost mode.
    /// </remarks>
    public Func<Task<IDistributedApplicationTestingBuilder>>? CreateStandaloneBuilder { get; set; }

    /// <summary>
    /// An optional callback invoked after <see cref="CreateStandaloneBuilder"/> to further
    /// configure the testing builder (e.g. adding projects, containers, or environment
    /// variables).
    /// </summary>
    public Action<IDistributedApplicationTestingBuilder>? ConfigureStandaloneBuilder { get; set; }

    /// <summary>
    /// A callback that applies Aspire service-defaults (service discovery, resilience,
    /// OpenTelemetry, etc.) to the lightweight <see cref="IHost"/> builder. This callback
    /// is invoked only in AppHost mode; in standalone mode service discovery is unnecessary
    /// because HTTP clients receive direct endpoint URIs.
    /// </summary>
    public Action<IHostApplicationBuilder>? ConfigureServiceDefaults { get; set; }

    /// <summary>
    /// Activity source names to register with OpenTelemetry tracing so that test spans
    /// appear in the Aspire dashboard.
    /// </summary>
    public IReadOnlyList<string> ActivitySourceNames { get; set; } = [];
}
