using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Groups;

/// <summary>
/// Extension methods for working with resources that are  groups for other resources.
/// </summary>
public static class ResourceExtensions
{
    /// <summary>
    /// Adds a logical group resource to the distributed application and initializes
    /// it with a default snapshot suitable for UI display.
    /// </summary>
    /// <param name="builder">
    /// The distributed application builder used to define resources.
    /// </param>
    /// <param name="name">
    /// The name of the group resource. This name is also propagated to child
    /// resources as the <c>resource.parentName</c> property.
    /// </param>
    /// <returns>
    /// An <see cref="IResourceBuilder{TResource}"/> for the created
    /// <see cref="GroupResource"/>, allowing further configuration.
    /// </returns>
    /// <remarks>
    /// When the group resource becomes ready, any resource annotated as having this
    /// group as its <see cref="KnownRelationshipTypes.Parent"/> will be updated with
    /// a <c>resource.parentName</c> property containing the group name. This enables
    /// grouping and filtering in dashboards and other tooling.
    /// </remarks>
    public static IResourceBuilder<GroupResource> AddGroup(this IDistributedApplicationBuilder builder, [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var groupInitialSnapshot = new CustomResourceSnapshot
        {
            ResourceType = "Group",
            Properties = [],
            State = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Success),
            StartTimeStamp = DateTime.UtcNow,
            IconName = "FolderOpen", // https://storybooks.fluentui.dev/react/?path=/docs/icons-catalog--docs but leave out the Regular/Filled suffix
            IconVariant = IconVariant.Filled,

        };

        var resource = new GroupResource(name);
        var resourceBuilder = builder.AddResource(resource)
            .WithInitialState(groupInitialSnapshot);

        builder.Eventing.Subscribe<ResourceReadyEvent>(resource, async (evt, _) =>
        {
            var rns = evt.Services.GetRequiredService<ResourceNotificationService>();

            foreach (var builderResource in builder.Resources)
            {
                if (builderResource.TryGetAnnotationsOfType<ResourceRelationshipAnnotation>(out var annotations)
                    && annotations.Any(a => a.Resource == resource && a.Type == KnownRelationshipTypes.Parent))
                {
                    await rns.PublishUpdateAsync(builderResource, previous =>
                        previous with
                        {
                            Properties = [.. previous.Properties,
                                new ResourcePropertySnapshot("resource.parentName", resource.Name)],
                        });
                }
            }
        });

        return resourceBuilder;
    }


    /// <summary>
    /// Represents a logical group resource that can act as a parent for other
    /// resources in the distributed application.
    /// </summary>
    /// <param name="name">
    /// The name of the group resource.
    /// </param>
    public sealed class GroupResource(string name) : Resource(name), IResourceWithWaitSupport
    {
    }
}