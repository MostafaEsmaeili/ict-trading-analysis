using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// Pure mapping from the wire <see cref="CandleDto"/> (MarketData.Contracts) to the domain
/// <see cref="Candle"/> value object. The <c>Timeframe</c> string is parsed against the domain
/// <see cref="Timeframe"/> enum member NAMES (the frozen wire contract — plan §11.1), failing fast on an
/// unparseable value so a malformed feed cannot silently scan against the wrong timeframe. No business logic
/// — the <see cref="Candle"/> ctor enforces every OHLC invariant.
/// </summary>
internal static class CandleDtoMapper
{
    public static Candle ToDomain(CandleDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (!Enum.TryParse<Timeframe>(dto.Timeframe, ignoreCase: false, out var timeframe))
        {
            throw new ArgumentException(
                $"Unparseable candle timeframe '{dto.Timeframe}' — must be one of {string.Join(", ", Enum.GetNames<Timeframe>())}.",
                nameof(dto));
        }

        return new Candle(
            new Symbol(dto.Symbol),
            timeframe,
            dto.OpenTimeUtc,
            dto.Open,
            dto.High,
            dto.Low,
            dto.Close,
            dto.Volume);
    }
}
