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

    /// <summary>
    /// HTF daily-bias alignment (the web/community #1 win-rate filter — "a 5-minute entry must never contradict the
    /// daily bias"). When ON, the required <see cref="IctTrader.Domain.Detection.ConfluenceCondition.BiasAligned"/>
    /// match is ALSO gated on the confirming price being on the bias-correct side of the day's reference open (the
    /// 00:00-NY midnight open, or the dual midnight/08:30 macro reference for an index) — bearish wants price ABOVE the
    /// open (the Judas premium), bullish BELOW it. It reuses the SAME <see cref="IctTrader.Domain.Detection.MarketContext.ReferenceOpen(bool)"/>
    /// the optional <see cref="IctTrader.Domain.Detection.ConfluenceCondition.OpenPriceReference"/> scorer reads, so the
    /// two can never disagree — this STRENGTHENS the existing dealing-range bias rather than adding a parallel gate.
    /// <para>OFF by default — a deliberate §2.5.10 strengthening (TIME-10-derived), opt-in + per-instrument tunable, so
    /// the strict §2.5 default path stays byte-identical. It only WITHHOLDS the match (Σ=9.75 untouched); the detector
    /// still SETS <c>ctx.Bias</c> regardless, so the MSS lock / PD veto / Judas read downstream are unaffected. When ON
    /// and no reference open is captured yet it withholds (fail-CLOSED — the conservative choice for an opted-in gate).</para>
    /// </summary>
    public bool RequireReferenceOpenAgreement { get; init; }

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
