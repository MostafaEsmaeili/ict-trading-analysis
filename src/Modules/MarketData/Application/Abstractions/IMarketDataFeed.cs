using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Application.Abstractions;

/// <summary>
/// A <b>read-only</b> source of market data (plan §6.1/§6.3). The system NEVER routes orders; a feed only
/// produces candles, so <see cref="IsReadOnly"/> is always <c>true</c> by construction and the ingestion
/// path asserts it. Implementations (Replay for tests/backtest; OANDA-practice/Finnhub/TraderMade/MT5
/// later) are selected by provider and stream candles in chronological order so a replay reproduces a live
/// run bit-for-bit.
/// </summary>
public interface IMarketDataFeed
{
    /// <summary>The provider name (e.g. <c>"Replay"</c>) for status + selection.</summary>
    string Provider { get; }

    /// <summary>Always <c>true</c> — feeds are structurally read-only (the no-live-trading guardrail).</summary>
    bool IsReadOnly { get; }

    /// <summary>Streams candles in chronological order until exhausted or cancelled.</summary>
    IAsyncEnumerable<CandleDto> StreamCandlesAsync(CancellationToken cancellationToken = default);
}
