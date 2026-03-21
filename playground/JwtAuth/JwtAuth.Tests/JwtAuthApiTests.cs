using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Aspire.Hosting.DevJwt;
using Aspire.Hosting.Testing;
using Aspire.Testing.MSTest;
using JwtAuth.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace JwtAuth.Tests;

[TestClass]
public sealed class JwtAuthApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string _bothToken = null!;
    private static string _apiOneToken = null!;
    private static string _apiTwoToken = null!;
    private static string _signingKey = null!;
    private static string _issuer = null!;
    private static string _audience = null!;
    private static AspireIntegrationTestHost _testHost = null!;
    private static ILogger _logger = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // In standalone mode, the signing key is captured from the AppHost's configuration
        // after CreateAsync runs the AppHost code (which calls EnsureSigningKey).
        string? standaloneSigningKey = null;

        // Build the Aspire test host. Mode detection (AppHost vs standalone) happens
        // inside BuildAsync by checking the OTEL_EXPORTER_OTLP_ENDPOINT env var.
        _testHost = await AspireIntegrationTestHost.CreateBuilder()
            .WithResource("api-one")
            .WithResource("api-two")
            .WithActivitySource(TracedTestMethodAttribute.TestActivitySource.Name)
            .WithServiceDefaults(builder => builder.AddServiceDefaults())
            .WithStandaloneBuilder(async () =>
            {
                // Load the AppHost assembly and use CreateAsync with its entry point type.
                // CreateAsync invokes the AppHost's top-level statements, which configure
                // the JWT authority, api-one, api-two, and tests resources on the builder.
                var appHostAssembly = Assembly.Load("JwtAuth.AppHost");
                var entryPointType = appHostAssembly.EntryPoint?.DeclaringType
                    ?? throw new InvalidOperationException(
                        "Could not find the entry point type in the JwtAuth.AppHost assembly.");

                return await DistributedApplicationTestingBuilder.CreateAsync(entryPointType, []);
            })
            .ConfigureStandaloneBuilder(builder =>
            {
                // Capture the signing key that was generated/loaded by the AppHost code
                // (via EnsureSigningKey in AddSharedDevJwtAuthority).
                standaloneSigningKey = builder.Configuration[SharedDevJwtAuthority.DefaultSigningKeySecret];
            })
            .BuildAsync();

        await _testHost.StartAsync();

        if (_testHost.IsStandalone)
        {
            // Standalone mode: mint tokens locally using the same signing key
            // the AppHost injected into the API projects.
            _signingKey = standaloneSigningKey
                ?? throw new InvalidOperationException(
                    "Failed to read signing key from AppHost configuration.");
            _issuer = SharedDevJwtAuthority.DefaultIssuer;
            _audience = SharedDevJwtAuthority.DefaultAudience;

            _bothToken = JwtTokenFactory.CreateToken(
                signingKey: _signingKey, issuer: _issuer, audience: _audience,
                subject: "both-user", expiry: TimeSpan.FromMinutes(30),
                roles: ["api-one", "api-two"]);

            _apiOneToken = JwtTokenFactory.CreateToken(
                signingKey: _signingKey, issuer: _issuer, audience: _audience,
                subject: "api-one-user", expiry: TimeSpan.FromMinutes(30),
                roles: ["api-one"]);

            _apiTwoToken = JwtTokenFactory.CreateToken(
                signingKey: _signingKey, issuer: _issuer, audience: _audience,
                subject: "api-two-user", expiry: TimeSpan.FromMinutes(30),
                roles: ["api-two"]);
        }
        else
        {
            // AppHost mode: read pre-minted bearer tokens injected by the AppHost
            // via WithNewDevJwtToken.
            _bothToken = Environment.GetEnvironmentVariable(
                    SharedDevJwtEnvironmentNames.GetBearerTokenName("both-user"))
                ?? throw new InvalidOperationException(
                    $"Bearer token not found. Ensure the test is run via the JwtAuth AppHost " +
                    $"(expected env var: {SharedDevJwtEnvironmentNames.GetBearerTokenName("both-user")}).");

            _apiOneToken = Environment.GetEnvironmentVariable(
                    SharedDevJwtEnvironmentNames.GetBearerTokenName("api-one-user"))
                ?? throw new InvalidOperationException(
                    $"Bearer token not found. Ensure the test is run via the JwtAuth AppHost " +
                    $"(expected env var: {SharedDevJwtEnvironmentNames.GetBearerTokenName("api-one-user")}).");

            _apiTwoToken = Environment.GetEnvironmentVariable(
                    SharedDevJwtEnvironmentNames.GetBearerTokenName("api-two-user"))
                ?? throw new InvalidOperationException(
                    $"Bearer token not found. Ensure the test is run via the JwtAuth AppHost " +
                    $"(expected env var: {SharedDevJwtEnvironmentNames.GetBearerTokenName("api-two-user")}).");

            // Read JWT authority config for crafting invalid tokens in the validation tests.
            _signingKey = Environment.GetEnvironmentVariable(
                    SharedDevJwtEnvironmentNames.SigningKeyValue)
                ?? throw new InvalidOperationException(
                    $"Signing key not found. Ensure WithSharedDevJwt is configured on the tests project " +
                    $"(expected env var: {SharedDevJwtEnvironmentNames.SigningKeyValue}).");

            _issuer = Environment.GetEnvironmentVariable(
                    SharedDevJwtEnvironmentNames.ValidIssuer)
                ?? throw new InvalidOperationException(
                    $"Issuer not found (expected env var: {SharedDevJwtEnvironmentNames.ValidIssuer}).");

            _audience = Environment.GetEnvironmentVariable(
                    SharedDevJwtEnvironmentNames.ValidAudiences)
                ?? throw new InvalidOperationException(
                    $"Audience not found (expected env var: {SharedDevJwtEnvironmentNames.ValidAudiences}).");
        }

        _logger = _testHost.Services.GetRequiredService<ILoggerFactory>().CreateLogger<JwtAuthApiTests>();
        _logger.LogInformation("Test host started in {Mode} mode.",
            _testHost.IsStandalone ? "standalone" : "AppHost");

        // Flush all startup logs to TestContext and switch to per-test Console.Out
        // logging. This must be the last thing in ClassInitialize so that all startup
        // output (DA lifecycle, resource stdout, Lifetime, and test-host ready) is
        // captured and none of it leaks into the first test method.
        var startupLog = _testHost.FlushStartupLog();
        if (startupLog.Length > 0)
        {
            context.WriteLine(startupLog);
        }
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _testHost.DisposeAsync();
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

    // ------------- Invalid token tests (JWT validation is strict) -------------

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiOne_WithExpiredToken_ReturnsUnauthorized()
    {
        // Create a token issued 3 hours ago with a 1-hour TTL — it expired 2 hours ago,
        // well outside the default 5-minute clock-skew tolerance.
        var expiredToken = CreateTokenWithCustomTimestamps(
            signingKey: _signingKey,
            issuer: _issuer,
            audience: _audience,
            issuedAt: DateTime.UtcNow.AddHours(-3),
            expiry: TimeSpan.FromHours(1),
            roles: ["api-one"]);

        using var client = CreateAuthenticatedClient("api-one", expiredToken);
        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast (expired token)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiOne_WithWrongIssuer_ReturnsUnauthorized()
    {
        // Valid key and audience but wrong issuer — the API rejects it.
        var wrongIssuerToken = JwtTokenFactory.CreateToken(
            signingKey: _signingKey,
            issuer: "https://wrong-issuer.example.com",
            audience: _audience,
            subject: "test-user",
            expiry: TimeSpan.FromMinutes(30),
            roles: ["api-one"]);

        using var client = CreateAuthenticatedClient("api-one", wrongIssuerToken);
        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast (wrong issuer)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiOne_WithWrongAudience_ReturnsUnauthorized()
    {
        // Valid key and issuer but wrong audience — the API rejects it.
        var wrongAudienceToken = JwtTokenFactory.CreateToken(
            signingKey: _signingKey,
            issuer: _issuer,
            audience: "wrong-audience",
            subject: "test-user",
            expiry: TimeSpan.FromMinutes(30),
            roles: ["api-one"]);

        using var client = CreateAuthenticatedClient("api-one", wrongAudienceToken);
        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast (wrong audience)", response.StatusCode, "(empty - 401 Unauthorized)");
    }

    [TracedTestMethod(HttpStatusCode.Unauthorized)]
    public async Task ApiOne_WithWrongSigningKey_ReturnsUnauthorized()
    {
        // Token is structurally valid (correct issuer/audience) but signed with a different key.
        var wrongSigningKey = JwtTokenFactory.GenerateSigningKey();
        var wrongKeyToken = JwtTokenFactory.CreateToken(
            signingKey: wrongSigningKey,
            issuer: _issuer,
            audience: _audience,
            subject: "test-user",
            expiry: TimeSpan.FromMinutes(30),
            roles: ["api-one"]);

        using var client = CreateAuthenticatedClient("api-one", wrongKeyToken);
        var response = await client.GetAsync("/weatherforecast");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);

        TestActivityScope.ReportStatusCode(response.StatusCode);
        LogEndpointReport("ApiOne", "GET /weatherforecast (wrong signing key)", response.StatusCode, "(empty - 401 Unauthorized)");
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
        return _testHost.CreateClient(serviceName);
    }

    /// <summary>
    /// Creates a JWT with explicit <paramref name="issuedAt"/> and <paramref name="expiry"/>
    /// timestamps, allowing tests to produce tokens that appear expired at runtime.
    /// </summary>
    private static string CreateTokenWithCustomTimestamps(
        string signingKey,
        string issuer,
        string audience,
        DateTime issuedAt,
        TimeSpan expiry,
        string[]? roles = null)
    {
        var keyBytes = Convert.FromBase64String(signingKey);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "test-user"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (roles is not null)
        {
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: issuedAt,
            expires: issuedAt.Add(expiry),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
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
