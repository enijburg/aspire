# Aspire.Testing.MSTest

A reusable library providing `AspireIntegrationTestHost` — an `IHost` implementation with a fluent builder API for writing integration tests against .NET Aspire distributed applications. It supports **dual-mode execution** so the same test code works both when orchestrated by an Aspire AppHost and when run standalone from an IDE or CI.

## Key Features

- **Dual-mode execution** — tests run under the Aspire orchestrator *or* standalone (Test Explorer, ReSharper, `dotnet test`) with no code changes.
- **Automatic mode detection** — checks for the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable (set by the Aspire orchestrator) to determine the execution mode.
- **Fluent builder API** — configure resources, activity sources, service defaults, and standalone builder factories with a chainable builder.
- **Named `HttpClient` registration** — call `CreateClient("resource-name")` to get a pre-configured `HttpClient` backed by `IHttpClientFactory`.
- **OpenTelemetry tracing** — `[TracedTestMethod]` wraps each test in an `Activity` so test spans appear in the Aspire dashboard.
- **Startup log buffering** — logs captured during `ClassInitialize` can be flushed to `TestContext` to prevent leakage into the first test method.
- **Dev-cert bypass** — HTTPS endpoints with self-signed development certificates are handled automatically.

## How It Works

### AppHost Mode

When the Aspire orchestrator launches the test project, `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The host uses Aspire service-discovery URIs (e.g. `https+http://api-one`) for named `HttpClient` instances and applies service defaults (service discovery, resilience, OTLP).

### Standalone Mode

When `OTEL_EXPORTER_OTLP_ENDPOINT` is absent (Test Explorer, CI, etc.), the host:

1. Creates a `DistributedApplication` via the factory supplied to `WithStandaloneBuilder`.
2. Starts the application and waits for each registered resource to reach the `Running` state.
3. Resolves resource endpoints directly and registers `HttpClient` instances with those URIs.

## Usage

### 1. Add a project reference

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Aspire.Testing.MSTest\Aspire.Testing.MSTest.csproj" />
</ItemGroup>
```

### 2. Build and start the test host

In your test class's `ClassInitialize`, create the host with the fluent builder:

```csharp
using Aspire.Hosting.Testing;
using Aspire.Testing.MSTest;

[TestClass]
public sealed class MyApiTests
{
    private static AspireIntegrationTestHost _testHost = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _testHost = await AspireIntegrationTestHost.CreateBuilder()
            .WithResource("api-one")
            .WithResource("api-two")
            .WithActivitySource(TracedTestMethodAttribute.TestActivitySource.Name)
            .WithServiceDefaults(builder => builder.AddServiceDefaults())
            .WithStandaloneBuilder(() =>
                DistributedApplicationTestingBuilder.CreateAsync<MyAppHost>())
            .BuildAsync();

        await _testHost.StartAsync();

        // Flush buffered startup logs so they appear in the class-level test output
        // rather than leaking into the first test method.
        _testHost.FlushStartupLog();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _testHost.DisposeAsync();
    }
}
```

### 3. Write test methods

Use `CreateClient` to obtain a named `HttpClient` for any registered resource:

```csharp
[TestMethod]
public async Task GetWeatherForecast_ReturnsOk()
{
    using var client = _testHost.CreateClient("api-one");
    var response = await client.GetAsync("/weatherforecast");
    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
}
```

### 4. Add OpenTelemetry tracing (optional)

Replace `[TestMethod]` with `[TracedTestMethod]` to automatically wrap each test in an OpenTelemetry `Activity`. Call `TestActivityScope.ReportStatusCode` to tag the span with the actual HTTP status code:

```csharp
[TracedTestMethod]
public async Task GetWeatherForecast_ReturnsOk()
{
    using var client = _testHost.CreateClient("api-one");
    var response = await client.GetAsync("/weatherforecast");
    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

    TestActivityScope.ReportStatusCode(response.StatusCode);
}

// Negative test — expects 401 Unauthorized
[TracedTestMethod(HttpStatusCode.Unauthorized)]
public async Task GetWeatherForecast_WithoutToken_ReturnsUnauthorized()
{
    using var client = _testHost.CreateClient("api-one");
    var response = await client.GetAsync("/weatherforecast");
    Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

    TestActivityScope.ReportStatusCode(response.StatusCode);
}
```

The attribute compares the reported status code against the expected one and sets `test.passed`, `test.actual_status_code`, and the `ActivityStatus` accordingly. If no status code is reported, the outcome is derived from the MSTest `UnitTestOutcome`.

## Builder API Reference

| Method | Description |
|---|---|
| `WithResource(name, schemePreference?, endpointName?)` | Registers a named `HttpClient` for the resource. In standalone mode, also waits for the resource to reach `Running` and resolves its endpoint. |
| `WithActivitySource(name)` | Registers an OpenTelemetry activity source so test spans appear in the Aspire dashboard. |
| `WithStandaloneBuilder(factory)` | Provides the async factory that creates the `IDistributedApplicationTestingBuilder` for standalone mode. |
| `ConfigureStandaloneBuilder(configure)` | Further configures the testing builder in standalone mode (e.g. reading configuration after the AppHost code runs). |
| `WithServiceDefaults(configure)` | Applies Aspire service defaults (service discovery, resilience, OTLP). Only invoked in AppHost mode. |
| `BuildAsync()` | Returns a configured but not-yet-started `AspireIntegrationTestHost`. Call `StartAsync()` to start it. |

## Public Types

| Type | Description |
|---|---|
| `AspireIntegrationTestHost` | The main host. Implements `IHost` and `IAsyncDisposable`. Provides `CreateClient(serviceName)`, `FlushStartupLog()`, and `IsStandalone`. |
| `AspireIntegrationTestHostBuilder` | Fluent builder obtained via `AspireIntegrationTestHost.CreateBuilder()`. |
| `ResourceEndpoint` | Record describing a resource endpoint (`Name`, `SchemePreference`, `EndpointName`). |
| `TracedTestMethodAttribute` | Custom `[TestMethod]` that wraps execution in an OpenTelemetry `Activity`. Accepts an optional `expectedStatusCode`. |
| `TestActivityScope` | Ambient scope (`AsyncLocal`) for reporting the observed HTTP status code from within a test method via `ReportStatusCode(statusCode)`. |

## Dependencies

- [Aspire.Hosting.Testing](https://www.nuget.org/packages/Aspire.Hosting.Testing) — `IDistributedApplicationTestingBuilder` and `DistributedApplicationTestingBuilder`
- [Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting) — `IHost`, `IHostApplicationBuilder`
- [MSTest](https://www.nuget.org/packages/MSTest) — `TestMethodAttribute`, `TestContext`
- [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting) — Activity source and tracing registration
