using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aspire.Testing.MSTest;

/// <summary>
/// Manages the lifecycle of an Aspire integration test, supporting both
/// <em>AppHost mode</em> (launched by the Aspire orchestrator) and <em>standalone mode</em>
/// (launched from Test Explorer, ReSharper, or CI via
/// <see cref="DistributedApplicationTestingBuilder"/>).
/// </summary>
/// <remarks>
/// <para>
/// Create an instance using <see cref="CreateBuilder()"/> and the fluent
/// <see cref="AspireIntegrationTestHostBuilder"/> API, then call
/// <see cref="StartAsync(CancellationToken)"/> to start the host.
/// </para>
/// <para>
/// In AppHost mode the host uses Aspire service-discovery URIs for named
/// <see cref="HttpClient"/> instances. In standalone mode a
/// <see cref="DistributedApplication"/> is built and started, resource endpoints are
/// resolved directly, and the lightweight <see cref="IHost"/> receives direct URIs
/// instead.
/// </para>
/// </remarks>
public sealed class AspireIntegrationTestHost : IHost, IAsyncDisposable
{
    private readonly AspireIntegrationTestHostOptions _options;
    private DistributedApplication? _app;
    private IHost? _host;
    private ILoggerFactory? _standaloneLoggerFactory;
    private ConcurrentQueue<string>? _startupLogBuffer;
    private TestContextLoggerProvider? _startupLogProvider;
    private bool _started;

    internal AspireIntegrationTestHost(AspireIntegrationTestHostOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// <see langword="true"/> when the test is running in standalone mode (the AppHost
    /// detection environment variable is absent or empty).
    /// </summary>
    public bool IsStandalone { get; private set; }

    /// <summary>
    /// Returns the log output captured during startup and switches subsequent
    /// logging to <see cref="Console.Out"/> where MSTest captures it per-test.
    /// Call this at the <strong>end</strong> of <c>[ClassInitialize]</c> and write
    /// the result to <c>TestContext</c> so startup logs appear in the class-level
    /// test detail summary rather than leaking into the first test method.
    /// </summary>
    /// <returns>
    /// The buffered startup log text, or <see cref="string.Empty"/> when not in
    /// standalone mode.
    /// </returns>
    public string FlushStartupLog()
    {
        _startupLogProvider?.StopBuffering();
        return _startupLogBuffer is not null
            ? string.Join(Environment.NewLine, _startupLogBuffer)
            : string.Empty;
    }

    /// <summary>
    /// The service provider from the lightweight host, providing access to
    /// <see cref="IHttpClientFactory"/>, <see cref="ILoggerFactory"/>, and other
    /// registered services.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when accessed before <see cref="AspireIntegrationTestHostBuilder.BuildAsync"/>
    /// has completed.
    /// </exception>
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException(
        "Host not built. Use AspireIntegrationTestHost.CreateBuilder().BuildAsync() to build the host.");

    /// <summary>
    /// Starts the lightweight host. Call this after obtaining the host from
    /// <see cref="AspireIntegrationTestHostBuilder.BuildAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the start operation.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null)
        {
            throw new InvalidOperationException(
                "Host not built. Use AspireIntegrationTestHost.CreateBuilder().BuildAsync() to build the host.");
        }

        if (!_started)
        {
            await _host.StartAsync(cancellationToken);
            _started = true;
        }
    }

    /// <summary>
    /// Stops the lightweight host and, if running in standalone mode, the
    /// <see cref="DistributedApplication"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the stop operation.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host is not null && _started)
        {
            await _host.StopAsync(cancellationToken);
            _started = false;
        }

        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> for the named resource, backed by the
    /// <see cref="IHttpClientFactory"/> registered in the lightweight host.
    /// </summary>
    public HttpClient CreateClient(string serviceName)
    {
        var factory = Services.GetRequiredService<IHttpClientFactory>();
        return factory.CreateClient(serviceName);
    }

    /// <summary>
    /// Creates a new <see cref="AspireIntegrationTestHostBuilder"/> for configuring and
    /// building an <see cref="AspireIntegrationTestHost"/>.
    /// </summary>
    /// <returns>A new builder instance.</returns>
    public static AspireIntegrationTestHostBuilder CreateBuilder() => new();

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_started)
        {
            await StopAsync();
        }

        _host?.Dispose();
        _host = null;

        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }

        _standaloneLoggerFactory?.Dispose();
    }

    // ─── Internal build logic (called by AspireIntegrationTestHostBuilder) ───

    internal async Task BuildInternalAsync()
    {
        // ── 1. Mode detection ────────────────────────────────────────────
        IsStandalone = string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable(_options.AppHostDetectionEnvVar));

        Dictionary<string, Uri>? resolvedEndpoints = null;

        // ── 2. Standalone path ───────────────────────────────────────────
        if (IsStandalone)
        {
            if (_options.CreateStandaloneBuilder is null)
            {
                throw new InvalidOperationException(
                    $"Running in standalone mode (environment variable " +
                    $"'{_options.AppHostDetectionEnvVar}' is not set) but no " +
                    $"CreateStandaloneBuilder factory was provided in the options.");
            }

            var testingBuilder = await _options.CreateStandaloneBuilder();

            // Allow the consumer to further configure the builder.
            _options.ConfigureStandaloneBuilder?.Invoke(testingBuilder);

            // Route all DA logs (including resource stdout) through our synchronous
            // TestContextLoggerProvider so they are written immediately rather than
            // arriving asynchronously via the DA's default console logger.
            // A buffer captures the startup output so the test can later write it
            // to TestContext for proper attribution in the test detail summary.
            _startupLogBuffer = new ConcurrentQueue<string>();
            _startupLogProvider = new TestContextLoggerProvider(_startupLogBuffer);
            _standaloneLoggerFactory = LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddProvider(_startupLogProvider);
            });

            testingBuilder.Services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new ForwardingLoggerProvider(_standaloneLoggerFactory));
                logging.AddFilter("Aspire.Hosting.Dcp", LogLevel.Warning);
            });

            _app = await testingBuilder.BuildAsync();

            var notificationService = _app.Services
                .GetRequiredService<ResourceNotificationService>();

            await _app.StartAsync();

            // Wait for named resources to reach Running and resolve their endpoints.
            resolvedEndpoints = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in _options.Resources)
            {
                await notificationService.WaitForResourceAsync(
                    resource.Name, KnownResourceStates.Running);

                var endpointName = resource.EndpointName
                    ?? DeriveEndpointName(resource.SchemePreference);

                resolvedEndpoints[resource.Name] = ResolveEndpoint(resource.Name, endpointName);
            }
        }

        // ── 3. Lightweight IHost construction ────────────────────────────
        var hostBuilder = Host.CreateApplicationBuilder();

        if (!IsStandalone)
        {
            // Apply service defaults (service discovery, resilience, OTLP, etc.)
            _options.ConfigureServiceDefaults?.Invoke(hostBuilder);
        }
        else
        {
            // Reuse the same buffered provider so that lightweight-host logs
            // (e.g. Lifetime "Application started") are also captured during
            // ClassInitialize and don't leak into the first test method.
            hostBuilder.Logging.ClearProviders();
            hostBuilder.Logging.AddProvider(_startupLogProvider!);
        }

        // Register OpenTelemetry tracing for the requested activity sources.
        if (_options.ActivitySourceNames.Count > 0)
        {
            hostBuilder.Services.AddOpenTelemetry()
                .WithTracing(tracing =>
                {
                    foreach (var name in _options.ActivitySourceNames)
                    {
                        tracing.AddSource(name);
                    }
                });
        }

        // Dev-cert bypass so HTTPS endpoints with self-signed certificates work.
        hostBuilder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        });

        // Named HTTP clients — service-discovery URIs in AppHost mode,
        // direct endpoint URIs in standalone mode.
        foreach (var resource in _options.Resources)
        {
            if (IsStandalone && resolvedEndpoints is not null
                             && resolvedEndpoints.TryGetValue(resource.Name, out var directUri))
            {
                hostBuilder.Services.AddHttpClient(resource.Name,
                    client => client.BaseAddress = directUri);
            }
            else
            {
                var discoveryUri = new Uri($"{resource.SchemePreference}://{resource.Name}");
                hostBuilder.Services.AddHttpClient(resource.Name,
                    client => client.BaseAddress = discoveryUri);
            }
        }

        // Let the consumer apply additional configuration.
        _options.ConfigureHostBuilder?.Invoke(hostBuilder);

        _host = hostBuilder.Build();
    }

    // ─── Private helpers ─────────────────────────────────────────────────

    private Uri ResolveEndpoint(string resourceName, string endpointName)
    {
        try
        {
            return _app!.GetEndpoint(resourceName, endpointName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // If the preferred endpoint is not available, try the fallback.
            var fallback = string.Equals(endpointName, "https", StringComparison.OrdinalIgnoreCase)
                ? "http" : "https";
            return _app!.GetEndpoint(resourceName, fallback);
        }
    }

    private static string DeriveEndpointName(string schemePreference) =>
        schemePreference.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
}
