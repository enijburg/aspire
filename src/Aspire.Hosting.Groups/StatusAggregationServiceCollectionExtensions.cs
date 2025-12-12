using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aspire.Hosting.Groups;

/// <summary>
/// Extension methods for registering status aggregation services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class StatusAggregationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the `AggregateStatusFromChildrenService` and returns a builder for configuring
    /// which parent resource types should have their status aggregated from their children.
    /// </summary>
    /// <param name="services">
    /// The dependency injection service collection to add the status aggregation services to.
    /// </param>
    /// <returns>
    /// An <see cref="AggregateStatusFromChildrenBuilder"/> that can be used to configure
    /// which parent resource types participate in status aggregation. By default, it is
    /// configured to aggregate status for `ResourceExtensions.GroupResource` parents.
    /// </returns>
    public static AggregateStatusFromChildrenBuilder AddAggregateParentStatusFromChildren(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            Microsoft.Extensions.Hosting.IHostedService,
            AggregateStatusFromChildrenService>());

        // By default, only monitor ResourceExtensions.GroupResource as parents.
        return new AggregateStatusFromChildrenBuilder(services)
            .ForParentType<ResourceExtensions.GroupResource>();
    }
}