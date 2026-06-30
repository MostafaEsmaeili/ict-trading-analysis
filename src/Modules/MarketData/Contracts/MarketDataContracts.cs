using IctTrader.SharedKernel.Messaging;

namespace IctTrader.MarketData.Contracts;

// ---- DTOs (camelCase JSON on the wire; mirrored in web/ict-dashboard/src/types/api.ts — plan §11.1 #4) ----

/// <summary>A read-only OHLC candle as published to other modules and the dashboard.</summary>
public sealed record CandleDto(
    string Symbol,
    string Timeframe,
    DateTimeOffset OpenTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

/// <summary>A read-only bid/ask quote.</summary>
public sealed record TickDto(string Symbol, DateTimeOffset TimeUtc, decimal Bid, decimal Ask);

/// <summary>The status of a configured data feed. <see cref="IsReadOnly"/> is always true (plan §6.3).</summary>
public sealed record FeedStatusDto(string Provider, bool Connected, bool IsReadOnly);

// ---- Integration messages on the bus (plan §3.0a) ----

public sealed record CandleIngested(CandleDto Candle) : IEvent;

public sealed record TickIngested(TickDto Tick) : IEvent;

public sealed record StartFeedCommand(string Provider, IReadOnlyList<string> Symbols) : ICommand;

public sealed record StopFeedCommand(string Provider) : ICommand;

public sealed record ListFeedsQuery : IQuery<IReadOnlyList<FeedStatusDto>>;

/// <summary>
/// The most-recent <paramref name="Max"/> candles for a <paramref name="Symbol"/> / <paramref name="Timeframe"/>
/// series in CHRONOLOGICAL (oldest→newest) order, for the dashboard's ICT Pattern Chart (plan §9.1). Additive —
/// the frozen REST wire (<c>GET /api/chart/{symbol}</c>) is unchanged; this is the bus query the Host routes to.
/// </summary>
public sealed record GetChartCandlesQuery(string Symbol, string Timeframe, int Max)
    : IQuery<IReadOnlyList<CandleDto>>;

/// <summary>
/// A time-bounded historical candle range for <paramref name="Symbol"/> / <paramref name="Timeframe"/>,
/// returned in CHRONOLOGICAL (oldest→newest) order (plan §7 / §9.1). Used by the extended
/// <c>GET /api/chart/{symbol}?from=&amp;to=</c> endpoint to render historical setup overlays for setups
/// older than the live ring-buffer window. At most <see cref="MaxRangeCandles"/> rows are returned.
/// </summary>
public sealed record GetChartRangeQuery(
    string Symbol,
    string Timeframe,
    DateTimeOffset From,
    DateTimeOffset To)
    : IQuery<IReadOnlyList<CandleDto>>;

