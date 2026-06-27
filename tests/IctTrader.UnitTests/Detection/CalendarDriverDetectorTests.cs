using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the OPTIONAL <see cref="ConfluenceCondition.CalendarDriver"/> emitter (0.35, decision TGR-3): a same-NY-day
/// economic DRIVER event gives the session a reason to move, so it is scoring confluence — but ONLY when that day is
/// NOT in the hard-gate blackout the <see cref="CalendarGateDetector"/> already vetoes (the release minute stays
/// blocked separately by the required <see cref="ConfluenceCondition.CalendarClear"/>). It fails OPEN (no match, not an
/// error) when the calendar is unloaded, mirroring the gate. Non-directional (a driver does not set a side).
/// </summary>
public class CalendarDriverDetectorTests
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

    private static DetectorResult Detect(
        IEnumerable<EconomicEvent>? calendar, int day,
        CalendarDriverOptions? driverOptions = null, CalendarOptions? gateOptions = null)
    {
        var ctx = NewContext();
        if (calendar is not null)
        {
            ctx.LoadCalendar(calendar);
        }

        var candle = Candle(day);
        ctx.Append(candle);
        return new CalendarDriverDetector(driverOptions ?? new CalendarDriverOptions(), gateOptions ?? new CalendarOptions())
            .Detect(ctx, candle);
    }

    [Fact]
    public void A_same_day_driver_that_is_not_in_the_blackout_matches_non_directionally()
    {
        // CPI on the SAME NY day, and CPI is not a blackout type (only FOMC/NFP black out) -> a clean driver day.
        var result = Detect([new EconomicEvent(new DateOnly(2024, 7, 1), CalendarEventType.Cpi)], day: 1);

        result.Matched.Should().BeTrue();
        result.Direction.Should().BeNull(); // a driver gives a reason to move, not a side (TGR-3)
    }

    [Fact]
    public void A_driver_in_the_hard_gate_blackout_does_not_match()
    {
        // NFP on the SAME day (Friday) — the release-minute blackout the CalendarGateDetector vetoes — so the driver
        // confluence is withheld; the day's no-trade is owned by the required CalendarClear, not double-counted here.
        Detect([new EconomicEvent(new DateOnly(2024, 7, 5), CalendarEventType.Nfp)], day: 5)
            .Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void A_driver_on_a_different_day_does_not_match()
        // A CPI two days away is not THIS session's driver.
        => Detect([new EconomicEvent(new DateOnly(2024, 7, 3), CalendarEventType.Cpi)], day: 1)
            .Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void An_nfp_driver_outside_the_blackout_window_still_matches()
    {
        // The same NFP, but read on the Monday BEFORE its week — outside the Wed→Fri blackout — is a (distant) driver
        // event on a non-blocked day... but it is not the SAME NY day, so it must NOT match (driver is same-day only).
        Detect([new EconomicEvent(new DateOnly(2024, 7, 5), CalendarEventType.Nfp)], day: 1)
            .Should().Be(DetectorResult.NoMatch);
    }

    [Fact]
    public void An_unloaded_calendar_fails_open_with_no_match()
        => Detect(calendar: null, day: 1).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_disabled_detector_never_matches()
        => Detect([new EconomicEvent(new DateOnly(2024, 7, 1), CalendarEventType.Cpi)], day: 1,
                driverOptions: new CalendarDriverOptions { Enabled = false })
            .Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Other_typed_events_are_drivers_only_when_configured()
    {
        // The "Other" bucket is not a driver by default; enabling it lets a same-day Other event count.
        Detect([new EconomicEvent(new DateOnly(2024, 7, 1), CalendarEventType.Other)], day: 1)
            .Should().Be(DetectorResult.NoMatch);
        Detect([new EconomicEvent(new DateOnly(2024, 7, 1), CalendarEventType.Other)], day: 1,
                driverOptions: new CalendarDriverOptions { DriverEventTypes = [CalendarEventType.Other] })
            .Matched.Should().BeTrue();
    }

    [Fact]
    public void Default_configuration_validates_clean()
        => new CalendarDriverOptions().Validate().Should().BeEmpty();
}
