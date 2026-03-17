using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.DevJwt;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JwtAuth.Tests;

[TestClass]
public sealed class JwtAuthApiTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private static string? s_token;
    private static string? s_apiOneUrl;
    private static string? s_apiTwoUrl;

    [ClassInitialize]
    public static Task ClassInitialize(TestContext context)
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

        s_token = JwtTokenFactory.CreateToken(
            signingKey: signingKey,
            issuer: issuer,
            audience: audience,
            subject: "test-user",
            expiry: TimeSpan.FromMinutes(30),
            roles: ["admin", "reader"]);

        Console.WriteLine($"[Setup] JWT generated for subject 'test-user' (issuer: {issuer}, audience: {audience})");

        // Read API base URLs from env vars injected by AppHost via WithReference
        s_apiOneUrl = ResolveServiceUrl("api-one");
        s_apiTwoUrl = ResolveServiceUrl("api-two");

        Console.WriteLine($"[Setup] api-one URL: {s_apiOneUrl}");
        Console.WriteLine($"[Setup] api-two URL: {s_apiTwoUrl}");
        Console.WriteLine();

        return Task.CompletedTask;
    }

    // ───────────────────────────── ApiOne tests ─────────────────────────────

    [TestMethod]
    public async Task ApiOne_GetWeatherForecast_ReturnsForecasts()
    {
        using var client = CreateAuthenticatedClient(s_apiOneUrl!);

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
        using var client = CreateAuthenticatedClient(s_apiOneUrl!);

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("ApiOne", user.GetProperty("service").GetString());

        LogEndpointReport("ApiOne", "GET /me", response.StatusCode, body);
    }

    // ───────────────────────────── ApiTwo tests ─────────────────────────────

    [TestMethod]
    public async Task ApiTwo_GetProducts_ReturnsProductCatalogue()
    {
        using var client = CreateAuthenticatedClient(s_apiTwoUrl!);

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
        using var client = CreateAuthenticatedClient(s_apiTwoUrl!);

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
        using var client = CreateAuthenticatedClient(s_apiTwoUrl!);

        var response = await client.GetAsync("/products/9999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        LogEndpointReport("ApiTwo", "GET /products/9999", response.StatusCode, "(empty – 404 Not Found)");
    }

    [TestMethod]
    public async Task ApiTwo_GetMe_ReturnsAuthenticatedUser()
    {
        using var client = CreateAuthenticatedClient(s_apiTwoUrl!);

        var response = await client.GetAsync("/me");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<JsonElement>(body);
        Assert.AreEqual("ApiTwo", user.GetProperty("service").GetString());

        LogEndpointReport("ApiTwo", "GET /me", response.StatusCode, body);
    }

    // ───────────────────── Unauthorized access tests ────────────────────────

    [TestMethod]
    public async Task ApiOne_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient(s_apiOneUrl!);

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        LogEndpointReport("ApiOne", "GET /weatherforecast (no token)", response.StatusCode, "(empty – 401 Unauthorized)");
    }

    [TestMethod]
    public async Task ApiTwo_WithoutToken_ReturnsUnauthorized()
    {
        using var client = CreateUnauthenticatedClient(s_apiTwoUrl!);

        var response = await client.GetAsync("/products");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        LogEndpointReport("ApiTwo", "GET /products (no token)", response.StatusCode, "(empty – 401 Unauthorized)");
    }

    // ───────────────────────── Summary report test ──────────────────────────

    [TestMethod]
    public async Task PrintFullApiReport()
    {
        var report = new StringBuilder();
        report.AppendLine();
        report.AppendLine("╔══════════════════════════════════════════════════════════════════════╗");
        report.AppendLine("║               JwtAuth API Integration Test Report                   ║");
        report.AppendLine("╠══════════════════════════════════════════════════════════════════════╣");
        report.AppendLine();

        // ApiOne endpoints
        report.AppendLine("  ┌─────────────────────────────────────────────────────────────────┐");
        report.AppendLine("  │  API ONE (api-one)                                              │");
        report.AppendLine("  └─────────────────────────────────────────────────────────────────┘");

        using (var client = CreateAuthenticatedClient(s_apiOneUrl!))
        {
            await AppendEndpointResult(report, client, "GET", "/weatherforecast");
            await AppendEndpointResult(report, client, "GET", "/me");
        }

        report.AppendLine();

        // ApiTwo endpoints
        report.AppendLine("  ┌─────────────────────────────────────────────────────────────────┐");
        report.AppendLine("  │  API TWO (api-two)                                              │");
        report.AppendLine("  └─────────────────────────────────────────────────────────────────┘");

        using (var client = CreateAuthenticatedClient(s_apiTwoUrl!))
        {
            await AppendEndpointResult(report, client, "GET", "/products");
            await AppendEndpointResult(report, client, "GET", "/products/1");
            await AppendEndpointResult(report, client, "GET", "/products/9999");
            await AppendEndpointResult(report, client, "GET", "/me");
        }

        report.AppendLine();

        // Unauthorized access
        report.AppendLine("  ┌─────────────────────────────────────────────────────────────────┐");
        report.AppendLine("  │  UNAUTHORIZED ACCESS (no bearer token)                          │");
        report.AppendLine("  └─────────────────────────────────────────────────────────────────┘");

        using (var noAuthClient = CreateUnauthenticatedClient(s_apiOneUrl!))
        {
            await AppendEndpointResult(report, noAuthClient, "GET", "/weatherforecast");
        }

        using (var noAuthClient = CreateUnauthenticatedClient(s_apiTwoUrl!))
        {
            await AppendEndpointResult(report, noAuthClient, "GET", "/products");
        }

        report.AppendLine();
        report.AppendLine("╚══════════════════════════════════════════════════════════════════════╝");

        Console.WriteLine(report.ToString());
    }

    // ─────────────────────────────── Helpers ────────────────────────────────

    private static string ResolveServiceUrl(string serviceName)
    {
        // Aspire injects service endpoint URLs via WithReference in the format:
        //   services__{name}__{scheme}__{index}
        return Environment.GetEnvironmentVariable($"services__{serviceName}__https__0")
            ?? Environment.GetEnvironmentVariable($"services__{serviceName}__http__0")
            ?? throw new InvalidOperationException(
                $"Service URL for '{serviceName}' not found. " +
                $"Ensure the test is run via the JwtAuth AppHost.");
    }

    private static HttpClient CreateAuthenticatedClient(string baseUrl)
    {
        var client = CreateUnauthenticatedClient(baseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s_token);
        return client;
    }

    private static HttpClient CreateUnauthenticatedClient(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
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

        foreach (var line in body.Split('\n'))
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
            return JsonSerializer.Serialize(element, s_jsonOptions);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void LogEndpointReport(string api, string endpoint, HttpStatusCode status, string body)
    {
        var prettyBody = PrettyPrint(body);
        Console.WriteLine($"[{api}] {endpoint} → {(int)status} {status}");
        Console.WriteLine(prettyBody);
        Console.WriteLine();
    }
}
