using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure §2.5.9 / §3.4 exit orchestrator. For one candle it runs the deterministic precedence
/// <b>protective-fill → scale → trail</b>: (1) if the bar reached the stop or the runner, the whole position closes
/// and nothing else happens this bar; (2) otherwise, on a surviving bar, take the T1 partial once (sized
/// <c>PartialFraction × Size</c> at the partial level via <see cref="IExecutionCostModel.ComputeExitLeg"/>); (3) and
/// ratchet the stop if the trail earned a tighter level. It DECIDES an apply-ordered <see cref="ExitPlan"/> (scale
/// before stop-move, both stamped at the bar-close time so the aggregate's monotonic timeline holds); the caller
/// APPLIES it. The max-hold / no-overnight time-exit is a deferred follow-on cut.
/// </summary>
public sealed class ExitManager : IExitManager
{
    private readonly IFillEvaluator _fillEvaluator;
    private readonly IStopTrailPolicy _stopTrailPolicy;
    private readonly IExecutionCostModel _costModel;
    private readonly ExitManagementOptions _options;

    public ExitManager(
        IFillEvaluator fillEvaluator,
        IStopTrailPolicy stopTrailPolicy,
        IExecutionCostModel costModel,
        ExitManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(fillEvaluator);
        ArgumentNullException.ThrowIfNull(stopTrailPolicy);
        ArgumentNullException.ThrowIfNull(costModel);
        ArgumentNullException.ThrowIfNull(options);
        _fillEvaluator = fillEvaluator;
        _stopTrailPolicy = stopTrailPolicy;
        _costModel = costModel;
        _options = options;
    }

    public ExitPlan Decide(PaperTrade trade, Candle candle, ExitContext context)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(trade.Status != TradeStatus.Open, "Only an open paper trade can be managed for an exit.");
        Guard.Against(candle.Symbol != trade.Symbol, "The candle must be for the trade's symbol.");

        var at = context.BarCloseUtc;

        // 1. Protective fill first (§2.5.8 worst-case). A stop/runner that hit closes the WHOLE remaining position;
        //    nothing else happens on a bar that already exits.
        var fill = _fillEvaluator.Evaluate(trade, candle);
        if (fill.IsFill)
        {
            var closeCosts = _costModel.ComputeExitLeg(trade, trade.RemainingSize);
            return new ExitPlan([ExitAction.Close(fill.ExitPrice!.Value, fill.CloseReason!.Value, closeCosts, at)]);
        }

        var actions = new List<ExitAction>();

        // 2. T1 scale-out — once, when a surviving bar reaches the partial target. Sized PartialFraction × the ORIGINAL
        //    size (the §2.5.9 "take half of the position"), booked at the partial LEVEL.
        if (trade.Lifecycle == TradeLifecycle.Open && ReachedPartial(trade, candle))
        {
            var legSize = new PositionSize(_options.PartialFraction * trade.Size.Lots);
            var legCosts = _costModel.ComputeExitLeg(trade, legSize);
            actions.Add(ExitAction.ScaleOut(
                trade.Plan.Targets.Partial, legSize, legCosts, TradeCloseReason.TargetHit, at));
        }

        // 3. Trail — ratchet the stop if the policy earned a strictly-tighter level. Decided against the SAME pre-bar
        //    state as the scale; listed AFTER the scale so the apply order keeps the timeline monotonic.
        var trail = _stopTrailPolicy.Evaluate(trade, candle);
        if (trail.ShouldMove)
        {
            actions.Add(ExitAction.MoveStop(trail.NewStop!.Value, at));
        }

        return actions.Count == 0 ? ExitPlan.NoOp : new ExitPlan(actions);
    }

    private static bool ReachedPartial(PaperTrade trade, Candle candle)
        => trade.Direction == Direction.Bullish
            ? candle.High >= trade.Plan.Targets.Partial.Value
            : candle.Low <= trade.Plan.Targets.Partial.Value;
}
