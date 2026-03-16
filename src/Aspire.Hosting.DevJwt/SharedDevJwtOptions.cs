namespace Aspire.Hosting.DevJwt;

/// <summary>
/// Configuration options for the shared development JWT authority.
/// </summary>
public sealed class SharedDevJwtOptions
{
    /// <summary>
    /// Gets or sets the JWT issuer.
    /// Defaults to <see cref="SharedDevJwtAuthority.DefaultIssuer"/>.
    /// </summary>
    public string Issuer { get; init; } = SharedDevJwtAuthority.DefaultIssuer;

    /// <summary>
    /// Gets or sets the JWT audience.
    /// Defaults to <see cref="SharedDevJwtAuthority.DefaultAudience"/>.
    /// </summary>
    public string Audience { get; init; } = SharedDevJwtAuthority.DefaultAudience;

    /// <summary>
    /// Gets or sets the user-secrets key under which the HMAC signing key is stored.
    /// Defaults to <see cref="SharedDevJwtAuthority.DefaultSigningKeySecret"/>.
    /// </summary>
    public string SigningKeySecretName { get; init; } = SharedDevJwtAuthority.DefaultSigningKeySecret;

    /// <summary>
    /// Gets or sets the user-secrets key under which the current bearer token is stored.
    /// Defaults to <see cref="SharedDevJwtAuthority.DefaultCurrentTokenSecret"/>.
    /// </summary>
    public string CurrentTokenSecretName { get; init; } = SharedDevJwtAuthority.DefaultCurrentTokenSecret;

    /// <summary>
    /// Gets or sets the user-secrets key under which the last-used claims JSON is stored.
    /// Defaults to <see cref="SharedDevJwtAuthority.DefaultLastClaimsJsonSecret"/>.
    /// </summary>
    public string LastClaimsJsonSecretName { get; init; } = SharedDevJwtAuthority.DefaultLastClaimsJsonSecret;
}
