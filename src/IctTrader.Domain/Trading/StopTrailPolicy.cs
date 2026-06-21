using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure §2.5.9/§2.5.10 stop-trail ladder. From the candle's favorable excursion it measures progress on two
/// axes — the entry→T1 range and the entry→original-stop (1R) — and tightens the stop tightest-wins: 50% of the T1
/// range → a residual-risk stop, 75% → breakeven, and break-even-at-1R. It only proposes a STRICTLY tighter stop the
/// current bar has not already traded through in the profit direction (§2.5.8), so the caller's
/// <see cref="PaperTrade.MoveStop"/> ratchet never throws and never books an already-passed level; otherwise it Holds.
/// R is measured against the FROZEN original 1R (§5.2), never the live trailed stop.
/// </summary>
public sealed class StopTrailPolicy : IStopTrailPolicy
{
    private readonly StopTrailOptions _options;

    public StopTrailPolicy(StopTrailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public StopTrailDecision Evaluate(PaperTrade trade, Candle candle)
    {
        ArgumentNullException.ThrowIfNull(trade);
        Guard.Against(trade.Status != TradeStatus.Open, "Only an open paper trade can be trailed.");
        Guard.Against(candle.Symbol != trade.Symbol, "The candle must be for the trade's symbol.");

        var isLong = trade.Direction == Direction.Bullish;
        var entry = trade.Entry.Value;
        var risk = trade.InitialRiskPerUnit;                            // the FROZEN original 1R (§5.2)
        var t1Range = Math.Abs(trade.Plan.Targets.Partial.Value - entry);

        // Favorable excursion off the bar extreme, on the profit side.
        var favExcursion = isLong ? candle.High - entry : entry - candle.Low;
        var t1Progress = favExcursion / t1Range;                        // axis (a): entry→T1 progress
        var rReached = favExcursion / risk;                             // axis (b): R vs the frozen 1R

        // Each fired rung's candidate, authored in increasing tightness so the last to fire is the tightest-wins stop
        // (breakeven dominates the residual-risk rung; the 1R axis wins the breakeven tie-break label).
        Price? candidate = null;
        var trigger = default(StopTrailTrigger);

        if (t1Progress >= _options.TrailHalfwayFraction)
        {
            var residual = _options.TrailHalfwayResidualRiskFraction * risk;
            candidate = new Price(isLong ? entry - residual : entry + residual);
            trigger = StopTrailTrigger.T1HalfResidualRisk;
        }

        if (t1Progress >= _options.TrailBreakevenFraction)
        {
            candidate = trade.Entry;
            trigger = StopTrailTrigger.T1ThreeQuarterBreakeven;
        }

        if (rReached >= _options.BreakEvenAtR)
        {
            candidate = trade.Entry;
            trigger = StopTrailTrigger.BreakevenAtOneR;
        }

        if (candidate is null)
        {
            return StopTrailDecision.Hold;
        }

        var newStop = candidate.Value.Value;
        var current = trade.CurrentStop.Value;

        // Strictly-tighter ratchet: never propose a non-tightening move (MoveStop would reject it).
        var tightens = isLong ? newStop > current : newStop < current;
        if (!tightens)
        {
            return StopTrailDecision.Hold;
        }

        // Belt-and-suspenders: never propose a stop at/through the runner. The current rungs cap at breakeven
        // (< runner by the TradePlan invariant), so this only protects a future rung from a throwing MoveStop.
        var runner = trade.Plan.Targets.Runner.Value;
        var beforeRunner = isLong ? newStop < runner : newStop > runner;
        if (!beforeRunner)
        {
            return StopTrailDecision.Hold;
        }

        // Cap (§2.5.8): the stop must be a REAL resting stop the bar has not already traded through — strictly beyond
        // the bar extreme, consistent with the fill evaluator's inclusive touch test. If the earned level is inside
        // this bar's adverse range (a pullback / gap), skip and wait for a clean bar rather than book an already-hit
        // stop or invent a sub-bar level.
        var restsBehindBar = isLong ? newStop < candle.Low : newStop > candle.High;
        if (!restsBehindBar)
        {
            return StopTrailDecision.Hold;
        }

        return StopTrailDecision.Move(candidate.Value, trigger);
    }
}
