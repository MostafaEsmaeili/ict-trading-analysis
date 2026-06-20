using IctTrader.Domain.MarketStructure;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable daily-bias detection (plan §2.5.1 step 1, §2.5.10). Bias is the dealing-range premium/discount read:
/// discount (&lt;50%) ⇒ bullish, premium (&gt;50%) ⇒ bearish, exactly 50% ⇒ NEUTRAL (no trade). The
/// 3-consecutive-close confirmation is §2.5.10 provenance-flagged (corroborative, not a hard gate) so it
/// defaults OFF. Bound from <c>Ict:Detection:Bias</c>.
/// </summary>
public sealed class DailyBiasOptions
{
    public const string SectionName = "Ict:Detection:Bias";

    /// <summary>The equilibrium split of the dealing range (default 0.50). Shared boundary semantics via EquilibriumBoundaryPolicy.</summary>
    public decimal EquilibriumPercent { get; init; } = 0.50m;

    /// <summary>The §2.5.10 provenance-flagged "3 consecutive directional daily closes" corroborator — OFF by default.</summary>
    public bool RequireConsecutiveCloseConfirmation { get; init; }

    public int ConsecutiveCloseCount { get; init; } = 3;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        // The equilibrium is the ICT 50% boundary, not a tuning knob — pin it so a config typo cannot silently
        // move the bias split (and keep it identical to the premium/discount gate via the shared policy).
        if (EquilibriumPercent != EquilibriumBoundaryPolicy.IctEquilibriumPercent)
        {
            errors.Add($"EquilibriumPercent must be the ICT equilibrium {EquilibriumBoundaryPolicy.IctEquilibriumPercent} but was {EquilibriumPercent}.");
        }

        if (ConsecutiveCloseCount < 1)
        {
            errors.Add($"ConsecutiveCloseCount must be at least 1 but was {ConsecutiveCloseCount}.");
        }

        return errors;
    }
}
