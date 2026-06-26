using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Application.Abstractions;

/// <summary>
/// A <b>read-only</b> source of market data (plan §6.1/§6.3). Read-only is structural, not a flag: this
/// abstraction exposes ONLY a way to read candles — there is no write/order/send method an implementation
/// could add through it — so the system cannot route an order via a feed ("structurally impossible, not
/// flag-disabled"). Implementations (Replay for tests/backtest; OANDA-practice/Finnhub/TraderMade/MT5 later)
/// are selected by provider and stream candles in chronological order so a replay reproduces a live run
/// bit-for-bit. The feed's read-only STATUS is reported separately on <c>FeedStatusDto.IsReadOnly</c>.
/// </summary>
public interface IMarketDataFeed
{
    /// <summary>The provider name (e.g. <c>"Replay"</c>) for status + selection.</summary>
    string Provider { get; }

    /// <summary>Streams candles in chronological order until exhausted or cancelled.</summary>
    IAsyncEnumerable<CandleDto> StreamCandlesAsync(CancellationToken cancellationToken = default);
}
