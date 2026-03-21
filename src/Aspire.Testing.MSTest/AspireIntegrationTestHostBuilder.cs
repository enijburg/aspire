using Aspire.Hosting.Testing;
using Microsoft.Extensions.Hosting;

namespace Aspire.Testing.MSTest;

/// <summary>
/// A builder for creating and configuring an <see cref="AspireIntegrationTestHost"/>.
/// Obtain an instance via <see cref="AspireIntegrationTestHost.CreateBuilder()"/>.
/// </summary>
public sealed class AspireIntegrationTestHostBuilder
{
    private readonly List<ResourceEndpoint> _resources = [];
    private readonly List<string> _activitySourceNames = [];
    private const string AppHostDetectionEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private Func<Task<IDistributedApplicationTestingBuilder>>? _createStandaloneBuilder;
    private Action<IDistributedApplicationTestingBuilder>? _configureStandaloneBuilder;
    private Action<IHostApplicationBuilder>? _configureServiceDefaults;

    /// <summary>
    /// Adds a resource endpoint to register a named <see cref="HttpClient"/> for.
    /// In standalone mode the host also waits for the resource to reach the
    /// <c>Running</c> state and resolves its endpoint.
    /// </summary>
    /// <param name="name">The Aspire resource name (e.g. <c>"api-one"</c>).</param>
    /// <param name="schemePreference">
    /// The scheme prefix used for service-discovery URIs when running under an AppHost
    /// (e.g. <c>"https+http"</c>). Defaults to <c>"https+http"</c>.
    /// </param>
    /// <param name="endpointName">
    /// Optional explicit endpoint name to resolve in standalone mode. When
    /// <see langword="null"/> the endpoint name is derived from
    /// <paramref name="schemePreference"/>.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public AspireIntegrationTestHostBuilder WithResource(
        string name,
        string schemePreference = "https+http",
        string? endpointName = null)
    {
        _resources.Add(new ResourceEndpoint(name, schemePreference, endpointName));
        return this;
    }

    /// <summary>
    /// Adds an activity source name to register with OpenTelemetry tracing so that
    /// test spans appear in the Aspire dashboard.
    /// </summary>
    /// <param name="name">The activity source name.</param>
    /// <returns>This builder for chaining.</returns>
    public AspireIntegrationTestHostBuilder WithActivitySource(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _activitySourceNames.Add(name);
        return this;
    }

    /// <summary>
    /// Sets the factory that creates the <see cref="IDistributedApplicationTestingBuilder"/>
    /// for standalone mode. Required when running outside of an Aspire AppHost.
    /// </summary>
    /// <param name="factory">
    /// An async factory that creates and returns the testing builder.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public AspireIntegrationTestHostBuilder WithStandaloneBuilder(
        Func<Task<IDistributedApplicationTestingBuilder>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _createStandaloneBuilder = factory;
        return this;
    }

    /// <summary>
    /// Configures the <see cref="IDistributedApplicationTestingBuilder"/> in standalone
    /// mode (e.g. adding projects, containers, or environment variables).
    /// </summary>
    /// <param name="configure">The configuration callback.</param>
    /// <returns>This builder for chaining.</returns>
    public AspireIntegrationTestHostBuilder ConfigureStandaloneBuilder(
        Action<IDistributedApplicationTestingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureStandaloneBuilder = configure;
        return this;
    }

    /// <summary>
    /// Configures Aspire service defaults (service discovery, resilience, OpenTelemetry,
    /// etc.) on the lightweight host builder. Only invoked in AppHost mode; in standalone
    /// mode service discovery is unnecessary because HTTP clients receive direct endpoint
    /// URIs.
    /// </summary>
    /// <param name="configure">
    /// A callback that applies service defaults (e.g.
    /// <c>builder =&gt; builder.AddServiceDefaults()</c>).
    /// </param>
    /// <returns>This builder for chaining.</returns>
    public AspireIntegrationTestHostBuilder WithServiceDefaults(
        Action<IHostApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configureServiceDefaults = configure;
        return this;
    }

    /// <summary>
    /// Builds the <see cref="AspireIntegrationTestHost"/>. In standalone mode this also
    /// creates and starts the <see cref="Aspire.Hosting.DistributedApplication"/>,
    /// waits for resources to reach the <c>Running</c> state, and resolves their endpoints.
    /// </summary>
    /// <returns>
    /// A configured but not yet started <see cref="AspireIntegrationTestHost"/>.
    /// Call <see cref="IHost.StartAsync(CancellationToken)"/> to start the host.
    /// </returns>
    public async Task<AspireIntegrationTestHost> BuildAsync()
    {
        var options = new AspireIntegrationTestHostOptions
        {
            AppHostDetectionEnvVar = AppHostDetectionEnvVar,
            Resources = _resources,
            CreateStandaloneBuilder = _createStandaloneBuilder,
            ConfigureStandaloneBuilder = _configureStandaloneBuilder,
            ConfigureServiceDefaults = _configureServiceDefaults,
            ActivitySourceNames = _activitySourceNames,
        };

        var host = new AspireIntegrationTestHost(options);
        await host.BuildInternalAsync();
        return host;
    }
}
