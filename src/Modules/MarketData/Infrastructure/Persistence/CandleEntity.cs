namespace IctTrader.MarketData.Infrastructure.Persistence;

/// <summary>
/// EF Core entity for a persisted OHLC candle (plan §7 — keeps EF out of the domain VO).
/// <para>
/// The natural key is <c>(Symbol, Timeframe, OpenTimeUtc)</c> — matching the UNIQUE index in
/// <see cref="Configurations.CandleConfiguration"/> that makes UPSERT idempotent (INSERT ON CONFLICT DO
/// NOTHING).  Candles are immutable once written so there is NO <c>xmin</c> concurrency token and NO
/// update path — the write-model is append-only (plan §6.3 guardrail).
/// </para>
/// </summary>
internal sealed class CandleEntity
{
    // Private parameterless ctor for EF Core materialisation.
    private CandleEntity()
    {
    }

    public CandleEntity(
        string symbol,
        string timeframe,
        DateTimeOffset openTimeUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume)
    {
        Symbol = symbol;
        Timeframe = timeframe;
        OpenTimeUtc = openTimeUtc;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    /// <summary>Surrogate PK — stable identity across reloads.</summary>
    public long Id { get; private set; }

    /// <summary>Instrument ticker in the scanner's canonical form, e.g. <c>EURUSD</c>.</summary>
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>Timeframe string matching the <c>Timeframe</c> enum member name, e.g. <c>M5</c>.</summary>
    public string Timeframe { get; private set; } = string.Empty;

    /// <summary>Bar open time, always UTC (<c>timestamptz</c>). Part of the natural unique key.</summary>
    public DateTimeOffset OpenTimeUtc { get; private set; }

    public decimal Open { get; private set; }

    public decimal High { get; private set; }

    public decimal Low { get; private set; }

    public decimal Close { get; private set; }

    public decimal Volume { get; private set; }
}
