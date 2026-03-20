using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aspire.Hosting.DevJwt;
using JwtAuth.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JwtAuth.Tests;

[TestClass]
public sealed class JwtAuthApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string _bothToken = null!;
    private static string _apiOneToken = null!;
    private static string _apiTwoToken = null!;
    private static IHost _host = null!;
    private static ILogger _logger = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // Read the pre-minted bearer tokens injected by the AppHost via WithNewDevJwtToken.
        _bothToken = Environment.GetEnvironmentVariable(SharedDevJwtEnvironmentNames.GetBearerTokenName("both-user"))
            ?? throw new InvalidOperationException(
                $"Bearer token not found. Ensure the test is run via the JwtAuth AppHost " +
                $"(expected env var: {SharedDevJwtEnvironmentNames.GetBearerTokenName("both-user")}).");

        _apiOneToken = Environment.GetEnvironmentVariable(SharedDevJwtEnvironmentNames.GetBearerTokenName("api-one-user"))
            ?? throw new InvalidOperationException(
                $"Bearer token not found. Ensure the test is run via the JwtAuth AppHost " +
                $"(expected env var: {SharedDevJwtEnvironmentNames.GetBearerTokenName("api-one-user")}).");

        _apiTwoToken = Environment.GetEnvironmentVariable(SharedDevJwtEnvironmentNames.GetBearerTokenName("api-two-user"))
            ?? throw new InvalidOperationException(
                $"Bearer token not found. Ensure the test is run via the JwtAuth AppHost " +
                $"(expected env var: {SharedDevJwtEnvironmentNames.GetBearerTokenName("api-two-user")}).");

        // Build a lightweight host that reuses the shared ServiceDefaults configuration:
        // OpenTelemetry (tracing + metrics + OTLP export), service discovery, and resilience.
        // Named HttpClients for api-one / api-two resolve via Aspire service discovery.
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.AddServiceDefaults();

        // Register the test ActivitySource so spans appear in the Aspire dashboard
        hostBuilder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(TracedTestMethodAttribute.TestActivitySource.Name));

        hostBuilder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        });

        hostBuilder.Services.AddHttpClient("api-one", client =>
            client.BaseAddress = new Uri("https+http://api-one"));
        hostBuilder.Services.AddHttpClient("api-two", client =>
            client.BaseAddress = new Uri("https+http://api-two"));

        _host = hostBuilder.Build();
        await _host.StartAsync();

        _logger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<JwtAuthApiTests>();
        _logger.LogInformation("Bearer tokens loaded from environment (injected by AppHost via WithNewDevJwtToken).");
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    // ------------------------------ ApiOne tests ------------------------------

    [TracedTestMethod]
    public async Task ApiOne_GetWeatherForecast_ReturnsForecasts()
    {
        using var client = CreateAuthenticatedClient("api-one", _bothToken);

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var forecasts = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.IsNotNull(forecasts);
        Assert.IsTrue(forecasts.Length > 0, "Expected at least one weather forecast.");

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast", response.StatusCode, body);
    }

    [TracedTestMethod]
    public async Task ApiOne_GetMe_ReturnsAuthenticatedUser()
    {
        using var client = CreateAuthenticatedClient("api-one", _bothToken);

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("ApiOne", user.GetProperty("service").GetString());

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /me", response.StatusCode, body);
    }

    // ------------------------------ ApiTwo tests ------------------------------

    [TracedTestMethod]
    public async Task ApiTwo_GetProducts_ReturnsProductCatalogue()
    {
        using var client = CreateAuthenticatedClient("api-two", _bothToken);

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.IsNotNull(products);
        Assert.IsTrue(products.Length > 0, "Expected at least one product.");

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /products", response.StatusCode, body);
    }

    [TracedTestMethod]
    public async Task ApiTwo_GetProductById_ReturnsProduct()
    {
        using var client = CreateAuthenticatedClient("api-two", _bothToken);

        var response = await client.GetAsync("/products/1");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var product = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual(1, product.GetProperty("id").GetInt32());

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /products/1", response.StatusCode, body);
    }

    [TracedTestMethod(HttpStatusCode.NotFound)]
    public async Task ApiTwo_GetProductById_ReturnsNotFoundForMissing()
    {
        using var client = CreateAuthenticatedClient("api-two", _bothToken);

        var response = await client.GetAsync("/products/9999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /products/9999", response.StatusCode, "(empty - 404 Not Found)");
    }

    [TracedTestMethod]
    public async Task ApiTwo_GetMe_ReturnsAuthenticatedUser()
    {
        using var client = CreateAuthenticatedClient("api-two", _bothToken);

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("ApiTwo", user.GetProperty("service").GetString());

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /me", response.StatusCode, body);
    }

    // ------------------------ Unauthorized access tests -----------------------

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiOne_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-one");

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiOne_GetMe_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-one");

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /me (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiTwo_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-two");

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /products (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiTwo_GetProductById_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-two");

        var response = await client.GetAsync("/products/1");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /products/1 (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiTwo_GetMe_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-two");

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /me (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    // ----------------------- Role-based access tests -------------------------

    [TracedTestMethod]
    public async Task ApiOne_WithApiOneToken_ReturnsForecasts()
    {
        using var client = CreateAuthenticatedClient("api-one", _apiOneToken);

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        LogEndpointReport("ApiOne", "GET /weatherforecast (api-one role)", response.StatusCode, body);
    }

    [TracedTestMethod(HttpStatusCode.Forbidden)]
    public async Task ApiOne_WithApiTwoTokenOnly_ReturnsForbidden()
    {
        using var client = CreateAuthenticatedClient("api-one", _apiTwoToken);

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast (api-two role only)", response.StatusCode, "(empty - 403 Forbidden)");
    }

    [TracedTestMethod]
    public async Task ApiTwo_WithApiTwoToken_ReturnsProducts()
    {
        using var client = CreateAuthenticatedClient("api-two", _apiTwoToken);

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        LogEndpointReport("ApiTwo", "GET /products (api-two role)", response.StatusCode, body);
    }

    [TracedTestMethod(HttpStatusCode.Forbidden)]
    public async Task ApiTwo_WithApiOneTokenOnly_ReturnsForbidden()
    {
        using var client = CreateAuthenticatedClient("api-two", _apiOneToken);

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiTwo", "GET /products (api-one role only)", response.StatusCode, "(empty - 403 Forbidden)");
    }


    // --------------------------------- Helpers --------------------------------

    private static HttpClient CreateAuthenticatedClient(string serviceName, string token)
    {
        var client = CreateUnauthenticatedClient(serviceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient CreateUnauthenticatedClient(string serviceName)
    {
        var factory = _host.Services.GetRequiredService<IHttpClientFactory>();
        return factory.CreateClient(serviceName);
    }

    private static string PrettyPrint(string json)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            return JsonSerializer.Serialize(element, JsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void LogEndpointReport(string api, string endpoint, HttpStatusCode status, string body)
    {
        var prettyBody = PrettyPrint(body);
        _logger.LogInformation("""
                                 [{Api}] {Endpoint} => {StatusCode} {Status}
                                 {Body}
                                 """, api, endpoint, (int)status, status, prettyBody);
    }
}
