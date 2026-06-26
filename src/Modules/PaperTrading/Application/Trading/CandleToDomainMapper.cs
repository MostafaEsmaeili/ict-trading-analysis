using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Pure mapping from the wire <see cref="CandleDto"/> (MarketData.Contracts) to the domain <see cref="Candle"/>
/// value object — PaperTrading's OWN mapper (the Scanning module's <c>CandleDtoMapper</c> is internal to that
/// module; the two never cross the §3.0a boundary, even though the mapping is the same). The <c>Timeframe</c>
/// string parses against the domain <see cref="Timeframe"/> enum member NAMES (the frozen wire contract), failing
/// fast on an unparseable value so a malformed feed cannot silently manage a trade against the wrong timeframe.
///
/// <para>It also derives the <b>bar-close time</b> the orchestrator stamps every exit/entry action with: the
/// candle's <see cref="CandleDto.OpenTimeUtc"/> plus the timeframe's period (M5 → +5 min, H1 → +1 h, …). The
/// domain <see cref="Timeframe"/> enum carries no duration, so the period is mapped from a small explicit table —
/// no magic numbers leak into the handler.</para>
/// </summary>
internal static class CandleToDomainMapper
{
    public static Candle ToDomain(CandleDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        return new Candle(
            new Symbol(dto.Symbol),
            ParseTimeframe(dto.Timeframe),
            dto.OpenTimeUtc.ToUniversalTime(),
            dto.Open,
            dto.High,
            dto.Low,
            dto.Close,
            dto.Volume);
    }

    /// <summary>The UTC time the candle closes — its open time plus the timeframe period. The orchestrator stamps
    /// every entry/exit action with this (the only "now" on the clock-free domain path).</summary>
    public static DateTimeOffset BarCloseUtc(CandleDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        var openTimeUtc = dto.OpenTimeUtc.ToUniversalTime();
        var timeframe = ParseTimeframe(dto.Timeframe);

        // A month is a CALENDAR period, not a fixed span — AddMonths handles 28/29/30/31-day months correctly,
        // so a monthly candle's close is never off by a few days (which would skew the trade's time-exit math).
        return timeframe == Timeframe.MN1 ? openTimeUtc.AddMonths(1) : openTimeUtc + PeriodOf(timeframe);
    }

    private static Timeframe ParseTimeframe(string timeframe)
    {
        if (!Enum.TryParse<Timeframe>(timeframe, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw new ArgumentException(
                $"Unparseable candle timeframe '{timeframe}' — must be one of {string.Join(", ", Enum.GetNames<Timeframe>())}.",
                nameof(timeframe));
        }

        return parsed;
    }

    /// <summary>The wall-clock span of one candle of the given timeframe — the §4.7 cascade periods, mapped
    /// explicitly (the domain enum carries no duration) so a bar-close time is never a literal in the handler.</summary>
    private static TimeSpan PeriodOf(Timeframe timeframe) => timeframe switch
    {
        Timeframe.M1 => TimeSpan.FromMinutes(1),
        Timeframe.M3 => TimeSpan.FromMinutes(3),
        Timeframe.M5 => TimeSpan.FromMinutes(5),
        Timeframe.M15 => TimeSpan.FromMinutes(15),
        Timeframe.M30 => TimeSpan.FromMinutes(30),
        Timeframe.H1 => TimeSpan.FromHours(1),
        Timeframe.H4 => TimeSpan.FromHours(4),
        Timeframe.D1 => TimeSpan.FromDays(1),
        Timeframe.W1 => TimeSpan.FromDays(7),
        // MN1 is a calendar month — handled by BarCloseUtc via AddMonths, never as a fixed span.
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unsupported timeframe period."),
    };
}
