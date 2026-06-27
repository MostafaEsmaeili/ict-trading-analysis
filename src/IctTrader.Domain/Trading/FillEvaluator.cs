using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure intrabar fill evaluator (plan §5.2). Touch tests use the bar HIGH/LOW (never close-only) so an ICT
/// wick-sweep that closes back inside still fills a resting stop (§2.5.8); resting orders fill at their LEVEL so a
/// stop-out books the R implied by the LIVE stop — exactly −1R when un-trailed, ~0R once trailed to breakeven
/// (§2.5.9) — and a runner books the plan reward-to-risk; a bar that straddles both stop and runner is resolved by
/// <see cref="FillOptions.StopVsTarget"/> (default worst-case StopFirst, applied to longs AND shorts). Gap-through /
/// slippage fill-price worsening is NOT yet modeled (a deferred §5.4 follow-on, spec §5 item 25): this slice fills
/// at the resting LEVEL, so a gap-through stop books optimistically at exactly −1R rather than the worse gapped price.
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

        // A fill can only be resolved against the trade's own instrument — a misrouted candle would silently
        // corrupt the simulation. (Candle is a value type, so it cannot be null; the timeframe is deliberately
        // NOT pinned to the trade's trigger timeframe — the planned sub-bar/tick fill path evaluates fills on a
        // finer timeframe, §5.2.)
        Guard.Against(candle.Symbol != trade.Symbol, "The candle must be for the trade's symbol.");

        var plan = trade.Plan;

        // High/Low touch tests, never close-only, so an ICT wick-sweep that closes back inside still fills the
        // resting stop (§2.5.8). The stop is the LIVE CurrentStop (which may have ratcheted toward profit, §2.5.9),
        // not the frozen original. Inclusive boundary: a resting order fills when price reaches its level.
        var stop = trade.CurrentStop.Value;
        bool stopTouched, runnerTouched;
        if (plan.Direction == Direction.Bullish)
        {
            stopTouched = candle.Low <= stop;
            runnerTouched = candle.High >= plan.Targets.Runner.Value;
        }
        else
        {
            stopTouched = candle.High >= stop;
            runnerTouched = candle.Low <= plan.Targets.Runner.Value;
        }

        // A bar that straddles both: the worst-case StopFirst assumption fills the stop for BOTH directions,
        // overriding the raw Open→Low→High→Close path (which would fill a short's target first). Resting orders
        // fill at their LEVEL — a stop-out books the R implied by the live stop (−1R un-trailed, ~0R at breakeven),
        // a runner books the plan reward-to-risk. Gap-through / slippage fill-price worsening is NOT yet modeled
        // (a deferred §5.4 follow-on, spec §5 item 25): this slice fills at the level, never the worse gapped price.
        if (stopTouched && runnerTouched && _options.StopVsTarget == IntrabarFillAssumption.StopFirst)
        {
            return FillDecision.Stop(trade.CurrentStop);
        }

        if (runnerTouched)
        {
            return FillDecision.Runner(plan.Targets.Runner);
        }

        return stopTouched ? FillDecision.Stop(trade.CurrentStop) : FillDecision.NoFill;
    }
}
