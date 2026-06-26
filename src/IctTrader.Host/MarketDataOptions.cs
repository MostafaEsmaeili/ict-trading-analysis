using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.Host;

/// <summary>
/// Selects which <b>read-only</b> market-data feed the runnable backend ingests from (plan §6, WP7), bound from
/// <c>Ict:MarketData</c> — no magic strings. The choice is structural-but-safe: BOTH providers are read-only by
/// shape (a candle stream with no order/broker surface), so the NON-NEGOTIABLE no-live-trading guardrail holds
/// whichever is selected.
/// <para>
/// <see cref="MarketFeedProvider.Replay"/> (the default) streams the deterministic CSV candle fixture configured
/// under <c>Ict:MarketData:Replay</c>; <see cref="MarketFeedProvider.Oanda"/> reads completed candles from the
/// OANDA <i>practice</i> REST API configured under <c>Ict:MarketData:Oanda</c>. Either way a single
/// <c>MarketDataIngestionHostedService</c> drives whatever <c>IMarketDataFeed</c> is registered into the bus.
/// </para>
/// </summary>
public sealed class MarketDataOptions
{
    public const string SectionName = "Ict:MarketData";

    /// <summary>The selected feed provider. Defaults to <see cref="MarketFeedProvider.Replay"/> (deterministic,
    /// fixture-driven) so a bare Host needs no external broker connection.</summary>
    public MarketFeedProvider Provider { get; init; } = MarketFeedProvider.Replay;
}
