namespace Aspire.Hosting.DevJwt;

/// <summary>
/// Constants for the environment variable names injected into services by the shared development JWT authority.
/// </summary>
public static class SharedDevJwtEnvironmentNames
{
    /// <summary>
    /// <c>Authentication__Schemes__Bearer__ValidIssuer</c>
    /// </summary>
    public const string ValidIssuer = "Authentication__Schemes__Bearer__ValidIssuer";

    /// <summary>
    /// <c>Authentication__Schemes__Bearer__ValidAudiences__0</c>
    /// </summary>
    public const string ValidAudiences = "Authentication__Schemes__Bearer__ValidAudiences__0";

    /// <summary>
    /// <c>Authentication__Schemes__Bearer__SigningKeys__0__Issuer</c>
    /// </summary>
    public const string SigningKeyIssuer = "Authentication__Schemes__Bearer__SigningKeys__0__Issuer";

    /// <summary>
    /// <c>Authentication__Schemes__Bearer__SigningKeys__0__Value</c>
    /// </summary>
    public const string SigningKeyValue = "Authentication__Schemes__Bearer__SigningKeys__0__Value";
}
