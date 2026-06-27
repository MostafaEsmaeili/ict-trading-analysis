using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The §2.4/§2.5.5 adaptive risk policy. The effective risk is decided in a fixed precedence:
/// <list type="number">
/// <item><b>Win-cycle override</b> — at each <see cref="RiskOptions.ConsecutiveWinsForLowestUnit"/>-win milestone
/// (every Nth consecutive win) the next trade drops to the lowest ladder unit to protect the run's profits, then the
/// cycle restarts (ramp back to base, build toward the next milestone); it does NOT latch low for the rest of a long
/// streak (§2.4 "after 5 wins drop to the lowest unit… then the same procedure starts again").</item>
/// <item><b>Base risk</b> — when there is no active loss streak, OR the drawdown has been recovered by
/// <see cref="RiskOptions.DipRecoveryFraction"/> of its depth (§2.5.5 "restore after recovering 50% of the loss").</item>
/// <item><b>Loss-ladder</b> — otherwise step down by the number of consecutive losses (1 loss → first reduction, ≥N →
/// the lowest unit), so risk shrinks while a drawdown is unrecovered (§2.5.5 "1% → 0.5% → 0.25%").</item>
/// </list>
/// The result is clamped to <c>[lowest unit, <see cref="RiskOptions.HardMaxRiskPercent"/>]</c> as a belt-and-suspenders
/// bound. Pure: it touches no clock and no I/O.
/// </summary>
public sealed class RiskManager : IRiskManager
{
    public RiskPercent EffectiveRisk(RiskState state, RiskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var ladder = options.ResolvedLossLadderPercents;
        var lowestUnit = ladder[^1];

        decimal percent;
        if (state.ConsecutiveWins > 0 && state.ConsecutiveWins % options.ConsecutiveWinsForLowestUnit == 0)
        {
            // At each N-win milestone the next trade sizes at the lowest unit, then the cycle restarts — a long streak
            // does NOT stay latched low (it ramps back to base and builds toward the next milestone).
            percent = lowestUnit;
        }
        else if (state.ConsecutiveLosses == 0 || DrawdownRecovered(state, options))
        {
            percent = options.BaseRiskPercent;
        }
        else
        {
            // 1 loss -> ladder[0], 2 losses -> [1], ... capped at the lowest unit.
            var step = Math.Min(state.ConsecutiveLosses - 1, ladder.Count - 1);
            percent = ladder[step];
        }

        percent = Math.Clamp(percent, lowestUnit, options.HardMaxRiskPercent);
        return new RiskPercent(percent);
    }

    /// <summary>
    /// True once equity has climbed back to at least <see cref="RiskOptions.DipRecoveryFraction"/> of the way from the
    /// drawdown trough toward the prior peak (§2.5.5). A flat or zero dip is treated as fully recovered.
    /// <para>Provenance: §2.5.5 anchors the 50% to the equity dip of the specific losing trade taken at the higher
    /// tier; this generalises it to the full peak→trough drawdown — a more robust, equivalent-in-spirit automation,
    /// tunable via <see cref="RiskOptions.DipRecoveryFraction"/>.</para>
    /// </summary>
    private static bool DrawdownRecovered(RiskState state, RiskOptions options)
    {
        var dip = state.PeakEquity.Amount - state.DipTrough.Amount;
        if (dip <= 0m)
        {
            return true;
        }

        var recovered = state.CurrentEquity.Amount - state.DipTrough.Amount;
        return recovered >= options.DipRecoveryFraction * dip;
    }
}
