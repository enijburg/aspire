using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Groups;

/// <summary>
/// Builder to configure which parent resources AggregateStatusFromChildrenService should monitor.
/// </summary>
public sealed class AggregateStatusFromChildrenBuilder(IServiceCollection services)
{
    /// <summary>
    /// Restrict aggregation to parents of the given resource type.
    /// Multiple calls can be made to monitor several parent types.
    /// </summary>
    public AggregateStatusFromChildrenBuilder ForParentType<TParent>() where TParent : class, IResourceWithWaitSupport
    {
        services.Configure<AggregateStatusFromChildrenOptions>(options =>
        {
            options.ParentResourceTypes.Add(typeof(TParent));
        });

        return this;
    }
}