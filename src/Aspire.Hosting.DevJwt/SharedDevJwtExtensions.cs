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

        var signingKey = builder.Configuration[options.SigningKeySecretName] ?? string.Empty;
        var existingToken = builder.Configuration[options.CurrentTokenSecretName];

        var environmentVariables = new List<EnvironmentVariableSnapshot>
        {
            new("Issuer", options.Issuer, IsFromSpec: true),
            new("Audience", options.Audience, IsFromSpec: true),
            new("SigningKey", signingKey, IsFromSpec: true),
        };

        if (!string.IsNullOrWhiteSpace(existingToken))
        {
            environmentVariables.Add(new EnvironmentVariableSnapshot("BearerToken", existingToken, IsFromSpec: true));
        }

        return builder
            .AddResource(resource)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "JwtAuthority",
                Properties = [],
                EnvironmentVariables = [.. environmentVariables],
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
            ctx.EnvironmentVariables[SharedDevJwtEnvironmentNames.ValidAudiences] = options.Audience;
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

    /// <param name="resource">The resource builder to configure.</param>
    /// <typeparam name="T">The resource type.</typeparam>
    extension<T>(IResourceBuilder<T> resource) where T : IResourceWithEnvironment
    {
        /// <summary>
        /// Mints a development JWT at orchestration time and injects it into the resource as an
        /// environment variable, so the resource can use it directly without needing to mint its
        /// own token. When <paramref name="name"/> is <see langword="null"/> the token is stored
        /// in <see cref="SharedDevJwtEnvironmentNames.BearerToken"/>; otherwise it is stored in
        /// <c>DevJwt__BearerToken__{name}</c>. Call this method multiple times with different
        /// names to inject tokens for different test scenarios.
        /// </summary>
        /// <param name="authority">The shared development JWT authority resource builder.</param>
        /// <param name="name">Optional token name. When specified the environment variable becomes
        /// <c>DevJwt__BearerToken__{name}</c>. Use <see cref="SharedDevJwtEnvironmentNames.GetBearerTokenName"/>
        /// to resolve the variable name at runtime.</param>
        /// <param name="subject">The <c>sub</c> claim. Defaults to <c>dev-user</c>.</param>
        /// <param name="expiry">Token lifetime. Defaults to 30 minutes.</param>
        /// <param name="roles">Optional role claims.</param>
        /// <param name="scopes">Optional scope claims.</param>
        /// <returns>The original <paramref name="resource"/> builder for chaining.</returns>
        public IResourceBuilder<T> WithNewDevJwtToken(IResourceBuilder<DevJwtAuthorityResource> authority,
            string? name = null,
            string subject = "dev-user",
            TimeSpan? expiry = null,
            string[]? roles = null,
            string[]? scopes = null)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(authority);

            var options = authority.Resource.Options;
            var tokenExpiry = expiry ?? TimeSpan.FromMinutes(30);
            var envVarName = SharedDevJwtEnvironmentNames.GetBearerTokenName(name);

            return resource.WithEnvironment(ctx =>
            {
                if (ctx.ExecutionContext.IsPublishMode)
                {
                    return;
                }

                var signingKey = resource.ApplicationBuilder.Configuration[options.SigningKeySecretName];

                if (string.IsNullOrWhiteSpace(signingKey))
                {
                    return;
                }

                var token = JwtTokenFactory.CreateToken(
                    signingKey: signingKey,
                    issuer: options.Issuer,
                    audience: options.Audience,
                    subject: subject,
                    expiry: tokenExpiry,
                    roles: roles,
                    scopes: scopes);

                ctx.EnvironmentVariables[envVarName] = token;
            });
        }

        /// <summary>
        /// Reads the most recently generated JWT from user-secrets
        /// (<see cref="SharedDevJwtOptions.CurrentTokenSecretName"/>) and injects it into the
        /// resource as an environment variable. This allows the test project to use a token
        /// that was generated interactively via the Aspire dashboard's "Generate JWT" command.
        /// </summary>
        /// <param name="authority">The shared development JWT authority resource builder.</param>
        /// <param name="name">Optional token name. When specified the environment variable becomes
        /// <c>DevJwt__BearerToken__{name}</c>. Use <see cref="SharedDevJwtEnvironmentNames.GetBearerTokenName"/>
        /// to resolve the variable name at runtime.</param>
        /// <returns>The original <paramref name="resource"/> builder for chaining.</returns>
        public IResourceBuilder<T> WithCurrentDevJwtToken(IResourceBuilder<DevJwtAuthorityResource> authority,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(authority);

            var options = authority.Resource.Options;
            var envVarName = SharedDevJwtEnvironmentNames.GetBearerTokenName(name);

            return resource.WithEnvironment(ctx =>
            {
                if (ctx.ExecutionContext.IsPublishMode)
                {
                    return;
                }

                var token = resource.ApplicationBuilder.Configuration[options.CurrentTokenSecretName];

                if (!string.IsNullOrWhiteSpace(token))
                {
                    ctx.EnvironmentVariables[envVarName] = token;
                }
            });
        }

        /// <summary>
        /// Reads a saved token profile's claims from user-secrets and mints a fresh JWT at
        /// orchestration time. The profile must have been created previously via the Aspire
        /// dashboard's "Generate JWT" command. The minted token is injected as an environment
        /// variable so the resource can use it directly.
        /// </summary>
        /// <param name="authority">The shared development JWT authority resource builder.</param>
        /// <param name="profile">The name of the saved profile in user-secrets
        /// (under <c>DevJwt:Profiles:{profile}:*</c>).</param>
        /// <param name="name">Optional token name. When specified the environment variable becomes
        /// <c>DevJwt__BearerToken__{name}</c>. Defaults to the <paramref name="profile"/> value.</param>
        /// <returns>The original <paramref name="resource"/> builder for chaining.</returns>
        public IResourceBuilder<T> WithDevJwtProfileToken(IResourceBuilder<DevJwtAuthorityResource> authority,
            string profile,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(authority);
            ArgumentException.ThrowIfNullOrWhiteSpace(profile);

            var options = authority.Resource.Options;
            var envVarName = SharedDevJwtEnvironmentNames.GetBearerTokenName(name ?? profile);

            return resource.WithEnvironment(ctx =>
            {
                if (ctx.ExecutionContext.IsPublishMode)
                {
                    return;
                }

                var signingKey = resource.ApplicationBuilder.Configuration[options.SigningKeySecretName];

                if (string.IsNullOrWhiteSpace(signingKey))
                {
                    return;
                }

                var config = resource.ApplicationBuilder.Configuration;
                var prefix = $"{SharedDevJwtAuthority.ProfilesSection}:{profile}:";

                var subject = config[$"{prefix}Subject"];

                if (string.IsNullOrWhiteSpace(subject))
                {
                    return;
                }

                var expiry = ParseExpiry(config[$"{prefix}Expiry"]);
                var roles = ParseCsv(config[$"{prefix}Roles"]);
                var scopes = ParseCsv(config[$"{prefix}Scopes"]);
                var customClaims = ParseCustomClaims(config[$"{prefix}CustomClaimsJson"]);

                var token = JwtTokenFactory.CreateToken(
                    signingKey: signingKey,
                    issuer: options.Issuer,
                    audience: options.Audience,
                    subject: subject,
                    expiry: expiry,
                    roles: roles,
                    scopes: scopes,
                    customClaims: customClaims);

                ctx.EnvironmentVariables[envVarName] = token;
            });
        }
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

    private const string NewProfileSentinel = "__new__";

    private static async Task<ExecuteCommandResult> ExecuteGenerateJwtAsync(
        IResourceBuilder<DevJwtAuthorityResource> resourceBuilder,
        ExecuteCommandContext context)
    {
        var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
        var loggerService = context.ServiceProvider.GetRequiredService<ResourceLoggerService>();
        var logger = loggerService.GetLogger(resourceBuilder.Resource);

        var config = resourceBuilder.ApplicationBuilder.Configuration;

        // --- Step 1: Profile selection ---
        var profileNames = config.GetSection(SharedDevJwtAuthority.ProfilesSection)
            .GetChildren()
            .Select(s => s.Key)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? selectedProfile = null;

        if (profileNames.Count > 0)
        {
            var pickerInputs = new List<InteractionInput>
            {
                new()
                {
                    Name = "Profile",
                    InputType = InputType.Choice,
                    Required = true,
                    Options =
                    [
                        .. profileNames.Select(n => new KeyValuePair<string, string>(n, n)),
                        new(NewProfileSentinel, "(Create new)"),
                    ],
                },
            };

            var pickerResult = await interactionService.PromptInputsAsync(
                title: "Select Token Profile",
                message: "Choose a saved profile or create a new one:",
                inputs: pickerInputs,
                cancellationToken: context.CancellationToken);

            if (pickerResult.Canceled)
            {
                return CommandResults.Failure("JWT generation was canceled by the user.");
            }

            var picked = pickerResult.Data[0].Value;
            if (picked is not null && picked != NewProfileSentinel)
            {
                selectedProfile = picked;
            }
        }

        // --- Step 2: Load saved values for selected profile ---
        string? savedSubject = null, savedExpiry = null, savedRoles = null, savedScopes = null, savedCustomClaims = null;
        if (selectedProfile is not null)
        {
            var prefix = $"{SharedDevJwtAuthority.ProfilesSection}:{selectedProfile}:";
            savedSubject = config[$"{prefix}Subject"];
            savedExpiry = config[$"{prefix}Expiry"];
            savedRoles = config[$"{prefix}Roles"];
            savedScopes = config[$"{prefix}Scopes"];
            savedCustomClaims = config[$"{prefix}CustomClaimsJson"];
        }

        // --- Step 3: JWT generation form ---
        var inputs = new List<InteractionInput>
        {
            new()
            {
                Name = "Profile Name",
                InputType = InputType.Text,
                Required = true,
                Placeholder = "default",
                Value = selectedProfile,
            },
            new()
            {
                Name = "Subject",
                InputType = InputType.Text,
                Required = true,
                Placeholder = "dev-user",
                Value = savedSubject,
            },
            new()
            {
                Name = "Expiry",
                InputType = InputType.Choice,
                Required = true,
                Value = savedExpiry,
                Options =
                [
                    new("15m", "15 Minutes"),
                    new("30m", "30 Minutes"),
                    new("1h", "1 Hour"),
                    new("4h", "4 Hours"),
                    new("8h", "8 Hours"),
                    new("1d", "1 Day"),
                    new("7d", "7 Days"),
                    new("30d", "30 Days"),
                    new("90d", "90 Days"),
                    new("365d", "1 Year"),
                ],
            },
            new()
            {
                Name = "Roles",
                InputType = InputType.Text,
                Required = false,
                Placeholder = "admin,reader",
                Value = savedRoles,
            },
            new()
            {
                Name = "Scopes",
                InputType = InputType.Text,
                Required = false,
                Placeholder = "api:read,api:write",
                Value = savedScopes,
            },
            new()
            {
                Name = "Custom Claims JSON",
                InputType = InputType.Text,
                Required = false,
                Placeholder = "{\"tenant\":\"acme\"}",
                Value = savedCustomClaims,
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

            var profileName = result.Data[0].Value ?? "default";
            var subject = result.Data[1].Value ?? "dev-user";
            var expiryStr = result.Data[2].Value ?? "1d";
            var rolesStr = result.Data[3].Value;
            var scopesStr = result.Data[4].Value;
            var customClaimsStr = result.Data[5].Value;

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

            var notificationService = context.ServiceProvider.GetRequiredService<ResourceNotificationService>();
            await notificationService.PublishUpdateAsync(resourceBuilder.Resource, previous =>
                previous with
                {
                    EnvironmentVariables = [
                        .. previous.EnvironmentVariables.Where(e => e.Name != "BearerToken"),
                        new("BearerToken", token, IsFromSpec: true),
                    ],
                });

            PersistProfile(resourceBuilder.ApplicationBuilder, profileName, subject, expiryStr, rolesStr, scopesStr, customClaimsStr);

            logger.LogInformation(
                """
                JWT generated successfully.
                Profile: {ProfileName}
                Subject: {Subject}
                Expiry: {Expiry}
                Roles: {Roles}
                Scopes: {Scopes}
                Token stored in user-secret '{SecretName}'.
                """,
                profileName,
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

    private static void PersistProfile(
        IDistributedApplicationBuilder builder,
        string profileName,
        string subject,
        string expiry,
        string? roles,
        string? scopes,
        string? customClaimsJson)
    {
        var prefix = $"{SharedDevJwtAuthority.ProfilesSection}:{profileName}:";
        WriteSecret(builder, $"{prefix}Subject", subject);
        WriteSecret(builder, $"{prefix}Expiry", expiry);
        WriteSecret(builder, $"{prefix}Roles", roles ?? string.Empty);
        WriteSecret(builder, $"{prefix}Scopes", scopes ?? string.Empty);
        WriteSecret(builder, $"{prefix}CustomClaimsJson", customClaimsJson ?? string.Empty);
    }

    [LoggerMessage(LogLevel.Error, "Failed to generate JWT token.")]
    private static partial void LogJwtGenerationFailed(ILogger logger, Exception exception);
}

