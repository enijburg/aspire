namespace Aspire.Hosting.Groups;

/// <summary>
/// Options for AggregateStatusFromChildrenService describing which parent resource types to monitor.
/// </summary>
internal sealed class AggregateStatusFromChildrenOptions
{
    public List<Type> ParentResourceTypes { get; } = [];
}