namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// The fixed wall-clock duration of one bar at each <see cref="Timeframe"/>. Used to derive a candle's CLOSE time
/// (open + duration) and to bucket candles when resampling a finer timeframe up to a coarser one
/// (<see cref="IctTrader.Domain.Services.CandleAggregator"/>). Monthly has no fixed duration, so it is unsupported.
/// </summary>
public static class TimeframeExtensions
{
    public static TimeSpan ToTimeSpan(this Timeframe timeframe) => timeframe switch
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
        Timeframe.MN1 => throw new NotSupportedException(
            "A calendar month has no fixed duration; MN1 cannot be expressed as a TimeSpan."),
        _ => throw new ArgumentOutOfRangeException(nameof(timeframe), timeframe, "Unknown timeframe."),
    };
}
