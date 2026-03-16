namespace Aspire.Hosting.DevJwt;

/// <summary>
/// Default configuration values for the shared development JWT authority.
/// </summary>
public static class SharedDevJwtAuthority
{
    /// <summary>Default JWT issuer: <c>https://dev-jwt.local</c></summary>
    public const string DefaultIssuer = "https://dev-jwt.local";

    /// <summary>Default JWT audience: <c>microservices-dev</c></summary>
    public const string DefaultAudience = "microservices-dev";

    /// <summary>User-secrets key for the HMAC signing key: <c>DevJwt:SigningKey</c></summary>
    public const string DefaultSigningKeySecret = "DevJwt:SigningKey";

    /// <summary>User-secrets key for the current bearer token: <c>DevJwt:Tokens:Current</c></summary>
    public const string DefaultCurrentTokenSecret = "DevJwt:Tokens:Current";
}
