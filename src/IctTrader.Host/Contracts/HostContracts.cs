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

/// <summary>Body for <c>POST /api/paper-trades</c> — an advisory request to SIMULATE a trade (plan §6.3).</summary>
public sealed record ExecutePaperTradeRequest(Guid SetupId);
