using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.DevJwt;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JwtAuth.Tests;

[TestClass]
public sealed class JwtAuthApiTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private static DistributedApplication? s_app;
    private static string? s_token;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        var appHostBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.JwtAuth_AppHost>();

        s_app = await appHostBuilder.BuildAsync();
        var resourceNotificationService = s_app.Services.GetRequiredService<ResourceNotificationService>();

        await s_app.StartAsync();

        // Wait for both API resources to be running
        await resourceNotificationService.WaitForResourceAsync("api-one", KnownResourceStates.Running);
        await resourceNotificationService.WaitForResourceAsync("api-two", KnownResourceStates.Running);

        // Retrieve the signing key from the AppHost configuration and generate a JWT
        var configuration = s_app.Services.GetRequiredService<IConfiguration>();
        var signingKey = configuration[SharedDevJwtAuthority.DefaultSigningKeySecret]
            ?? throw new InvalidOperationException("Signing key not found in AppHost configuration.");

        s_token = JwtTokenFactory.CreateToken(
            signingKey: signingKey,
            issuer: SharedDevJwtAuthority.DefaultIssuer,
            audience: SharedDevJwtAuthority.DefaultAudience,
            subject: "test-user",
            expiry: TimeSpan.FromMinutes(30),
            roles: ["admin", "reader"]);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (s_app is not null)
        {
            await s_app.StopAsync();
            await s_app.DisposeAsync();
        }
    }

    // ───────────────────────────── ApiOne tests ─────────────────────────────

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

    // ───────────────────────────── ApiTwo tests ─────────────────────────────

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

        LogEndpointReport("ApiTwo", "GET /products/9999", response.StatusCode, "(empty – 404 Not Found)");
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

    // ───────────────────── Unauthorized access tests ────────────────────────

    [TestMethod]
    public async Task ApiOne_WithoutToken_ReturnsUnauthorized()
    {
        using var client = s_app!.CreateHttpClient("api-one");

        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        LogEndpointReport("ApiOne", "GET /weatherforecast (no token)", response.StatusCode, "(empty – 401 Unauthorized)");
    }

    [TestMethod]
    public async Task ApiTwo_WithoutToken_ReturnsUnauthorized()
    {
        using var client = s_app!.CreateHttpClient("api-two");

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

        using (var client = CreateAuthenticatedClient("api-one"))
        {
            await AppendEndpointResult(report, client, "GET", "/weatherforecast");
            await AppendEndpointResult(report, client, "GET", "/me");
        }

        report.AppendLine();

        // ApiTwo endpoints
        report.AppendLine("  ┌─────────────────────────────────────────────────────────────────┐");
        report.AppendLine("  │  API TWO (api-two)                                              │");
        report.AppendLine("  └─────────────────────────────────────────────────────────────────┘");

        using (var client = CreateAuthenticatedClient("api-two"))
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

        using (var noAuthClient = s_app!.CreateHttpClient("api-one"))
        {
            await AppendEndpointResult(report, noAuthClient, "GET", "/weatherforecast");
        }

        using (var noAuthClient = s_app!.CreateHttpClient("api-two"))
        {
            await AppendEndpointResult(report, noAuthClient, "GET", "/products");
        }

        report.AppendLine();
        report.AppendLine("╚══════════════════════════════════════════════════════════════════════╝");

        Console.WriteLine(report.ToString());
    }

    // ─────────────────────────────── Helpers ────────────────────────────────

    private static HttpClient CreateAuthenticatedClient(string resourceName)
    {
        var client = s_app!.CreateHttpClient(resourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s_token);
        return client;
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
