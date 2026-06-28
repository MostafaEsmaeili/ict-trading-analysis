using IctTrader.Domain.ValueObjects;
using IctTrader.Host.Backtesting;
using IctTrader.MarketData.Contracts;

namespace IctTrader.Host;

/// <summary>
/// Serves recorded-history candles to the chart for ANY selected (symbol, timeframe) from the same CSV datasets the
/// backtest uses (plan §9.1) — so the Live chart renders real candles for whatever asset/timeframe the operator
/// picks, not only the one series the live feed happens to be ingesting. Read-only recorded data (no order path).
/// The live ring buffer (<c>ChartCandleStore</c>) still takes precedence for the actively-scanned series; this is the
/// fallback the chart endpoint uses when that store is empty for the requested series.
/// </summary>
internal static class ChartHistory
{
    public static IReadOnlyList<CandleDto> RecentCandles(
        BacktestEngine engine, string symbol, string timeframe, int limit)
    {
        try
        {
            var candles = engine.LoadCandles(symbol, timeframe);
            var start = candles.Count <= limit ? 0 : candles.Count - limit;
            var slice = new List<CandleDto>(Math.Min(candles.Count, limit));
            for (var i = start; i < candles.Count; i++)
            {
                slice.Add(ToDto(candles[i]));
            }

            return slice;
        }
        // No dataset on disk for this (symbol, tf), or an unparseable row → empty, so the chart shows its empty
        // state rather than failing the request. The dataset directory is operator-controlled, not user input.
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException
            or FormatException or ArgumentException or InvalidOperationException)
        {
            return [];
        }
    }

    private static CandleDto ToDto(Candle c) =>
        new(c.Symbol.Value, c.Timeframe.ToString(), c.OpenTimeUtc, c.Open, c.High, c.Low, c.Close, c.Volume);
}
