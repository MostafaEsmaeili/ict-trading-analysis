using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure intrabar fill evaluator (plan §5.2). Touch tests use the bar HIGH/LOW (never close-only) so an ICT
/// wick-sweep that closes back inside still fills a resting stop (§2.5.8); resting orders fill at their LEVEL so a
/// stop-out books exactly −1R and a runner books the plan reward-to-risk; a bar that straddles both stop and
/// runner is resolved by <see cref="FillOptions.StopVsTarget"/> (default worst-case StopFirst, applied to longs
/// AND shorts). Gap-through and spread/slippage worsening are the §5.4 cost model's job, not this evaluator's.
/// </summary>
public sealed class FillEvaluator : IFillEvaluator
{
    private readonly FillOptions _options;

    public FillEvaluator(FillOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public FillDecision Evaluate(PaperTrade trade, Candle candle)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(trade.Status != TradeStatus.Open, "Only an open paper trade can be evaluated for a fill.");

        var plan = trade.Plan;

        // High/Low touch tests, never close-only, so an ICT wick-sweep that closes back inside still fills the
        // resting stop (§2.5.8). Inclusive boundary: a resting order fills when price reaches its level.
        bool stopTouched, runnerTouched;
        if (plan.Direction == Direction.Bullish)
        {
            stopTouched = candle.Low <= plan.Stop.Value;
            runnerTouched = candle.High >= plan.Targets.Runner.Value;
        }
        else
        {
            stopTouched = candle.High >= plan.Stop.Value;
            runnerTouched = candle.Low <= plan.Targets.Runner.Value;
        }

        // A bar that straddles both: the worst-case StopFirst assumption fills the stop for BOTH directions,
        // overriding the raw Open→Low→High→Close path (which would fill a short's target first). Resting orders
        // fill at their LEVEL — a stop-out books exactly −1R, a runner books the plan reward-to-risk. Gap-through
        // and spread/slippage worsening are the §5.4 cost model's job, applied downstream.
        if (stopTouched && runnerTouched && _options.StopVsTarget == IntrabarFillAssumption.StopFirst)
        {
            return FillDecision.Stop(plan.Stop);
        }

        if (runnerTouched)
        {
            return FillDecision.Runner(plan.Targets.Runner);
        }

        return stopTouched ? FillDecision.Stop(plan.Stop) : FillDecision.NoFill;
    }
}
