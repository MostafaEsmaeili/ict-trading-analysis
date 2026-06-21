namespace IctTrader.Domain.Trading;

/// <summary>
/// The immutable, APPLY-ORDERED set of <see cref="ExitAction"/>s the <see cref="IExitManager"/> decided for one
/// candle (plan §2.5.9). The caller applies them to the <see cref="PaperTrade"/> in order — a protective close alone,
/// or a scale-out followed by a stop ratchet (both stamped at the same bar-close time, so the aggregate's monotonic
/// timeline guard is satisfied). An empty plan (<see cref="NoOp"/>) is a no-op bar.
/// </summary>
public readonly record struct ExitPlan
{
    public ExitPlan(IReadOnlyList<ExitAction> actions)
    {
        Actions = actions;
    }

    /// <summary>A bar that decided nothing — the trade is unchanged.</summary>
    public static readonly ExitPlan NoOp = new(Array.Empty<ExitAction>());

    /// <summary>The decided actions, in the order the caller must apply them.</summary>
    public IReadOnlyList<ExitAction> Actions { get; }

    /// <summary>True when the bar decided at least one action.</summary>
    public bool HasActions => Actions is { Count: > 0 };
}
