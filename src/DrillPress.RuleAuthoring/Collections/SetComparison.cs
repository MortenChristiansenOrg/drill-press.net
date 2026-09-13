namespace DrillPress.Collections;

/// <summary>Compares inventories using caller-selected semantic keys and equality; duplicates do not affect membership.</summary>
public sealed class SetComparison<T>
{
    /// <summary>Compares an expected inventory with an actual inventory, retaining first-occurrence order.</summary>
    public SetComparison(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        IEqualityComparer<T>? comparer = null
    )
    {
        var wanted = expected.ToHashSet(comparer);
        var found = actual.ToHashSet(comparer);
        Missing = wanted.Where(item => !found.Contains(item)).ToArray();
        Unexpected = found.Where(item => !wanted.Contains(item)).ToArray();
    }

    /// <summary>Expected items absent from the actual inventory.</summary>
    public IReadOnlyList<T> Missing { get; }

    /// <summary>Actual items absent from the expected inventory.</summary>
    public IReadOnlyList<T> Unexpected { get; }

    /// <summary>Whether both inventories contain precisely the same keys.</summary>
    public bool AreEqual => Missing.Count == 0 && Unexpected.Count == 0;
}
