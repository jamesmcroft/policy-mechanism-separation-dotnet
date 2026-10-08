namespace Sample.Planning;

/// <summary>A candidate selected by a caller, with an opaque identifier and capacity cost.</summary>
/// <param name="Id">A nonblank identifier without leading or trailing whitespace, unique within a plan.</param>
/// <param name="Units">Positive capacity cost, no greater than the batch capacity.</param>
/// <param name="Priority">Nonnegative priority; higher priorities run first.</param>
public sealed record WorkItem(string Id, int Units, int Priority);

/// <summary>A read-only batch produced by the builder.</summary>
/// <param name="Items">Items in execution order.</param>
/// <param name="TotalUnits">The sum of the items' capacity costs.</param>
public sealed record WorkBatch(IReadOnlyList<WorkItem> Items, int TotalUnits);

/// <summary>Validates candidates and creates deterministic, capacity-bounded work batches.</summary>
public static class WorkPlanBuilder
{
    /// <summary>
    /// Orders items by descending priority and then ordinal identifier, and packs consecutive
    /// items into batches without exceeding either limit. Does not optimize bin utilization.
    /// </summary>
    /// <param name="items">An eligible scope. Identifiers are opaque and case-sensitive.</param>
    /// <param name="capacity">Positive maximum capacity units per batch.</param>
    /// <param name="maxItemsPerBatch">Positive maximum number of items per batch.</param>
    /// <returns>Read-only snapshots; an empty scope produces no batches.</returns>
    /// <exception cref="ArgumentNullException">The scope is null.</exception>
    /// <exception cref="ArgumentException">An item is null or violates the common contract.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Either batch limit is nonpositive.</exception>
    public static IReadOnlyList<WorkBatch> Build(
        IEnumerable<WorkItem> items, int capacity, int maxItemsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItemsPerBatch);

        var candidates = new List<WorkItem>();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item is null)
                throw new ArgumentException("A work item cannot be null.", nameof(items));
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id != item.Id.Trim())
                throw new ArgumentException("Identifiers must be nonblank and unpadded.", nameof(items));
            if (item.Units <= 0 || item.Units > capacity)
                throw new ArgumentException(
                    $"Item '{item.Id}' must cost between 1 and {capacity} units.", nameof(items));
            if (item.Priority < 0)
                throw new ArgumentException($"Item '{item.Id}' has a negative priority.", nameof(items));
            if (!identifiers.Add(item.Id))
                throw new ArgumentException($"Duplicate identifier '{item.Id}'.", nameof(items));

            candidates.Add(item);
        }

        var batches = new List<WorkBatch>();
        var batchItems = new List<WorkItem>();
        var totalUnits = 0;

        foreach (var item in candidates
            .OrderByDescending(i => i.Priority)
            .ThenBy(i => i.Id, StringComparer.Ordinal))
        {
            if (batchItems.Count > 0
                && (batchItems.Count == maxItemsPerBatch || item.Units > capacity - totalUnits))
                FinishBatch();

            batchItems.Add(item);
            totalUnits += item.Units;
        }

        if (batchItems.Count > 0)
            FinishBatch();

        return Array.AsReadOnly(batches.ToArray());

        void FinishBatch()
        {
            batches.Add(new(Array.AsReadOnly(batchItems.ToArray()), totalUnits));
            batchItems.Clear();
            totalUnits = 0;
        }
    }
}
