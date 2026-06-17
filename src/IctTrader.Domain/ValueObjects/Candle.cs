using IctTrader.Domain.Common;

namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// An OHLC candle for a symbol/timeframe. FROZEN CONTRACT (plan §11.1 #2): <see cref="OpenTimeUtc"/> is
/// ALWAYS UTC — NY-session conversion happens only in NyClock (plan §4.8), never on the stored value.
/// Invariants: high is the bar maximum, low the bar minimum, volume is non-negative.
/// </summary>
public readonly record struct Candle
{
    public Candle(
        Symbol symbol,
        Timeframe timeframe,
        DateTimeOffset openTimeUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        Guard.Against(openTimeUtc.Offset != TimeSpan.Zero, "Candle.OpenTimeUtc must be UTC (zero offset).");
        Guard.Against(high < low, "Candle high must be >= low.");
        Guard.Against(high < open || high < close, "Candle high must be the bar maximum.");
        Guard.Against(low > open || low > close, "Candle low must be the bar minimum.");
        Guard.Against(volume < 0m, "Candle volume must be non-negative.");

        Symbol = symbol;
        Timeframe = timeframe;
        OpenTimeUtc = openTimeUtc;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    public Symbol Symbol { get; }

    public Timeframe Timeframe { get; }

    public DateTimeOffset OpenTimeUtc { get; }

    public decimal Open { get; }

    public decimal High { get; }

    public decimal Low { get; }

    public decimal Close { get; }

    public decimal Volume { get; }

    public bool IsUpClose => Close > Open;

    public bool IsDownClose => Close < Open;
}
