using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.DevJwt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JwtAuth.Tests;

[TestClass]
public sealed class JwtAuthApiTests
{
    private static readonly JsonSerializerOptions SJsonOptions = new() { WriteIndented = true };

    private static string? _sToken;
    private static IHost? _sHost;
    private static ILogger? _sLogger;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // Read JWT signing key from env var injected by AppHost via WithSharedDevJwt
        var signingKey = Environment.GetEnvironmentVariable(SharedDevJwtEnvironmentNames.SigningKeyValue)
            ?? throw new InvalidOperationException(
                $"JWT signing key not found. Ensure the test is run via the JwtAuth AppHost " +
                $"(expected env var: {SharedDevJwtEnvironmentNames.SigningKeyValue}).");

        var issuer = Environment.GetEnvironmentVariable(SharedDevJwtEnvironmentNames.ValidIssuer)
            ?? SharedDevJwtAuthority.DefaultIssuer;

        var audience = Environment.GetEnvironmentVariable(SharedDevJwtEnvironmentNames.ValidAudiences)
            ?? SharedDevJwtAuthority.DefaultAudience;

        _sToken = JwtTokenFactory.CreateToken(
            signingKey: signingKey,
            issuer: issuer,
            audience: audience,
            subject: "test-user",
            expiry: TimeSpan.FromMinutes(30),
            roles: ["admin", "reader"]);

        // Build a lightweight host that reuses the shared ServiceDefaults configuration:
        // OpenTelemetry (tracing + metrics + OTLP export), service discovery, and resilience.
        // Named HttpClients for api-one / api-two resolve via Aspire service discovery.
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.AddServiceDefaults();

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

        _sHost = hostBuilder.Build();
        await _sHost.StartAsync();

        _sLogger = _sHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger<JwtAuthApiTests>();
        _sLogger.LogInformation("JWT generated for subject 'test-user' (issuer: {Issuer}, audience: {Audience})", issuer, audience);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_sHost is not null)
        {
            await _sHost.StopAsync();
            _sHost.Dispose();
        }
    }

    // ------------------------------ ApiOne tests ------------------------------

    [TestMethod]
    public async Task ApiOne_GetWeatherForecast_ReturnsForecasts()
    {
        using var client = CreateAuthenticatedClient("api-one");

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var forecasts = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.IsNotNull(forecasts);
        Assert.IsTrue(forecasts.Length > 0, "Expected at least one weather forecast.");

        LogEndpointReport("ApiOne", "GET /weatherforecast", response.StatusCode, body);
    }

    [TestMethod]
    public async Task ApiOne_GetMe_ReturnsAuthenticatedUser()
    {
        using var client = CreateAuthenticatedClient("api-one");

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("ApiOne", user.GetProperty("service").GetString());

        LogEndpointReport("ApiOne", "GET /me", response.StatusCode, body);
    }

    // ------------------------------ ApiTwo tests ------------------------------

    [TestMethod]
    public async Task ApiTwo_GetProducts_ReturnsProductCatalogue()
    {
        using var client = CreateAuthenticatedClient("api-two");

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var products = JsonSerializer.Deserialize<JsonElement[]>(body);
        Assert.IsNotNull(products);
        Assert.IsTrue(products.Length > 0, "Expected at least one product.");

        LogEndpointReport("ApiTwo", "GET /products", response.StatusCode, body);
    }

    [TestMethod]
    public async Task ApiTwo_GetProductById_ReturnsProduct()
    {
        using var client = CreateAuthenticatedClient("api-two");

        var response = await client.GetAsync("/products/1");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var product = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual(1, product.GetProperty("id").GetInt32());

        LogEndpointReport("ApiTwo", "GET /products/1", response.StatusCode, body);
    }

    [TestMethod]
    public async Task ApiTwo_GetProductById_ReturnsNotFoundForMissing()
    {
        using var client = CreateAuthenticatedClient("api-two");

        var response = await client.GetAsync("/products/9999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        LogEndpointReport("ApiTwo", "GET /products/9999", response.StatusCode, "(empty - 404 Not Found)");
    }

    [TestMethod]
    public async Task ApiTwo_GetMe_ReturnsAuthenticatedUser()
    {
        using var client = CreateAuthenticatedClient("api-two");

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("ApiTwo", user.GetProperty("service").GetString());

        LogEndpointReport("ApiTwo", "GET /me", response.StatusCode, body);
    }

    // ------------------------ Unauthorized access tests -----------------------

    [TestMethod]
    public async Task ApiOne_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-one");

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        LogEndpointReport("ApiOne", "GET /weatherforecast (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TestMethod]
    public async Task ApiTwo_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient("api-two");

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        LogEndpointReport("ApiTwo", "GET /products (no token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    // ----------------------------- Summary report ----------------------------

    [TestMethod]
    public async Task PrintFullApiReport()
    {
        var report = new StringBuilder();
        report.AppendLine();
        report.AppendLine("======================================================================");
        report.AppendLine("  JwtAuth API Integration Test Report");
        report.AppendLine("======================================================================");
        report.AppendLine();

        // ApiOne endpoints
        report.AppendLine("  --- API ONE (api-one) ---");

        using (var client = CreateAuthenticatedClient("api-one"))
        {
            await AppendEndpointResult(report, client, "GET", "/weatherforecast");
            await AppendEndpointResult(report, client, "GET", "/me");
        }

        report.AppendLine();

        // ApiTwo endpoints
        report.AppendLine("  --- API TWO (api-two) ---");

        using (var client = CreateAuthenticatedClient("api-two"))
        {
            await AppendEndpointResult(report, client, "GET", "/products");
            await AppendEndpointResult(report, client, "GET", "/products/1");
            await AppendEndpointResult(report, client, "GET", "/products/9999");
            await AppendEndpointResult(report, client, "GET", "/me");
        }

        report.AppendLine();

        // Unauthorized access
        report.AppendLine("  --- UNAUTHORIZED ACCESS (no bearer token) ---");

        using (var noAuthClient = CreateUnauthenticatedClient("api-one"))
        {
            await AppendEndpointResult(report, noAuthClient, "GET", "/weatherforecast");
        }

        using (var noAuthClient = CreateUnauthenticatedClient("api-two"))
        {
            await AppendEndpointResult(report, noAuthClient, "GET", "/products");
        }

        report.AppendLine();
        report.AppendLine("======================================================================");

        _sLogger!.LogInformation("{Report}", report.ToString());
    }

    // --------------------------------- Helpers --------------------------------

    private static HttpClient CreateAuthenticatedClient(string serviceName)
    {
        var client = CreateUnauthenticatedClient(serviceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _sToken);
        return client;
    }

    private static HttpClient CreateUnauthenticatedClient(string serviceName)
    {
        var factory = _sHost!.Services.GetRequiredService<IHttpClientFactory>();
        return factory.CreateClient(serviceName);
    }

    private static async Task AppendEndpointResult(StringBuilder report, HttpClient client, string method, string path)
    {
        var response = await client.GetAsync(path);
        var statusCode = (int)response.StatusCode;
        var statusText = response.StatusCode.ToString();
        var body = response.IsSuccessStatusCode
            ? PrettyPrint(await response.Content.ReadAsStringAsync())
            : $"({statusCode} {statusText})";

        report.AppendLine($"    {method} {path}");
        report.AppendLine($"      Status: {statusCode} {statusText}");

        foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
        {
            report.AppendLine($"      {line}");
        }

        report.AppendLine();
    }

    private static string PrettyPrint(string json)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            return JsonSerializer.Serialize(element, SJsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void LogEndpointReport(string api, string endpoint, HttpStatusCode status, string body)
    {
        var prettyBody = PrettyPrint(body);
        _sLogger!.LogInformation("[{Api}] {Endpoint} => {StatusCode} {Status}\n{Body}", api, endpoint, (int)status, status, prettyBody);
    }
}
