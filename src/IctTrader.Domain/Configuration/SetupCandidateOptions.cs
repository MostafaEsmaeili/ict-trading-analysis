using IctTrader.Domain.Detection;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable confluence-FSM (ScanSession/SetupCandidate) mechanics (plan §4.4) — separate from the §2.5.3
/// scoring weights in <see cref="ConfluenceOptions"/>. The §2.5 entry model assembles within one killzone
/// and within the sweep→MSS→FVG→entry sequence, so the candidate's memory is bounded, not unbounded:
/// <list type="bullet">
/// <item><b>Standing conditions</b> (bias, premium/discount, killzone, calendar) reflect the CURRENT candle —
/// they are re-evaluated every candle and only count when re-emitted, so a price that crosses into the wrong
/// premium/discount half after the sweep silently withdraws its required match (the live half-veto).</item>
/// <item><b>Event conditions</b> (sweep, MSS, FVG, OB, OTE) latch on formation and AGE OUT after
/// <see cref="MaxAssemblyBars"/> so a stale sweep cannot complete a setup forming much later.</item>
/// </list>
/// Bound from <c>Ict:Scanning:Candidate</c>.
/// </summary>
public sealed class SetupCandidateOptions
{
    public const string SectionName = "Ict:Scanning:Candidate";

    /// <summary>
    /// The outer bound (in candles) a latched EVENT condition survives without being refreshed before it
    /// ages out. The killzone is the natural outer envelope (FX NY 07:00–10:00 ≈ 36 M5 bars); the inner
    /// sweep→MSS bound is owned by <c>MarketStructureShiftOptions.SweepToMssMaxBars</c>. Expressed in the
    /// trigger timeframe's bars (the candidate is single-timeframe per scan); tune per active style.
    /// </summary>
    public int MaxAssemblyBars { get; init; } = 36;

    /// <summary>Whether a change of active killzone resets the in-flight (unconfirmed) candidate (§2.5 — the setup must form within one killzone).</summary>
    public bool ResetOnKillzoneChange { get; init; } = true;

    /// <summary>
    /// The conditions that describe the CURRENT candle's state (not a one-off event) and so are re-evaluated
    /// every candle rather than latched: daily bias, the premium/discount half, killzone membership, and the
    /// calendar gate. Defaults to EMPTY so the .NET config binder REPLACES rather than APPENDS to a pre-populated
    /// initializer (see MarketContextOptions.cs for the documented rationale) — a non-empty default would be
    /// prepended to the operator's set and (after the HashSet dedupe) silently force REMOVED conditions back into
    /// the standing set, defeating an intent-to-move-to-event-latching. Consume
    /// <see cref="ResolvedStandingConditions"/>, never this.
    /// </summary>
    public IReadOnlyList<ConfluenceCondition> StandingConditions { get; init; } = [];

    /// <summary>
    /// The standing conditions to use — the configured set de-duplicated, or the §2.5 standing filters when none is
    /// configured. Consume this, never the raw <see cref="StandingConditions"/>.
    /// </summary>
    public IReadOnlyList<ConfluenceCondition> ResolvedStandingConditions =>
        StandingConditions.Count == 0 ? DefaultStandingConditions : StandingConditions.Distinct().ToArray();

    /// <summary>The §2.5 standing filters — true/false NOW, with no formation event to remember.</summary>
    public static IReadOnlyList<ConfluenceCondition> DefaultStandingConditions { get; } =
    [
        ConfluenceCondition.BiasAligned,
        ConfluenceCondition.PremiumDiscountHalf,
        ConfluenceCondition.KillzoneEntry,
        ConfluenceCondition.CalendarClear,
    ];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxAssemblyBars < 1)
        {
            errors.Add($"MaxAssemblyBars must be at least 1 but was {MaxAssemblyBars}.");
        }

        if (StandingConditions is null)
        {
            errors.Add("StandingConditions must not be null.");
        }

        return errors;
    }
}
