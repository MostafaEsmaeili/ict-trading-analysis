using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>What a stop-trail evaluation produced (plan §2.5.9).</summary>
public enum StopTrailOutcome
{
    /// <summary>No rung fired, or the earned stop is not a clean tighten this bar — the stop stays put.</summary>
    Hold,

    /// <summary>The ladder earned a strictly-tighter resting stop — the caller ratchets to it via MoveStop.</summary>
    MoveStop,
}

/// <summary>Which §2.5.9/§2.5.10 trail rung produced the move (for the audit / stop-moved reason).</summary>
public enum StopTrailTrigger
{
    /// <summary>Price reached 50% of the entry→T1 range — stop tightened to the residual-risk level.</summary>
    T1HalfResidualRisk,

    /// <summary>Price reached 75% of the entry→T1 range — stop tightened to breakeven.</summary>
    T1ThreeQuarterBreakeven,

    /// <summary>Favorable excursion reached the break-even-at-1R threshold (§2.5.10) — stop to breakeven.</summary>
    BreakevenAtOneR,
}

/// <summary>
/// The immutable result of evaluating one open <see cref="PaperTrade"/> against one <see cref="Candle"/> for a stop
/// trail (plan §2.5.9). The policy DECIDES (this value); the orchestrator APPLIES it via <see cref="PaperTrade.MoveStop"/>.
/// It carries the new resting stop LEVEL and the rung that earned it, but no timestamp — the caller stamps the
/// bar-close time, keeping the policy clock-free (§3.0/§4.8). Mirrors <see cref="FillDecision"/>.
/// </summary>
public readonly record struct StopTrailDecision
{
    private StopTrailDecision(StopTrailOutcome outcome, Price? newStop, StopTrailTrigger? trigger)
    {
        Outcome = outcome;
        NewStop = newStop;
        Trigger = trigger;
    }

    /// <summary>The stop holds — no rung fired, or the earned stop was not a clean tighten this bar.</summary>
    public static readonly StopTrailDecision Hold = new(StopTrailOutcome.Hold, null, null);

    /// <summary>Ratchet the stop to <paramref name="newStop"/>, earned by <paramref name="trigger"/>.</summary>
    public static StopTrailDecision Move(Price newStop, StopTrailTrigger trigger)
        => new(StopTrailOutcome.MoveStop, newStop, trigger);

    public StopTrailOutcome Outcome { get; }

    /// <summary>The new resting stop level; null on Hold.</summary>
    public Price? NewStop { get; }

    /// <summary>The rung that earned the move; null on Hold.</summary>
    public StopTrailTrigger? Trigger { get; }

    /// <summary>True when the ladder earned a tighter stop this bar.</summary>
    public bool ShouldMove => Outcome == StopTrailOutcome.MoveStop;
}
