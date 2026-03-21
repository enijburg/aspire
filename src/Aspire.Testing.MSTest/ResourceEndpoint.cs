namespace Aspire.Testing.MSTest;

/// <summary>
/// Describes a resource endpoint that <see cref="AspireIntegrationTestHost"/> should register
/// an <see cref="System.Net.Http.HttpClient"/> for.
/// </summary>
/// <param name="Name">The Aspire resource name (e.g. <c>"api-one"</c>).</param>
/// <param name="SchemePreference">
/// The scheme prefix used for service-discovery URIs when running under an AppHost
/// (e.g. <c>"https+http"</c>). In standalone mode the preferred endpoint is derived
/// from this value (<c>"https"</c> when the preference starts with <c>"https"</c>,
/// <c>"http"</c> otherwise).
/// </param>
/// <param name="EndpointName">
/// Optional explicit endpoint name to resolve in standalone mode. When <see langword="null"/>
/// the endpoint name is derived from <paramref name="SchemePreference"/>.
/// </param>
public sealed record ResourceEndpoint(
    string Name,
    string SchemePreference = "https+http",
    string? EndpointName = null);
