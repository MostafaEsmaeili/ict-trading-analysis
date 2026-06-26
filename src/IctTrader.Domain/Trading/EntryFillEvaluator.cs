using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The pure entry-limit evaluator (plan §2.5.1 step 7). ICT's entry is a resting LIMIT at the OTE/FVG/OB level
/// (Mentorship FULL PLAYLIST:876-877 "limit order where FVG/OB coincides with OTE"; :933 a limit "may not get
/// filled"; :2817/:3097 don't chase an un-retraced entry); price has displaced AWAY on the MSS leg and the
/// limit fills ONLY if price RETRACES back into the zone. Touch tests read the bar HIGH/LOW (never close-only, so
/// an intrabar wick into the zone fills even if the bar closes back outside, §2.5.8); a resting limit fills at its
/// LEVEL (the plan entry), never the better gap price, so the planned 1R equals the booked 1R. The buy@ask /
/// sell@bid spread stays the §5.4 <see cref="IExecutionCostModel.ComputeEntryLeg"/> cost line — NOT a fill-price
/// worsening here — so the same dollars are never double-counted (mirroring the exit <see cref="FillEvaluator"/>).
/// Pure: it returns an immutable <see cref="EntryFillDecision"/> with no timestamp; the caller stamps the open time.
/// <para>
/// SCOPE: this is the per-bar touch DECISION only. The same-bar entry-then-stop −1R straddle (a fast bar that
/// fills the limit then runs to the stop), the armed-order lifecycle + no-chase cancellation, and the
/// EntryMode wiring are deferred follow-on cuts.
/// </para>
/// </summary>
public sealed class EntryFillEvaluator : IEntryFillEvaluator
{
    private readonly EntryManagementOptions _options;
    private readonly SymbolSpec _symbolSpec;

    /// <param name="options">The §2.5.1-step-7 entry policy — its EG-3 close-proximity flag governs the recorded fill price.</param>
    /// <param name="symbolSpec">
    /// The instrument spec the EG-3 pip→price tolerance converts against (so the band is no-magic-number). REQUIRED — it
    /// must be the trade's own instrument so the tolerance can never silently mis-scale (e.g. an FX-major default applied
    /// to a JPY pair or an index would be ~100× off); the host injects the real spec at the DI seam.
    /// </param>
    public EntryFillEvaluator(EntryManagementOptions options, SymbolSpec symbolSpec)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(symbolSpec);
        _options = options;
        _symbolSpec = symbolSpec;
    }

    public EntryFillDecision Evaluate(Setup setup, Candle candle)
    {
        ArgumentNullException.ThrowIfNull(setup);

        // The limit can only fill against its own instrument — a misrouted candle would silently corrupt the
        // simulation. (Candle is a value type, so it cannot be null.)
        Guard.Against(candle.Symbol != setup.Symbol, "The candle must be for the setup's symbol.");

        var entry = setup.Plan.Entry.Value;

        // The limit rests at the OTE/FVG entry; price must RETRACE into it. A long buy-limit sits in DISCOUNT below
        // the post-displacement price, so it fills when the bar trades DOWN to it (Low ≤ entry); a short sell-limit
        // sits in PREMIUM above, filling when the bar trades UP to it (High ≥ entry). Inclusive boundary — an exact
        // kiss fills (mirrors the exit evaluator's resting-order touch). NB these are NOT the exit operators: a long
        // ENTRY fills on Low ≤ level (price coming down to the discount limit), the discount-side mirror of a short.
        var touched = setup.Direction == Direction.Bullish
            ? candle.Low <= entry
            : candle.High >= entry;

        if (!touched)
        {
            return EntryFillDecision.Hold;
        }

        return EntryFillDecision.Filled(new Price(ResolveFillPrice(setup.Direction, entry, candle)));
    }

    // EG-3 v1 close-proximity (Ep10/29/07/22/35) — a DIAGNOSTIC fill price only. With UseCloseProximityEntry OFF
    // (default) the resting limit fills at its LEVEL (the plan entry), byte-identical to before. ON, the recorded fill
    // is the touched price clamped to a small entry-anchored band [entry-tol, entry] (long) / [entry, entry+tol]
    // (short). CRITICAL: PaperTradeFactory.OpenArmed opens the trade at Plan.Entry regardless, so this NEVER moves the
    // open — InitialRiskPerUnit/RiskBudget stay vs the original 1R (frozen-1R invariant). The buy@ask/sell@bid spread
    // remains the §5.4 ComputeEntryLeg cost line, not a fill-price worsening here, so no dollars are double-counted.
    private decimal ResolveFillPrice(Direction direction, decimal entry, Candle candle)
    {
        if (!_options.UseCloseProximityEntry)
        {
            return entry;
        }

        var tol = _symbolSpec.PipsToPrice(new Pips(_options.CloseProximityTolerancePips));
        return direction == Direction.Bullish
            ? Math.Clamp(candle.Low, entry - tol, entry)   // band [entry - tol, entry]
            : Math.Clamp(candle.High, entry, entry + tol); // band [entry, entry + tol]
    }
}
