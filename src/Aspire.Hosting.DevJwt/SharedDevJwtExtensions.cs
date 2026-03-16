using System.Text.Json;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
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
                Description = "Interactively prompts for claims and generates a signed development JWT, " +
                              "storing the result in user-secrets.",
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
        var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
        var loggerService = context.ServiceProvider.GetRequiredService<ResourceLoggerService>();
        var logger = loggerService.GetLogger(resourceBuilder.Resource);

        var inputs = new List<InteractionInput>
        {
            new()
            {
                Name = "Subject",
                InputType = InputType.Text,
                Required = true,
                Placeholder = "dev-user",
            },
            new()
            {
                Name = "Expiry",
                InputType = InputType.Choice,
                Required = true,
                Options =
                [
                    new("1d", "1 Day"),
                    new("8h", "8 Hours"),
                    new("7d", "7 Days"),
                    new("30d", "30 Days"),
                ],
            },
            new()
            {
                Name = "Roles",
                InputType = InputType.Text,
                Required = false,
                Placeholder = "admin,reader",
            },
            new()
            {
                Name = "Scopes",
                InputType = InputType.Text,
                Required = false,
                Placeholder = "api:read,api:write",
            },
            new()
            {
                Name = "Custom Claims JSON",
                InputType = InputType.Text,
                Required = false,
                Placeholder = "{\"tenant\":\"acme\"}",
            },
        };

        var result = await interactionService.PromptInputsAsync(
            title: "Generate Development JWT",
            message: "Configure the claims for your development bearer token:",
            inputs: inputs,
            cancellationToken: context.CancellationToken);

        if (result.Canceled)
        {
            return CommandResults.Failure("JWT generation was canceled by the user.");
        }

        try
        {
            var options = resourceBuilder.Resource.Options;
            var signingKey = resourceBuilder.ApplicationBuilder.Configuration[options.SigningKeySecretName];

            if (string.IsNullOrWhiteSpace(signingKey))
            {
                return CommandResults.Failure("Signing key not found. Ensure the AppHost has a UserSecretsId.");
            }

            var subject = result.Data[0].Value ?? "dev-user";
            var expiryStr = result.Data[1].Value ?? "1d";
            var rolesStr = result.Data[2].Value;
            var scopesStr = result.Data[3].Value;
            var customClaimsStr = result.Data[4].Value;

            var expiry = ParseExpiry(expiryStr);
            var roles = ParseCsv(rolesStr);
            var scopes = ParseCsv(scopesStr);
            var customClaims = ParseCustomClaims(customClaimsStr);

            var token = JwtTokenFactory.CreateToken(
                signingKey: signingKey,
                issuer: options.Issuer,
                audience: options.Audience,
                subject: subject,
                expiry: expiry,
                roles: roles,
                scopes: scopes,
                customClaims: customClaims);

            WriteSecret(resourceBuilder.ApplicationBuilder, options.CurrentTokenSecretName, token);

            logger.LogInformation(
                """
                JWT generated successfully.
                Subject: {Subject}
                Expiry: {Expiry}
                Roles: {Roles}
                Scopes: {Scopes}
                Token stored in user-secret '{SecretName}'.
                """,
                subject,
                expiryStr,
                rolesStr ?? "(none)",
                scopesStr ?? "(none)",
                options.CurrentTokenSecretName);

            return CommandResults.Success();
        }
        catch (Exception ex)
        {
            LogJwtGenerationFailed(logger, ex);
            return CommandResults.Failure(ex.Message);
        }
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
        if (builder.Configuration is IConfigurationManager configManager)
        {
            builder.UserSecretsManager.GetOrSetSecret(
                configManager,
                options.SigningKeySecretName,
                JwtTokenFactory.GenerateSigningKey);
        }
    }

    private static void WriteSecret(IDistributedApplicationBuilder builder, string key, string value)
    {
        builder.UserSecretsManager.TrySetSecret(key, value);
    }

    [LoggerMessage(LogLevel.Error, "Failed to generate JWT token.")]
    private static partial void LogJwtGenerationFailed(ILogger logger, Exception exception);
}

