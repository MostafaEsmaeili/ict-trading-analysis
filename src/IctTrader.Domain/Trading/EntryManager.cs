using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure §2.5.1-step-7 entry orchestrator — the DECIDE half that drives a resting <see cref="ArmedEntry"/> for one
/// candle. It delegates the limit touch to <see cref="IEntryFillEvaluator"/>; a no-touch bar is <see cref="EntryPlan.NoOp"/>
/// (the limit keeps resting). On a fill it emits an <see cref="EntryActionKind.Open"/> at the bar-close, then resolves
/// the <b>same-bar entry-then-stop straddle</b> by re-feeding the SAME candle to the exit <see cref="IFillEvaluator"/> —
/// the ONE worst-case (StopFirst) authority — so a fast bar that fills the limit and then runs to the stop books a real
/// −1R (an apply-ordered <see cref="EntryActionKind.Open"/> then <see cref="EntryActionKind.Close"/>, both stamped at the
/// bar-close), never a phantom same-bar win. A same-bar runner is deliberately NOT credited here (the steady-state exit
/// pass books it on its own bar); the entry path only owns the protective −1R. DECIDE-only: the caller applies the plan
/// (<see cref="PaperTradeFactory.OpenArmed"/> for the open, <see cref="PaperTrade.Close"/> for the straddle); the
/// no-chase cancellation precedence (killzone-end &gt; max-wait) runs BEFORE the fill: an unfilled limit whose entry
/// window has passed is cancelled (the caller releases its reservation so the cap self-heals), never chased.
/// </summary>
public sealed class EntryManager : IEntryManager
{
    private readonly IEntryFillEvaluator _entryFillEvaluator;
    private readonly IFillEvaluator _fillEvaluator;
    private readonly IExecutionCostModel _costModel;
    private readonly KillzoneClock _killzoneClock;
    private readonly KillzoneEntryOptions _killzoneOptions;
    private readonly EntryManagementOptions _options;

    public EntryManager(
        IEntryFillEvaluator entryFillEvaluator,
        IFillEvaluator fillEvaluator,
        IExecutionCostModel costModel,
        KillzoneClock killzoneClock,
        KillzoneEntryOptions killzoneOptions,
        EntryManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(entryFillEvaluator);
        ArgumentNullException.ThrowIfNull(fillEvaluator);
        ArgumentNullException.ThrowIfNull(costModel);
        ArgumentNullException.ThrowIfNull(killzoneClock);
        ArgumentNullException.ThrowIfNull(killzoneOptions);
        ArgumentNullException.ThrowIfNull(options);
        _entryFillEvaluator = entryFillEvaluator;
        _fillEvaluator = fillEvaluator;
        _costModel = costModel;
        _killzoneClock = killzoneClock;
        _killzoneOptions = killzoneOptions;
        _options = options;
    }

    public EntryPlan Decide(ArmedEntry armedEntry, Candle candle, EntryContext context)
    {
        ArgumentNullException.ThrowIfNull(armedEntry);
        Guard.Against(
            armedEntry.Status != ArmedEntryStatus.Armed, "Only an armed (resting) entry can be evaluated for a fill.");
        Guard.Against(candle.Symbol != armedEntry.Symbol, "The candle must be for the armed entry's symbol.");

        // The fill bar must close at or after the arm — this also fail-fasts a default(EntryContext) (its BarCloseUtc
        // is MinValue), which bypasses the ctor's UTC guard, before any action is stamped.
        Guard.Against(
            context.BarCloseUtc < armedEntry.ArmedAtUtc, "The bar-close time cannot precede the arm time.");

        var at = context.BarCloseUtc;

        // No-chase cancellation FIRST (§2.5.1 "don't chase"): an unfilled limit whose entry window has passed is
        // cancelled before any fill is considered. The caller applies Cancel as ArmedEntry.Cancel + PaperAccount.Release.
        var cancel = ResolveCancellation(armedEntry, candle, at);
        if (cancel is not null)
        {
            return new EntryPlan([EntryAction.Cancel(cancel.Value, armedEntry.Setup.Plan.Entry, at)]);
        }

        var fill = _entryFillEvaluator.Evaluate(armedEntry.Setup, candle);
        if (!fill.IsFilled)
        {
            return EntryPlan.NoOp;
        }

        var open = EntryAction.Open(fill.FillPrice!.Value, at);

        // Same-bar straddle (§2.5.8 worst-case): build the would-be trade and re-feed the SAME candle to the exit
        // FillEvaluator — the ONE StopFirst authority. Only a same-bar STOP closes here (−1R); a same-bar runner is
        // left for the steady-state exit pass, so the entry path can never grant a free same-bar win. The would-be
        // trade is a transient computation vehicle (its open event is cleared); the caller opens the REAL trade.
        var wouldBe = BuildWouldBeTrade(armedEntry, at);
        var sameBar = _fillEvaluator.Evaluate(wouldBe, candle);
        if (sameBar.Outcome != FillOutcome.StopHit)
        {
            return new EntryPlan([open]);
        }

        // The straddle is a same-bar full round trip, so it books the costed entry crossing + exit (unlike a clean
        // open, whose entry spread rides the deferred exit-leg cost line). RemainingSize == the full size here.
        var costs = _costModel.Compute(wouldBe);
        var close = EntryAction.Close(sameBar.ExitPrice!.Value, sameBar.CloseReason!.Value, costs, at);
        return new EntryPlan([open, close]);
    }

    /// <summary>
    /// The no-chase cancellation precedence (§2.5.1 "don't chase"): <b>killzone-end</b> (the bar is no longer a
    /// tradeable killzone entry — window over, lunch, or the index cutoff) outranks the <b>max-wait</b> backstop, which
    /// outranks the <b>wrong-order NIX</b> (FVG-SEM-2b, Ep3 L376-413: a retrace that reaches the FARTHER stacked gap
    /// before the limit fills). The active hunt-set is the same §4.6 set the entry detector uses, so the arm and entry
    /// windows can't drift apart. The killzone is classified from the candle's OPEN time — the SAME axis
    /// <see cref="Detection.MarketContext"/> and the <c>KillzoneEntryDetector</c> classify on (so a bar straddling a
    /// boundary is treated identically everywhere); max-wait is measured to the bar close (<paramref name="at"/>). The
    /// NIX reads the candle High/Low (inclusive, mirroring the fill touch), so a same-bar entry+farther touch lets the
    /// NIX win (no-trade beats trade). No-overnight is NOT a separate rung: no FX active killzone spans 00:00 NY, so a
    /// midnight cross already trips killzone-end. Returns null when the limit may rest.
    /// </summary>
    private EntryCancelReason? ResolveCancellation(ArmedEntry armedEntry, Candle candle, DateTimeOffset at)
    {
        if (!_killzoneClock.IsActiveEntry(candle.OpenTimeUtc, armedEntry.InstrumentClass, _killzoneOptions.ActiveKillzones))
        {
            return EntryCancelReason.KillzoneEnded;
        }

        if (at - armedEntry.ArmedAtUtc >= TimeSpan.FromMinutes(_options.MaxWaitMinutes))
        {
            return EntryCancelReason.MaxWaitElapsed;
        }

        // FVG-SEM-2b §3: the wrong-order NIX (pre-fill only — ResolveCancellation runs while Status == Armed; a
        // post-fill farther-gap touch is the §1 widened stop's job, not a nix). Inclusive boundary.
        if (armedEntry.StackedFartherBound is { } farther
            && (armedEntry.Direction == Direction.Bullish ? candle.Low <= farther : candle.High >= farther))
        {
            return EntryCancelReason.StackedFartherGapHitFirst;
        }

        return null;
    }

    private static PaperTrade BuildWouldBeTrade(ArmedEntry armedEntry, DateTimeOffset openedAtUtc)
    {
        var setup = armedEntry.Setup;
        var trade = new PaperTrade(
            armedEntry.Id, armedEntry.AccountId, setup.Symbol, setup.Style, setup.Timeframe, setup.Plan,
            armedEntry.Size, armedEntry.PipSize, armedEntry.ValuePerPip, openedAtUtc);
        trade.ClearDomainEvents(); // transient — the REAL trade is opened by the caller via OpenArmed
        return trade;
    }
}
