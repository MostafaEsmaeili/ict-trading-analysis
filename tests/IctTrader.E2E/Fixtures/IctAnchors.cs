using IctTrader.Domain.Sessions;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.E2E.Fixtures;

/// <summary>
/// The named ICT price + time anchors of the canonical 2022 Intraday FVG model (plan §2.5), expressed ONCE so
/// every fixture candle and the priced setup derive from the SAME story — never magic numbers. The model is the
/// ICT sequence in the ubiquitous language: an <b>Asian range</b> forms overnight; a <b>Judas sweep</b> raids the
/// sell-side below it (against the long); an energetic <b>MSS / displacement</b> candle closes back above the prior
/// short-term swing inside the <b>London Open killzone</b>, leaving a bullish <b>fair value gap</b>; price retraces
/// to the <b>OTE</b> entry in that gap; the trade targets the buy-side <b>draw on liquidity</b> above.
///
/// <para>Everything is built from one base price and a single pip granularity, so the geometry is self-consistent
/// (stop below the swept low, entry at the 70.5% OTE of the displacement leg, T1 at the entry→runner equilibrium,
/// T2 at the draw) and the reward ratio is COMPUTED from the anchors, not asserted as a literal.</para>
///
/// <para>Times are New York anchored (plan §4.8). 02:00 NY on a July date is 06:00 UTC (NY = UTC-4 under EDT),
/// squarely inside the London Open killzone — the London window is 02:00–05:00 NY (<see cref="KillzoneSchedule"/>).</para>
/// </summary>
internal static class IctAnchors
{
    /// <summary>The instrument the whole story is told on — a single FX major (price geometry + money geometry).</summary>
    public const string Symbol = "EURUSD";

    /// <summary>The advisory trade direction: a bullish London setup (the §2.5.10 "London Open = day's low" odds).</summary>
    public const Direction Direction = IctTrader.Domain.ValueObjects.Direction.Bullish;

    /// <summary>The killzone the displacement + entry occur in.</summary>
    public const Killzone Killzone = IctTrader.Domain.Sessions.Killzone.LondonOpen;

    /// <summary>The style/timeframe triple's entry frame for the default Intraday model (plan §4.7).</summary>
    public const TradeStyle Style = TradeStyle.Intraday;

    public const Timeframe Timeframe = IctTrader.Domain.ValueObjects.Timeframe.M5;

    /// <summary>One pip for a 5-decimal FX major — the single granularity ALL distances are quoted in.</summary>
    public const decimal Pip = 0.0001m;

    // ── The price anchors of the story, all derived from one equilibrium base in whole pips ──────────────────

    /// <summary>Equilibrium of the Asian range — the neutral mid the whole day pivots around.</summary>
    public const decimal AsianEquilibrium = 1.0850m;

    /// <summary>Half-width of the Asian consolidation, in pips (so AsianHigh/Low straddle the equilibrium).</summary>
    private const int AsianHalfRangePips = 30;

    /// <summary>How far below the Asian low the Judas sweep wicks before rejecting (the stop-raid depth).</summary>
    private const int JudasSweepPips = 12;

    /// <summary>Stop buffer beyond the swept extreme (~the §2.5 1–2 tick / ~10-pip FX clearance).</summary>
    private const int StopBufferPips = 8;

    /// <summary>The displacement leg height in pips — the energetic MSS thrust the OTE retrace is measured on.</summary>
    private const int DisplacementLegPips = 60;

    /// <summary>The 70.5% OTE sweet-spot retrace of the displacement leg (Primer-flagged default, plan §2.5.7).</summary>
    private const decimal OteRetrace = 0.705m;

    /// <summary>The buy-side draw above the range the long is targeting — the opposing liquidity pool.</summary>
    private const int DrawAboveAsianHighPips = 50;

    // ── The story, computed ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Top of the Asian range — the buy-side liquidity resting above (relative-equal highs).</summary>
    public static decimal AsianHigh => AsianEquilibrium + Pips(AsianHalfRangePips);

    /// <summary>Bottom of the Asian range — the sell-side liquidity the Judas sweep raids.</summary>
    public static decimal AsianLow => AsianEquilibrium - Pips(AsianHalfRangePips);

    /// <summary>The low printed by the Judas sweep wick — below the Asian low, then rejected (sweep != run).</summary>
    public static decimal JudasSweepLow => AsianLow - Pips(JudasSweepPips);

    /// <summary>The protective stop — beyond (below) the swept extreme by the buffer (§2.5.1 step 8).</summary>
    public static decimal Stop => JudasSweepLow - Pips(StopBufferPips);

    /// <summary>The terminus (top) of the displacement leg — where the MSS thrust closed beyond the prior swing.</summary>
    public static decimal DisplacementHigh => JudasSweepLow + Pips(DisplacementLegPips);

    /// <summary>
    /// The OTE entry — the 70.5% retrace of the displacement leg (origin = swept low, terminus = displacement high),
    /// where the bullish FVG and the OTE band coincide (plan §2.5.1 step 7). A long enters on a LIMIT here.
    /// </summary>
    public static decimal Entry => DisplacementHigh - (DisplacementHigh - JudasSweepLow) * OteRetrace;

    /// <summary>T2 — the runner target: the buy-side draw on liquidity above the Asian high (the §2.5 DOL).</summary>
    public static decimal Target2 => AsianHigh + Pips(DrawAboveAsianHighPips);

    /// <summary>T1 — the partial: the entry→runner equilibrium (50%, plan §2.5.5 target ladder).</summary>
    public static decimal Target1 => Entry + (Target2 - Entry) / 2m;

    /// <summary>The frozen 1R risk-per-unit distance: |entry − stop| (price terms).</summary>
    public static decimal RiskPerUnit => Entry - Stop;

    /// <summary>The reward ratio entry→runner, COMPUTED from the anchors (never an asserted literal).</summary>
    public static decimal RewardRatio => (Target2 - Entry) / RiskPerUnit;

    private static decimal Pips(int count) => count * Pip;
}
