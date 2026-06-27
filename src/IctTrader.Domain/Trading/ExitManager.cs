using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure §2.5.9 / §3.4 exit orchestrator. For one candle it runs the deterministic precedence
/// <b>protective-fill → time-exit → scale → trail</b>: (1) if the bar reached the stop or the runner, the whole
/// position closes at that LEVEL and nothing else happens this bar; (2) otherwise, if the trade has reached its
/// style's max hold OR a no-overnight style has crossed the NY-day boundary (§2.5.1 step 9), the whole remaining
/// position closes at the bar CLOSE as a <see cref="TradeCloseReason.TimeExit"/> and nothing else happens; (3) on a
/// surviving bar, take the T1 partial once (sized <c>PartialFraction × Size</c> at the partial level via
/// <see cref="IExecutionCostModel.ComputeExitLeg"/>); (4) and ratchet the stop if the trail earned a tighter level.
/// It DECIDES an apply-ordered <see cref="ExitPlan"/> (scale before stop-move, every action stamped at the bar-close
/// time so the aggregate's monotonic timeline holds); the caller APPLIES it. Pure: the only "now" is the bar-close
/// time on the <see cref="ExitContext"/>, and the no-overnight NY date is taken via the DST-aware <see cref="NyClock"/>
/// (never an ambient zone, §4.8).
/// </summary>
public sealed class ExitManager : IExitManager
{
    private readonly IFillEvaluator _fillEvaluator;
    private readonly IStopTrailPolicy _stopTrailPolicy;
    private readonly IExecutionCostModel _costModel;
    private readonly ExitManagementOptions _options;
    private readonly NyClock _nyClock;
    private readonly TradeStyleOptions _styleOptions;
    private readonly ContractSpec _contractSpec;

    public ExitManager(
        IFillEvaluator fillEvaluator,
        IStopTrailPolicy stopTrailPolicy,
        IExecutionCostModel costModel,
        ExitManagementOptions options,
        NyClock nyClock,
        TradeStyleOptions styleOptions,
        ContractSpec contractSpec)
    {
        ArgumentNullException.ThrowIfNull(fillEvaluator);
        ArgumentNullException.ThrowIfNull(stopTrailPolicy);
        ArgumentNullException.ThrowIfNull(costModel);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(nyClock);
        ArgumentNullException.ThrowIfNull(styleOptions);
        ArgumentNullException.ThrowIfNull(contractSpec);
        _fillEvaluator = fillEvaluator;
        _stopTrailPolicy = stopTrailPolicy;
        _costModel = costModel;
        _options = options;
        _nyClock = nyClock;
        _styleOptions = styleOptions;
        _contractSpec = contractSpec;
    }

    public ExitPlan Decide(PaperTrade trade, Candle candle, ExitContext context)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(trade.Status != TradeStatus.Open, "Only an open paper trade can be managed for an exit.");
        Guard.Against(candle.Symbol != trade.Symbol, "The candle must be for the trade's symbol.");

        // The bar must close at or after the trade opened — this also fail-fasts a default(ExitContext) (its
        // BarCloseUtc is MinValue), which bypasses the ctor's UTC guard, before any action is stamped.
        Guard.Against(
            context.BarCloseUtc < trade.OpenedAtUtc, "The bar-close time cannot precede the trade open.");

        var at = context.BarCloseUtc;

        // 1. Protective fill first (§2.5.8 worst-case). A stop/runner that hit closes the WHOLE remaining position
        //    at the LEVEL; nothing else happens on a bar that already exits. A REAL fill always outranks a time-exit:
        //    if the stop/runner was touched this bar, that exit happened in the market regardless of the clock, so it
        //    must book the fill (−1R / the plan RR), never a flattering bar-close time-exit.
        var fill = _fillEvaluator.Evaluate(trade, candle);
        if (fill.IsFill)
        {
            // §5.4: book net of spread on BOTH legs. The open path books no cost, so the entry crossing is folded
            // into this terminal close. A T1 scale (below) stays exit-only, so (scale exit) + (final exit + entry)
            // sums to exactly one full entry + one full exit == ExecutionCostModel.Compute (leg-sum invariant).
            var closeCosts = _costModel.ComputeExitLeg(trade, trade.RemainingSize) + _costModel.ComputeEntryLeg(trade);
            return new ExitPlan([ExitAction.Close(fill.ExitPrice!.Value, fill.CloseReason!.Value, closeCosts, at)]);
        }

        // 2. Time-exit (§2.5.1 step 9: "max hold 90–120 min; no overnight"). A force-flatten is a TIME event, not a
        //    price level reached, so it closes the WHOLE remaining position at the bar CLOSE — and it overrides a
        //    same-bar scale/trail (there is no remaining position to manage once you are flattening). It sits BELOW
        //    the protective fill (above) and ABOVE discretionary management (below).
        if (TimeExitFires(trade, context))
        {
            // Like the protective fill, the force-flatten is a whole-position terminal close, so it carries the
            // once-per-trade entry crossing (§5.4 both legs). The leg-sum invariant holds whether or not a prior
            // T1 scale (exit-only) was taken on an earlier bar.
            var timeExitCosts =
                _costModel.ComputeExitLeg(trade, trade.RemainingSize) + _costModel.ComputeEntryLeg(trade);
            return new ExitPlan(
                [ExitAction.Close(new Price(candle.Close), TradeCloseReason.TimeExit, timeExitCosts, at)]);
        }

        var actions = new List<ExitAction>();

        // 3. T1 scale-out — once, when a surviving bar reaches the partial target. Sized PartialFraction × the ORIGINAL
        //    size (the §2.5.9 "take half of the position"), FLOORED to the contract lot step (mirroring PositionSizer),
        //    booked at the partial LEVEL. Apply-safety relies on the validated PartialFraction ∈ (0,1)
        //    (ExitManagementOptions.Validate + ValidateOnStart) so the leg stays strictly below RemainingSize.
        if (trade.Lifecycle == TradeLifecycle.Open && ReachedPartial(trade, candle)
            && TryFloorPartialLeg(trade, out var legSize))
        {
            var legCosts = _costModel.ComputeExitLeg(trade, legSize);
            actions.Add(ExitAction.ScaleOut(
                trade.Plan.Targets.Partial, legSize, legCosts, TradeCloseReason.TargetHit, at));
        }

        // 4. Trail — ratchet the stop if the policy earned a strictly-tighter level. Decided against the SAME pre-bar
        //    state as the scale; listed AFTER the scale so the apply order keeps the timeline monotonic.
        var trail = _stopTrailPolicy.Evaluate(trade, candle);
        if (trail.ShouldMove)
        {
            actions.Add(ExitAction.MoveStop(trail.NewStop!.Value, at));
        }

        return actions.Count == 0 ? ExitPlan.NoOp : new ExitPlan(actions);
    }

    /// <summary>
    /// A time-exit fires when the trade has reached its style's max hold OR a no-overnight style has crossed its day
    /// boundary (§2.5.1 step 9). The two compose with OR — both force the identical bar-close flatten. Max-hold is a
    /// pure UTC elapsed test; only the no-overnight boundary needs the DST-aware NY date. Max-hold is measured at BAR
    /// CLOSE, so the realized flatten honors the cap to within one bar at the entry timeframe — faithful to Ep21's
    /// discretionary "two hours maximum" (not a tick-precise rule); intrabar precision is the deferred §5.2 path.
    /// </summary>
    private bool TimeExitFires(PaperTrade trade, ExitContext context)
    {
        var style = _styleOptions.For(trade.Style);

        var maxHoldReached =
            context.BarCloseUtc - trade.OpenedAtUtc >= TimeSpan.FromMinutes(style.MaxHoldMinutes);

        var noOvernightCrossed =
            !style.AllowOvernight && CrossedNoOvernightBoundary(trade, context);

        return maxHoldReached || noOvernightCrossed;
    }

    /// <summary>
    /// Whether a no-overnight trade has crossed its configured day boundary. For <see cref="NoOvernightBoundary.NyMidnight"/>
    /// (the ICT financial day start, §2.1) a bar whose NY calendar date differs from the entry's has crossed midnight —
    /// compared with <c>!=</c> (not <c>&gt;</c>) so a mis-stamped earlier bar fails safe-CLOSED rather than silently
    /// holding. The 17:00 ET boundary is not yet wired (validation rejects it; this throws if it is ever reached).
    /// For the default §2.5 Intraday model the 120-min max-hold dominates (killzone-gated entries flatten well before
    /// midnight), so this branch is DEFENSIVE — load-bearing only for operator-configured styles with longer caps or
    /// non-killzone entries.
    /// </summary>
    private bool CrossedNoOvernightBoundary(PaperTrade trade, ExitContext context) =>
        _options.NoOvernightBoundary switch
        {
            NoOvernightBoundary.NyMidnight =>
                _nyClock.NewYorkDate(trade.OpenedAtUtc) != _nyClock.NewYorkDate(context.BarCloseUtc),
            _ => throw new NotSupportedException(
                $"NoOvernightBoundary '{_options.NoOvernightBoundary}' is not yet wired; only NyMidnight is supported."),
        };

    private static bool ReachedPartial(PaperTrade trade, Candle candle)
        => trade.Direction == Direction.Bullish
            ? candle.High >= trade.Plan.Targets.Partial.Value
            : candle.Low <= trade.Plan.Targets.Partial.Value;

    /// <summary>
    /// Floors the T1 partial leg (<c>PartialFraction × Size</c>) DOWN to the contract lot step — the same flooring
    /// <see cref="PositionSizer"/> applies to the opening size — so the leg is always on the lot grid and tradeable.
    /// A partial is taken only when BOTH the floored leg AND the residual runner reach the minimum lot; otherwise no
    /// partial is booked this bar (the runner stays whole and the stop/runner closes it in full later), so the
    /// aggregate never sees a sub-minimum off-grid <see cref="PositionSize"/>. Reached only with no prior partial
    /// (<see cref="TradeLifecycle.Open"/>), so <c>RemainingSize == Size</c> and the residual is measured off Size.
    /// </summary>
    private bool TryFloorPartialLeg(PaperTrade trade, out PositionSize legSize)
    {
        var rawLeg = _options.PartialFraction * trade.Size.Lots;
        var flooredLeg = Math.Floor(rawLeg / _contractSpec.LotStep) * _contractSpec.LotStep;
        var residual = trade.Size.Lots - flooredLeg;

        if (flooredLeg >= _contractSpec.MinLot && residual >= _contractSpec.MinLot)
        {
            legSize = new PositionSize(flooredLeg);
            return true;
        }

        legSize = default;
        return false;
    }
}
