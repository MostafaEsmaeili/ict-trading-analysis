using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Styles;

/// <summary>
/// Pure domain service that routes a delivered candle's <see cref="Timeframe"/> to the active trade styles whose
/// canonical ENTRY timeframe is that timeframe (plan §4.7 top-down cascade). Each <see cref="TradeStyle"/> is
/// scanned on its own entry TF — Scalp→M1, Intraday→M5, Swing→M15, Position→H4 by the configured
/// <c>Ict:TradeStyles</c> defaults — resolved through the <see cref="TradeStyleClassifier"/>
/// (<c>ResolvePolicy(style).EntryTimeframe</c>), so the map never hardcodes a TF and honours config overrides.
///
/// <para>This is the matrix router for the multi-granularity feed: the scan loop holds ONE single-symbol mutable
/// <c>SymbolScanner</c> per (symbol, timeframe, style) cell, and each cell must be fed only candles of its own
/// granularity (mixed TFs corrupt the FSM). Given a candle, <see cref="StylesFor"/> answers "which active styles
/// enter on THIS timeframe", so the handler dispatches the candle to exactly those cells — scanning every active
/// style on its own entry TF, and only what the feed actually delivers. Pure and deterministic: no clock, no
/// state; the same inputs always yield the same styles.</para>
/// </summary>
public sealed class StyleTimeframeMap
{
    private readonly TradeStyleClassifier _classifier;

    public StyleTimeframeMap(TradeStyleClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _classifier = classifier;
    }

    /// <summary>
    /// Returns the subset of <paramref name="activeStyles"/> whose canonical entry timeframe equals
    /// <paramref name="timeframe"/> — the styles to scan on a candle of that granularity. A timeframe that no
    /// active style enters on yields an empty list (the candle is a no-op for scanning), and a style appears at
    /// most once (the active set is expected pre-de-duplicated, e.g. via <c>ResolvedActiveStyles</c>).
    /// </summary>
    public IReadOnlyList<TradeStyle> StylesFor(Timeframe timeframe, IReadOnlyCollection<TradeStyle> activeStyles)
    {
        ArgumentNullException.ThrowIfNull(activeStyles);

        var matches = new List<TradeStyle>(activeStyles.Count);
        foreach (var style in activeStyles)
        {
            if (_classifier.ResolvePolicy(style).EntryTimeframe == timeframe)
            {
                matches.Add(style);
            }
        }

        return matches;
    }
}
