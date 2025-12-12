using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aspire.Hosting.Groups;

/// <summary>
/// External (AppHost) service: aggregates parent status snapshots from child status snapshots,
/// based on Parent relationships (IResourceWithParent or ResourceRelationshipAnnotation type "Parent").
/// </summary>
internal sealed partial class AggregateStatusFromChildrenService(
    DistributedApplicationModel model,
    ResourceNotificationService notifications,
    IOptions<AggregateStatusFromChildrenOptions> options,
    ILogger<AggregateStatusFromChildrenService> logger)
    : BackgroundService
{
    private readonly Dictionary<string, List<IResource>> _parentNameToChildren = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<IResource>> _childNameToParents = new(StringComparer.OrdinalIgnoreCase);
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

            if (!_parentNameToChildren.TryGetValue(parent.Name, out var children))
            {
                children = [];
                _parentNameToChildren[parent.Name] = children;
            }

            children.Add(resource);

            if (!_childNameToParents.TryGetValue(resource.Name, out var parents))
            {
                parents = [];
                _childNameToParents[resource.Name] = parents;
            }

            parents.Add(parent);
        }

        LogAggregatestatusfromchildrenserviceInitializedWithParentCountParents(logger, _parentNameToChildren.Count);
    }

    private async Task WatchAndAggregateAsync(CancellationToken cancellationToken)
    {
        var latestByName = new Dictionary<string, CustomResourceSnapshot>(StringComparer.OrdinalIgnoreCase);

        await foreach (var evt in notifications.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            latestByName[evt.Resource.Name] = evt.Snapshot;

            if (!_childNameToParents.TryGetValue(evt.Resource.Name, out var parents))
            {
                continue;
            }

            foreach (var parent in parents)
            {
                if (!_parentNameToChildren.TryGetValue(parent.Name, out var children))
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

                var (snapShot, exitCode) = ComputeAggregateState(childSnapshots);

                await notifications.PublishUpdateAsync(parent, previous => previous with { State = snapShot, ExitCode = exitCode }).ConfigureAwait(false);
            }
        }
    }

    private static (ResourceStateSnapshot? snapShot, int exitCode) ComputeAggregateState(IReadOnlyList<CustomResourceSnapshot> childSnapshots)
    {
        var exitCode = childSnapshots.Max(s => s.ExitCode) ?? 0;

        var states = childSnapshots
            .Select(s => s.State?.Text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        if (states.Length == 0)
        {
            return (null, exitCode);
        }

        var facts = (
            HasFailure: Has(KnownResourceStates.FailedToStart)
                        || Has(KnownResourceStates.RuntimeUnhealthy)
                        || Has(KnownResourceStates.Exited),

            HasStopping: Has(KnownResourceStates.Stopping),

            HasRunning: Has(KnownResourceStates.Running),

            HasInProgress: Has(KnownResourceStates.Starting)
                           || Has(KnownResourceStates.Waiting)
                           || Has(KnownResourceStates.NotStarted),

            AllNotStarted: All(KnownResourceStates.NotStarted),
            AllWaiting: All(KnownResourceStates.Waiting),
            AllFinished: All(KnownResourceStates.Finished),
            AllActive: All(KnownResourceStates.Active)
        );

        var snapshot = facts switch
        {
            // 1) Failure dominates
            { HasFailure: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.FailedToStart,
                    KnownResourceStateStyles.Error),

            // 2) Stopping dominates
            { HasStopping: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.Stopping,
                    KnownResourceStateStyles.Info),

            // 3) Running dominates everything below
            { HasRunning: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.Running,
                    KnownResourceStateStyles.Success),

            // 4) In-progress
            { HasInProgress: true, AllNotStarted: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.NotStarted,
                    KnownResourceStateStyles.Info),

            { HasInProgress: true, AllWaiting: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.Waiting,
                    KnownResourceStateStyles.Info),

            { HasInProgress: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.Starting,
                    KnownResourceStateStyles.Info),

            // 5) Terminal steady states
            { AllFinished: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.Finished,
                    exitCode > 0
                        ? KnownResourceStateStyles.Error
                        : KnownResourceStateStyles.Success),

            { AllActive: true } =>
                new ResourceStateSnapshot(
                    KnownResourceStates.Active,
                    exitCode > 0
                        ? KnownResourceStateStyles.Error
                        : KnownResourceStateStyles.Success),

            // 6) Mixed / unexpected
            _ =>
                new ResourceStateSnapshot(
                    "Degraded",
                    KnownResourceStateStyles.Warn)
        };

        return (snapshot, exitCode);

        bool All(string state) =>
            states.All(s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));

        bool Has(string state) =>
            states.Any(s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));
    }

    [LoggerMessage(LogLevel.Debug, "AggregateStatusFromChildrenService initialized with {ParentCount} parents.")]
    static partial void LogAggregatestatusfromchildrenserviceInitializedWithParentCountParents(ILogger<AggregateStatusFromChildrenService> logger, int parentCount);
}