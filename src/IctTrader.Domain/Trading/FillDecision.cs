using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>What an intrabar fill resolution produced (plan §5.2).</summary>
public enum FillOutcome
{
    /// <summary>The bar touched neither the stop nor the runner target — the trade stays open.</summary>
    NoFill,

    /// <summary>The bar reached the stop — the trade closes at the stop level for −1R.</summary>
    StopHit,

    /// <summary>The bar reached the runner (T2) target — the trade closes at the plan reward-to-risk.</summary>
    RunnerHit,
}

/// <summary>
/// The immutable result of evaluating one open <see cref="PaperTrade"/> against one <see cref="Candle"/> (plan
/// §5.2). The evaluator DECIDES (this value); the aggregate APPLIES it via <see cref="PaperTrade.Close"/>. The
/// decision carries the exit price — always the resting LEVEL (the stop / runner price), never the bar extreme,
/// so a stop-out books exactly −1R and a runner books the plan reward-to-risk — and the matching close reason, but
/// no timestamp: the caller stamps the bar's close time, keeping the evaluator clock-free (§3.0/§4.8).
/// </summary>
public readonly record struct FillDecision
{
    private FillDecision(FillOutcome outcome, Price? exitPrice, TradeCloseReason? closeReason)
    {
        Outcome = outcome;
        ExitPrice = exitPrice;
        CloseReason = closeReason;
    }

    /// <summary>A bar that filled nothing — the trade remains open.</summary>
    public static readonly FillDecision NoFill = new(FillOutcome.NoFill, null, null);

    /// <summary>A stop-out at the stop level.</summary>
    public static FillDecision Stop(Price stopLevel)
        => new(FillOutcome.StopHit, stopLevel, TradeCloseReason.StopHit);

    /// <summary>A target fill at the runner level.</summary>
    public static FillDecision Runner(Price runnerLevel)
        => new(FillOutcome.RunnerHit, runnerLevel, TradeCloseReason.TargetHit);

    public FillOutcome Outcome { get; }

    /// <summary>The resting fill level (stop or runner price); null when nothing filled.</summary>
    public Price? ExitPrice { get; }

    /// <summary>The matching close reason; null when nothing filled.</summary>
    public TradeCloseReason? CloseReason { get; }

    /// <summary>True when the bar closed the trade (stop or runner).</summary>
    public bool IsFill => Outcome != FillOutcome.NoFill;
}
