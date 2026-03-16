using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace JwtAuth.ServiceDefaults;

/// <summary>
/// Extension methods for registering shared development JWT authentication in service projects.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// Adds JWT bearer authentication pre-configured for the shared development JWT authority.
    /// The issuer, audience, and signing key are supplied automatically via environment variables
    /// injected by <c>WithSharedDevJwt</c> or <c>AddJwtProject</c> in the AppHost.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddSharedDevJwtAuthentication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddAuthorization();

        return services;
    }
}
