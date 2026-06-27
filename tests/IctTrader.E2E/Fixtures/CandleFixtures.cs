using IctTrader.Domain.Setups;
using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Contracts;

namespace IctTrader.E2E.Fixtures;

/// <summary>
/// Builds the named-anchor fixtures the E2E scenarios pump through the real Host — the priced advisory
/// <see cref="SetupDto"/> and the M5 candle sequence that manages it — ALL derived from <see cref="IctAnchors"/>
/// (no magic numbers anywhere). The session start is anchored in New-York time: 02:00 NY = 06:00 UTC on a July
/// date (NY = UTC-4 under EDT), squarely inside the London Open killzone.
/// </summary>
internal static class CandleFixtures
{
    /// <summary>The confirming instant: 02:00 NY (London Open) on a fixed July date, expressed in UTC.</summary>
    public static readonly DateTimeOffset SessionStartUtc = new(2024, 7, 1, 6, 0, 0, TimeSpan.Zero);

    /// <summary>The M5 candle period — the entry-frame granularity for the default Intraday style.</summary>
    private static readonly TimeSpan BarPeriod = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The confirmed, priced, ADVISORY bullish London setup — the geometry the §2.5 scan would emit, projected onto
    /// the wire DTO. Entry/stop/targets/RR are the COMPUTED <see cref="IctAnchors"/>, never literals. A deterministic
    /// id keeps the scenario reproducible (same anchors → same setup), matching the Scanning module's wire convention.
    /// </summary>
    public static SetupDto BullishLondonSetup() => new(
        Id: DeterministicId,
        Symbol: IctAnchors.Symbol,
        Direction: IctAnchors.Direction.ToString(),
        Killzone: IctAnchors.Killzone.ToString(),
        Style: IctAnchors.Style.ToString(),
        Grade: SetupGrade.B.ToString(),
        TriggerTimeframe: IctAnchors.Timeframe.ToString(),
        Entry: IctAnchors.Entry,
        Stop: IctAnchors.Stop,
        Targets: [IctAnchors.Target1, IctAnchors.Target2],
        RewardRatio: IctAnchors.RewardRatio,
        Reason: SetupReasonText,
        DetectedAtUtc: SessionStartUtc,
        IsAdvisoryOnly: true);

    /// <summary>
    /// A candle that drives the open trade to its runner target: it trades up THROUGH the draw on liquidity (High
    /// clears T2) while its Low stays above the stop, so the protective-fill pass closes the WHOLE position at the
    /// runner — a TargetHit (the +RR winner). Opens one bar after the setup confirms, still inside the killzone.
    /// </summary>
    public static CandleDto RunnerToTargetCandle()
    {
        var open = IctAnchors.Entry;
        var high = IctAnchors.Target2 + IctAnchors.Pip;   // clears the draw → the runner fills
        var low = IctAnchors.Entry - IctAnchors.Pip * 2;  // stays well above the stop
        var close = IctAnchors.Target2;
        return Candle(SessionStartUtc + BarPeriod, open, high, low, close);
    }

    /// <summary>
    /// A candle that stops the open trade out: its Low pierces the protective stop (below the swept extreme), so the
    /// protective-fill pass closes the whole position at the stop — exactly −1R (the frozen 1R, §5.2). Used to prove
    /// the pipeline books a LOSS, not only a winner.
    /// </summary>
    public static CandleDto StopOutCandle()
    {
        var open = IctAnchors.Entry - IctAnchors.Pip * 5;
        var high = IctAnchors.Entry;
        var low = IctAnchors.Stop - IctAnchors.Pip;       // pierces the stop → stops out
        var close = IctAnchors.Stop + IctAnchors.Pip;
        return Candle(SessionStartUtc + BarPeriod, open, high, low, close);
    }

    private static CandleDto Candle(
        DateTimeOffset openTimeUtc, decimal open, decimal high, decimal low, decimal close) =>
        new(
            IctAnchors.Symbol,
            IctAnchors.Timeframe.ToString(),
            openTimeUtc,
            open,
            high,
            low,
            close,
            Volume);

    /// <summary>The §2.5 confluence story in the ubiquitous language — the alert feed surfaces this verbatim.</summary>
    private const string SetupReasonText =
        "Bullish FVG inside London killzone after Asian-low Judas sweep; MSS displacement confirmed; OTE entry";

    private const decimal Volume = 1_000m;

    /// <summary>A fixed id so a replayed setup is byte-identical (deterministic E2E).</summary>
    private static readonly Guid DeterministicId = new("e2e00000-0000-0000-0000-000000000001");
}
