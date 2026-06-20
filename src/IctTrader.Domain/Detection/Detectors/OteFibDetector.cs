using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an Optimal Trade Entry (plan §2.5.1 step 7) and emits <see cref="ConfluenceCondition.OteZone"/> (0.7,
/// not required). It delegates the band projection + array-level selection to the shared
/// <see cref="OteEntryResolver"/> (so the entry cannot drift from the draw-on-liquidity detector): the 62–79%
/// band (sweet spot 70.5%) is cast onto the pre-validated displacement leg and a same-direction, same-timeframe
/// FVG/OB key level nearest the sweet spot is chosen. A fully retraced leg (OteVoidedOnFullRetrace) or no
/// overlapping array (OteSkippedNoOverlap) yields no match.
/// </summary>
public sealed class OteFibDetector : ISetupDetector
{
    private readonly OteOptions _options;

    public OteFibDetector(OteOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.OteZone;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (OteEntryResolver.Resolve(context, _options) is not { } ote)
        {
            return DetectorResult.NoMatch; // no leg / fully retraced, or no overlapping array
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = ote.Direction.ToString(),
            [EvidenceKeys.OteSweetSpot] = ote.SweetSpot,
            [EvidenceKeys.Timeframe] = ote.Timeframe.ToString(),
        };

        return DetectorResult.Match(
            ote.Direction, ote.Level, ReasonFragments.OteEntry(ote.Direction, ote.SweetSpot, ote.Timeframe), evidence);
    }
}
