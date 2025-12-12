using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aspire.Hosting.Groups;

/// <summary>
/// External (AppHost) service: aggregates parent status snapshots from child status snapshots,
/// based on Parent relationships (IResourceWithParent or ResourceRelationshipAnnotation type "Parent").
/// </summary>
internal sealed class AggregateStatusFromChildrenService(
    DistributedApplicationModel model,
    ResourceNotificationService notifications,
    IOptions<AggregateStatusFromChildrenOptions> options,
    ILogger<AggregateStatusFromChildrenService> logger)
    : BackgroundService
{
    private readonly Dictionary<IResource, List<IResource>> _parentToChildren = new();
    private readonly Dictionary<IResource, List<IResource>> _childToParents = new();
    private readonly HashSet<Type> _monitoredParentTypes = [.. options.Value.ParentResourceTypes];

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        BuildLookups();
        return WatchAndAggregateAsync(stoppingToken);
    }

    private void BuildLookups()
    {
        foreach (var resource in model.Resources)
        {
            var parent = resource switch
            {
                IResourceWithParent rwp => rwp.Parent,
                _ => resource.Annotations
                    .OfType<ResourceRelationshipAnnotation>()
                    .LastOrDefault(a => string.Equals(a.Type, KnownRelationshipTypes.Parent, StringComparison.Ordinal))?
                    .Resource
            };

            if (parent is null)
            {
                continue;
            }

            // Only aggregate statuses into configured parent resource types
            if (!_monitoredParentTypes.Contains(parent.GetType()))
            {
                continue;
            }

            if (!_parentToChildren.TryGetValue(parent, out var children))
            {
                children = [];
                _parentToChildren[parent] = children;
            }

            children.Add(resource);

            if (!_childToParents.TryGetValue(resource, out var parents))
            {
                parents = [];
                _childToParents[resource] = parents;
            }

            parents.Add(parent);
        }

        logger.LogDebug("AggregateStatusFromChildrenService initialized with {ParentCount} parents.", _parentToChildren.Count);
    }

    private async Task WatchAndAggregateAsync(CancellationToken cancellationToken)
    {
        var latestByName = new Dictionary<string, CustomResourceSnapshot>(StringComparer.OrdinalIgnoreCase);

        await foreach (var evt in notifications.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            latestByName[evt.Resource.Name] = evt.Snapshot;

            if (!_childToParents.TryGetValue(evt.Resource, out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (!_parentToChildren.TryGetValue(parent, out var children))
                {
                    continue;
                }

                var childSnapshots = new List<CustomResourceSnapshot>(children.Count);

                foreach (var child in children)
                {
                    if (latestByName.TryGetValue(child.Name, out var snap))
                    {
                        childSnapshots.Add(snap);
                    }
                    else if (notifications.TryGetCurrentState(child.Name, out var current))
                    {
                        // Fallback if we haven't observed via WatchAsync yet.
                        childSnapshots.Add(current.Snapshot);
                        latestByName[child.Name] = current.Snapshot;
                    }
                }

                var aggregateState = ComputeAggregateState(childSnapshots);

                await notifications.PublishUpdateAsync(parent, previous => previous with { State = aggregateState }).ConfigureAwait(false);
            }
        }
    }

    private static ResourceStateSnapshot? ComputeAggregateState(IReadOnlyList<CustomResourceSnapshot> childSnapshots)
    {
        if (childSnapshots.Count == 0)
        {
            return null;
        }

        var states = childSnapshots
            .Select(s => s.State?.Text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        if (states.Length == 0)
        {
            return null;
        }

        // Optionally ignore Hidden children when computing status.
        // If *all* children are Hidden, parent becomes Hidden.
#pragma warning disable CS0618 // Type or member is obsolete
        var nonHiddenStates = states.Where(s => s != KnownResourceStates.Hidden).ToArray();
#pragma warning restore CS0618 // Type or member is obsolete

        // ---- 1) Failure dominates ----
        if (nonHiddenStates.Any(s =>
                s == KnownResourceStates.FailedToStart ||
                s == KnownResourceStates.RuntimeUnhealthy ||
                s == KnownResourceStates.Exited))
        {
            return new ResourceStateSnapshot(
                KnownResourceStates.FailedToStart,
                KnownResourceStateStyles.Error);
        }

        // ---- 2) Stopping dominates ----
        if (nonHiddenStates.Any(s => s == KnownResourceStates.Stopping))
        {
            return new ResourceStateSnapshot(
                KnownResourceStates.Stopping,
                KnownResourceStateStyles.Info);
        }

        // ---- 3) In-progress dominates steady ----
        if (nonHiddenStates.Any(s =>
                s == KnownResourceStates.Starting ||
                s == KnownResourceStates.Waiting ||
                s == KnownResourceStates.NotStarted))
        {
            if (nonHiddenStates.All(s => s == KnownResourceStates.NotStarted))
            {
                return new ResourceStateSnapshot(
                    KnownResourceStates.NotStarted,
                    KnownResourceStateStyles.Info);
            }

            if (nonHiddenStates.All(s => s == KnownResourceStates.Waiting))
            {
                return new ResourceStateSnapshot(
                    KnownResourceStates.Waiting,
                    KnownResourceStateStyles.Info);
            }

            return new ResourceStateSnapshot(
                KnownResourceStates.Starting,
                KnownResourceStateStyles.Info);
        }

        // ---- 4) Steady states ----
        var anyRunning = nonHiddenStates.Any(s => s == KnownResourceStates.Running);
        var allRunning = nonHiddenStates.All(s => s == KnownResourceStates.Running);
        var allFinished = nonHiddenStates.All(s => s == KnownResourceStates.Finished);
        var allActive = nonHiddenStates.All(s => s == KnownResourceStates.Active);

        if (anyRunning)
        {
            return allRunning
                ? new ResourceStateSnapshot(
                    KnownResourceStates.Running,
                    KnownResourceStateStyles.Success)
                : new ResourceStateSnapshot(
                    "PartiallyRunning",
                    KnownResourceStateStyles.Success);
        }

        if (allFinished)
        {
            return new ResourceStateSnapshot(
                KnownResourceStates.Finished,
                KnownResourceStateStyles.Success);
        }

        if (allActive)
        {
            return new ResourceStateSnapshot(
                KnownResourceStates.Active,
                KnownResourceStateStyles.Success);
        }

        return new ResourceStateSnapshot("Degraded", KnownResourceStateStyles.Warn);
    }

}