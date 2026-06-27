using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Emits the OPTIONAL <see cref="ConfluenceCondition.MacroTime"/> confluence (0.45, plan §2.5.5/§2.5.8). ICT's
/// algorithmic "macros" run at fixed New-York times (08:30 / 09:30 / 13:30 / 15:00) where price is most likely to be
/// delivered toward a draw; a setup confirming inside one of those windows carries extra confluence. CONFORMANT on the
/// anchor times; the window WIDTH is INVENTED-flagged (<see cref="MacroTimeOptions.MacroWindowMinutes"/>), so it is
/// small + operator-tunable.
///
/// <para>Non-directional (a TIME gate, like <see cref="KillzoneEntryDetector"/>), so the FSM counts it for either side.
/// A confluence (scoring-only), NOT a RequiredCondition — its absence never blocks a setup. NY conversion goes through
/// the DST-aware <see cref="NyClock"/> (plan §4.8), so the read is host-zone-independent.</para>
/// </summary>
public sealed class MacroTimeDetector : ISetupDetector
{
    private readonly NyClock _nyClock;
    private readonly MacroTimeOptions _options;

    public MacroTimeDetector(NyClock nyClock, MacroTimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(nyClock);
        ArgumentNullException.ThrowIfNull(options);
        _nyClock = nyClock;
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.MacroTime;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.Enabled)
        {
            return DetectorResult.NoMatch;
        }

        var nyTime = _nyClock.NewYorkTimeOfDay(current.OpenTimeUtc);
        if (NearestMacro(nyTime) is not { } anchor)
        {
            return DetectorResult.NoMatch; // not within any macro window
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.MacroWindowTime] = anchor.ToString("HH\\:mm", System.Globalization.CultureInfo.InvariantCulture),
        };

        return DetectorResult.Match(
            direction: null, keyLevel: null, ReasonFragments.MacroTime(anchor), evidence);
    }

    private TimeOnly? NearestMacro(TimeOnly nyTime)
    {
        foreach (var anchor in _options.ResolvedMacroAnchors)
        {
            // Inclusive ±window minutes around the anchor (same-day wall-clock; the macros are intraday, never
            // crossing midnight, so a simple absolute-minute distance is exact).
            var distanceMinutes = Math.Abs((nyTime - anchor).TotalMinutes);
            if (distanceMinutes <= _options.MacroWindowMinutes)
            {
                return anchor;
            }
        }

        return null;
    }
}
