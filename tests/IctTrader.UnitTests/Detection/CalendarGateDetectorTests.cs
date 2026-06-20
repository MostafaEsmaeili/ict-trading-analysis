using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the §2.5.2 calendar no-trade gate: CalendarClear is emitted only when the current New-York date is not
/// blocked — the FOMC day and the day after (post-FOMC), and the NFP week from Wednesday through Friday. An
/// unloaded calendar is config-gated (fail-open by default). July 2024: NFP falls on Friday the 5th.
/// </summary>
public class CalendarGateDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero));

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    // 12:00 UTC = 08:00 NY (EDT), so the NY date equals the UTC day in July.
    private static Candle Candle(int day)
        => new(Eurusd, Timeframe.M5, new DateTimeOffset(2024, 7, day, 12, 0, 0, TimeSpan.Zero),
            1.0850m, 1.0855m, 1.0845m, 1.0850m, 1m);

    private static readonly CalendarGateDetector Detector = new(new CalendarOptions());

    private static bool DetectIsClear(MarketContext ctx, Candle candle)
    {
        ctx.Append(candle);
        return Detector.Detect(ctx, candle).Matched;
    }

    [Theory]
    [InlineData(1, false)] // FOMC announcement day (knee-jerk) -> blocked
    [InlineData(2, false)] // post-FOMC (1 day after) -> blocked
    [InlineData(3, true)]  // 2 days after -> clear
    public void Fomc_blocks_the_announcement_day_and_the_day_after(int day, bool clear)
    {
        var ctx = NewContext();
        ctx.LoadCalendar([new EconomicEvent(new DateOnly(2024, 7, 1), CalendarEventType.Fomc)]);

        DetectIsClear(ctx, Candle(day)).Should().Be(clear);
    }

    [Theory]
    [InlineData(2, true)]  // Tue, 3 days before NFP -> clear
    [InlineData(3, false)] // Wed -> blocked
    [InlineData(4, false)] // Thu -> blocked
    [InlineData(5, false)] // Fri (NFP release) -> blocked
    [InlineData(8, true)]  // following Mon -> clear
    public void Nfp_week_blocks_from_wednesday_through_friday(int day, bool clear)
    {
        var ctx = NewContext();
        ctx.LoadCalendar([new EconomicEvent(new DateOnly(2024, 7, 5), CalendarEventType.Nfp)]);

        DetectIsClear(ctx, Candle(day)).Should().Be(clear);
    }

    [Fact]
    public void A_day_with_no_blocking_event_is_calendar_clear()
    {
        var ctx = NewContext();
        ctx.LoadCalendar([new EconomicEvent(new DateOnly(2024, 7, 5), CalendarEventType.Nfp)]);

        DetectIsClear(ctx, Candle(1)).Should().BeTrue(); // Mon, well before NFP week
    }

    [Fact]
    public void An_unloaded_calendar_is_clear_by_default_fail_open()
    {
        var ctx = NewContext();

        DetectIsClear(ctx, Candle(1)).Should().BeTrue();
    }

    [Fact]
    public void An_unloaded_calendar_blocks_when_configured_fail_closed()
    {
        var detector = new CalendarGateDetector(new CalendarOptions { BlockWhenCalendarUnavailable = true });
        var ctx = NewContext();
        var candle = Candle(1);
        ctx.Append(candle);

        detector.Detect(ctx, candle).Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new CalendarOptions().Validate().Should().BeEmpty();
}
