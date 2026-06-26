using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Host;

/// <summary>Response for <c>GET /api/chart</c> — candles plus the ICT overlays to draw (plan §9.1).</summary>
public sealed record ChartResponse(
    string Symbol,
    string Timeframe,
    string Style,
    IReadOnlyList<CandleDto> Candles,
    IReadOnlyList<SetupDto> Overlays);

/// <summary>
/// The request defaults the <c>GET /api/chart/{symbol}</c> endpoint applies — the default timeframe/style and the
/// per-request window caps (no magic numbers in the endpoint). The window caps are how MANY rows to return; the
/// stores themselves bound how many they RETAIN (<see cref="MarketData.Application.Chart.ChartCandleStore.MaxCandlesPerSeries"/>
/// / <see cref="Scanning.Application.Scanning.RecentSetupStore.MaxSetupsPerSymbol"/>).
/// </summary>
internal static class ChartDefaults
{
    /// <summary>The default series timeframe when the caller omits <c>?tf=</c> (the §2.5 entry timeframe).</summary>
    public const string Timeframe = "M5";

    /// <summary>The default trade style when the caller omits <c>?style=</c>.</summary>
    public const string Style = "Intraday";

    /// <summary>How many of the most-recent candles to return for the chart series (chronological).</summary>
    public const int MaxCandles = 500;

    /// <summary>How many of the most-recent setups to return as chart overlays (newest-first).</summary>
    public const int MaxOverlays = 20;
}

/// <summary>Body for <c>POST /api/paper-trades</c> — an advisory request to SIMULATE a trade (plan §6.3).</summary>
public sealed record ExecutePaperTradeRequest(Guid SetupId);
