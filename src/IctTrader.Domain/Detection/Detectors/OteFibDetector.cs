using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Detects an Optimal Trade Entry (plan §2.5.1 step 7) and emits <see cref="ConfluenceCondition.OteZone"/> (0.7,
/// not required). It delegates the band projection + array-level selection to the shared
/// <see cref="OteEntryResolver"/> (so the entry cannot drift from the draw-on-liquidity detector): the 62–79%
/// band (sweet spot 70.5%) is cast onto the pre-validated displacement leg and an in-band FVG/OB key level is
/// chosen — nearest the sweet spot by default, or (FVG-SEM-2a, <c>FvgOptions.StrictFirstFvg</c>) the shallowest
/// "first" FVG. A fully retraced leg (OteVoidedOnFullRetrace) or no overlapping array (OteSkippedNoOverlap)
/// yields no match.
///
/// <para>This detector is the SINGLE WRITER of the entry marker: on every resolution it clears
/// <c>IsSelectedEntry</c>/<c>Stacked</c> on every open FVG, then marks the resolved entry FVG (when an FVG, not an
/// OB, wins) and flags it <c>Stacked</c> when a deeper gap sits within the stack proximity (FVG-SEM-2a).</para>
/// </summary>
public sealed class OteFibDetector : ISetupDetector
{
    private readonly OteOptions _options;
    private readonly FvgOptions _fvgOptions;

    public OteFibDetector(OteOptions options, FvgOptions fvgOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(fvgOptions);
        _options = options;
        _fvgOptions = fvgOptions;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.OteZone;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        var policy = new OteEntryResolver.OteSelectionPolicy(_fvgOptions.StrictFirstFvg, _fvgOptions.StackProximityPips);
        if (OteEntryResolver.Resolve(context, _options, policy) is not { } ote)
        {
            return DetectorResult.NoMatch; // no leg / fully retraced, or no overlapping array
        }

        MarkSelectedEntry(context, ote);

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = ote.Direction.ToString(),
            [EvidenceKeys.OteSweetSpot] = ote.SweetSpot,
            [EvidenceKeys.Timeframe] = ote.Timeframe.ToString(),
            [EvidenceKeys.Stacked] = ote.StackedFartherBound is not null,
        };

        return DetectorResult.Match(
            ote.Direction, ote.Level, ReasonFragments.OteEntry(ote.Direction, ote.SweetSpot, ote.Timeframe), evidence);
    }

    // Single-writer, clean-then-set: clear the marker on every open FVG, then mark the resolved entry FVG (none
    // when an OB level wins — the marker IS the entry FVG) and flag it stacked when a deeper gap is in proximity.
    private static void MarkSelectedEntry(MarketContext context, OteEntryResolver.OteEntry ote)
    {
        foreach (var fvg in context.OpenFvgs)
        {
            fvg.ClearEntrySelection();
        }

        if (ote.SelectedFvg is not { } selected)
        {
            return;
        }

        selected.SelectAsEntry();
        if (ote.StackedFartherBound is not null)
        {
            selected.MarkStacked();
        }
    }
}
