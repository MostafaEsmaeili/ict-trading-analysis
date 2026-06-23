namespace IctTrader.Domain.Trading;

/// <summary>
/// The immutable, APPLY-ORDERED set of <see cref="EntryAction"/>s the <see cref="IEntryManager"/> decided for one
/// candle (plan §2.5.1 step 7). The caller applies them to the <see cref="ArmedEntry"/> / produced <see cref="PaperTrade"/>
/// in order — an <see cref="EntryActionKind.Open"/> alone (a clean fill), or an open followed by a same-bar
/// <see cref="EntryActionKind.Close"/> (the −1R straddle), both stamped at the same bar-close time so the aggregate's
/// monotonic timeline guard is satisfied. An empty plan (<see cref="NoOp"/>) is a no-fill bar — the limit stays resting.
/// </summary>
public readonly record struct EntryPlan
{
    public EntryPlan(IReadOnlyList<EntryAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        Actions = [.. actions]; // copy to an array so the plan is genuinely immutable after construction
    }

    /// <summary>A bar that decided nothing — the limit stays resting, unchanged.</summary>
    public static readonly EntryPlan NoOp = new(Array.Empty<EntryAction>());

    /// <summary>The decided actions, in the order the caller must apply them.</summary>
    public IReadOnlyList<EntryAction> Actions { get; }

    /// <summary>True when the bar decided at least one action.</summary>
    public bool HasActions => Actions is { Count: > 0 };
}
