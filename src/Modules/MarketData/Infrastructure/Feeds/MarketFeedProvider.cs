namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// The market-data feed implementations the Host can select between (plan §6). Both are <b>read-only</b>:
/// <see cref="Replay"/> replays a recorded candle fixture (tests/backtest) and <see cref="Oanda"/> reads live +
/// historical candles from the OANDA practice REST API. Neither has an order path — the no-live-trading
/// guardrail is structural. The Host binds the choice from configuration in a follow-on slice.
/// </summary>
public enum MarketFeedProvider
{
    /// <summary>Replays a fixed candle set from a CSV/in-memory fixture (deterministic tests/backtest).</summary>
    Replay,

    /// <summary>Reads completed candles from the OANDA v20 practice REST API (read-only).</summary>
    Oanda,
}
