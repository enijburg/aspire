using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting.DevJwt;

/// <summary>
/// Extension methods for adding the shared development JWT authority to a .NET Aspire AppHost.
/// </summary>
public static partial class SharedDevJwtExtensions
{
    /// <summary>
    /// Adds a shared development JWT authority resource to the distributed application.
    /// If no signing key exists in user-secrets under <see cref="SharedDevJwtOptions.SigningKeySecretName"/>,
    /// a new 256-bit key is generated and persisted automatically.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name. Defaults to <c>dev-jwt</c>.</param>
    /// <param name="options">Optional JWT authority configuration. Defaults are applied when <see langword="null"/>.</param>
    /// <returns>A resource builder for the <see cref="DevJwtAuthorityResource"/>.</returns>
    public static IResourceBuilder<DevJwtAuthorityResource> AddSharedDevJwtAuthority(
        this IDistributedApplicationBuilder builder,
        string name = "dev-jwt",
        SharedDevJwtOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        options ??= new SharedDevJwtOptions();

        EnsureSigningKey(builder, options);

        var resource = new DevJwtAuthorityResource(name, options);

        return builder
            .AddResource(resource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "JwtAuthority",
                Properties =
                [
                    new ResourcePropertySnapshot("jwt.issuer", options.Issuer),
                    new ResourcePropertySnapshot("jwt.audience", options.Audience),
                ],
                State = new ResourceStateSnapshot("Ready", KnownResourceStateStyles.Success),
                StartTimeStamp = DateTime.UtcNow,
                IconName = "LockClosed",
                IconVariant = IconVariant.Filled,
            })
            .WithGenerateJwtCommand();
    }

    /// <summary>
    /// Injects the shared JWT bearer authentication environment variables into the resource,
    /// allowing the service to validate tokens issued by the shared development authority.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="resource">The resource builder to configure.</param>
    /// <param name="authority">The shared development JWT authority resource builder.</param>
    /// <returns>The original <paramref name="resource"/> builder for chaining.</returns>
    public static IResourceBuilder<T> WithSharedDevJwt<T>(
        this IResourceBuilder<T> resource,
        IResourceBuilder<DevJwtAuthorityResource> authority)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(authority);

        var options = authority.Resource.Options;

        return resource.WithEnvironment(ctx =>
        {
            if (ctx.ExecutionContext.IsPublishMode)
            {
                return;
            }

            var signingKey = resource.ApplicationBuilder.Configuration[options.SigningKeySecretName] ?? string.Empty;

            ctx.EnvironmentVariables[SharedDevJwtEnvironmentNames.ValidIssuer] = options.Issuer;
            ctx.EnvironmentVariables[SharedDevJwtEnvironmentNames.ValidAudiences0] = options.Audience;
            ctx.EnvironmentVariables[SharedDevJwtEnvironmentNames.SigningKeyIssuer] = options.Issuer;
            ctx.EnvironmentVariables[SharedDevJwtEnvironmentNames.SigningKeyValue] = signingKey;
        });
    }

    /// <summary>
    /// Adds a project resource and wires up the shared JWT bearer environment variables in a single call.
    /// </summary>
    /// <typeparam name="TProject">The project metadata type.</typeparam>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name for the project.</param>
    /// <param name="authority">The shared development JWT authority resource builder.</param>
    /// <returns>A resource builder for the added project.</returns>
    public static IResourceBuilder<ProjectResource> AddJwtProject<TProject>(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<DevJwtAuthorityResource> authority)
        where TProject : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(authority);

        return builder
            .AddProject<TProject>(name)
            .WithSharedDevJwt(authority);
    }

    private static IResourceBuilder<DevJwtAuthorityResource> WithGenerateJwtCommand(
        this IResourceBuilder<DevJwtAuthorityResource> builder)
    {
        return builder.WithCommand(
            name: "generate-jwt",
            displayName: "Generate JWT",
            executeCommand: ctx => ExecuteGenerateJwtAsync(builder, ctx),
            commandOptions: new CommandOptions
            {
                Description = "Generates a signed development JWT and stores it in user-secrets. " +
                              "Customize claims by editing the DevJwt:Tokens:LastClaimsJson secret first.",
                IconName = "Key",
                IconVariant = IconVariant.Filled,
                IsHighlighted = true,
                UpdateState = _ => ResourceCommandState.Enabled,
            });
    }

    private static async Task<ExecuteCommandResult> ExecuteGenerateJwtAsync(
        IResourceBuilder<DevJwtAuthorityResource> resourceBuilder,
        ExecuteCommandContext context)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<DevJwtAuthorityResource>>();

        try
        {
            var options = resourceBuilder.Resource.Options;
            var config = resourceBuilder.ApplicationBuilder.Configuration;

            var signingKey = config[options.SigningKeySecretName];
            if (string.IsNullOrWhiteSpace(signingKey))
            {
                return CommandResults.Failure("Signing key not found. Ensure the AppHost has a UserSecretsId and can write to user-secrets.");
            }

            var (subject, expiry, roles, scopes, customClaims) = ReadClaimsConfig(config, options);

            var token = JwtTokenFactory.CreateToken(
                signingKey: signingKey,
                issuer: options.Issuer,
                audience: options.Audience,
                subject: subject,
                expiry: expiry,
                roles: roles,
                scopes: scopes,
                customClaims: customClaims);

            var claimsJson = JsonSerializer.Serialize(new
            {
                subject,
                expiry = FormatExpiry(expiry),
                roles = roles is not null ? string.Join(",", roles) : string.Empty,
                scopes = scopes is not null ? string.Join(",", scopes) : string.Empty,
                customClaims = customClaims is not null
                    ? JsonSerializer.Serialize(customClaims)
                    : string.Empty,
            });

            WriteUserSecret(options.CurrentTokenSecretName, token);
            WriteUserSecret(options.LastClaimsJsonSecretName, claimsJson);

            LogJwtGenerated(logger, subject, options.Issuer);

            return await Task.FromResult(CommandResults.Success());
        }
        catch (Exception ex)
        {
            LogJwtGenerationFailed(logger, ex);
            return CommandResults.Failure(ex.Message);
        }
    }

    private static (string subject, TimeSpan expiry, IEnumerable<string>? roles, IEnumerable<string>? scopes, IDictionary<string, string>? customClaims)
        ReadClaimsConfig(IConfiguration config, SharedDevJwtOptions options)
    {
        var lastClaimsJson = config[options.LastClaimsJsonSecretName];

        if (!string.IsNullOrWhiteSpace(lastClaimsJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(lastClaimsJson);
                var root = doc.RootElement;

                var subject = root.TryGetProperty("subject", out var subEl) ? subEl.GetString() ?? "dev-user" : "dev-user";
                var expiryStr = root.TryGetProperty("expiry", out var expEl) ? expEl.GetString() : null;
                var rolesStr = root.TryGetProperty("roles", out var rolesEl) ? rolesEl.GetString() : null;
                var scopesStr = root.TryGetProperty("scopes", out var scopesEl) ? scopesEl.GetString() : null;
                var customClaimsStr = root.TryGetProperty("customClaims", out var ccEl) ? ccEl.GetString() : null;

                var roles = ParseCsv(rolesStr);
                var scopes = ParseCsv(scopesStr);
                var customClaims = ParseCustomClaims(customClaimsStr);

                return (subject, ParseExpiry(expiryStr), roles, scopes, customClaims);
            }
            catch (JsonException)
            {
                // Fall through to defaults
            }
        }

        return ("dev-user", TimeSpan.FromDays(1), null, null, null);
    }

    private static TimeSpan ParseExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return TimeSpan.FromDays(1);
        }

        if (expiry.EndsWith('d') && int.TryParse(expiry[..^1], out var days))
        {
            return TimeSpan.FromDays(days);
        }

        if (expiry.EndsWith('h') && int.TryParse(expiry[..^1], out var hours))
        {
            return TimeSpan.FromHours(hours);
        }

        if (expiry.EndsWith('m') && int.TryParse(expiry[..^1], out var minutes))
        {
            return TimeSpan.FromMinutes(minutes);
        }

        return TimeSpan.FromDays(1);
    }

    private static string FormatExpiry(TimeSpan expiry)
    {
        if (expiry.TotalDays >= 1 && expiry.TotalDays % 1 == 0)
        {
            return $"{(int)expiry.TotalDays}d";
        }

        if (expiry.TotalHours >= 1 && expiry.TotalHours % 1 == 0)
        {
            return $"{(int)expiry.TotalHours}h";
        }

        return $"{(int)expiry.TotalMinutes}m";
    }

    private static string[]? ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }

    private static IDictionary<string, string>? ParseCustomClaims(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnsureSigningKey(IDistributedApplicationBuilder builder, SharedDevJwtOptions options)
    {
        var existingKey = builder.Configuration[options.SigningKeySecretName];
        if (!string.IsNullOrWhiteSpace(existingKey))
        {
            return;
        }

        var newKey = JwtTokenFactory.GenerateSigningKey();
        WriteUserSecret(options.SigningKeySecretName, newKey);

        if (builder.Configuration is IConfigurationRoot configRoot)
        {
            configRoot.Reload();
        }
    }

    private static void WriteUserSecret(string key, string value)
    {
        var userSecretsId = GetUserSecretsId();
        if (userSecretsId is null)
        {
            return;
        }

        var secretsPath = PathHelper.GetSecretsPathFromSecretsId(userSecretsId);
        var dir = Path.GetDirectoryName(secretsPath);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        JsonObject secrets;
        if (File.Exists(secretsPath))
        {
            var existing = File.ReadAllText(secretsPath);
            secrets = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            secrets = new JsonObject();
        }

        SetNestedValue(secrets, key.Split(':'), value);

        File.WriteAllText(
            secretsPath,
            secrets.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void SetNestedValue(JsonObject obj, string[] keys, string value)
    {
        if (keys.Length == 1)
        {
            obj[keys[0]] = value;
            return;
        }

        var key = keys[0];
        if (obj[key] is not JsonObject nested)
        {
            nested = new JsonObject();
            obj[key] = nested;
        }

        SetNestedValue(nested, keys[1..], value);
    }

    private static string? GetUserSecretsId()
    {
        var assembly = Assembly.GetEntryAssembly();
        return assembly?.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
    }

    [LoggerMessage(LogLevel.Information, "JWT generated for subject '{Subject}' with issuer '{Issuer}'.")]
    private static partial void LogJwtGenerated(ILogger logger, string subject, string issuer);

    [LoggerMessage(LogLevel.Error, "Failed to generate JWT token.")]
    private static partial void LogJwtGenerationFailed(ILogger logger, Exception exception);
}
