using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting.DevJwt;

/// <summary>
/// Represents the shared development JWT authority resource in the distributed application.
/// </summary>
/// <param name="name">The name of the resource.</param>
/// <param name="options">The JWT authority configuration options.</param>
public sealed class DevJwtAuthorityResource(string name, SharedDevJwtOptions options)
    : Resource(name)
{
    /// <summary>Gets the JWT authority configuration options.</summary>
    public SharedDevJwtOptions Options { get; } = options;
}
