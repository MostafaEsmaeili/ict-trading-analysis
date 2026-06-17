using FluentAssertions;
using IctTrader.Domain.Sessions;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Sessions;

/// <summary>
/// Proves NY-session time is correct and host-independent (plan §4.8): the IANA zone resolves, US DST is
/// honored (EDT in summer, EST in winter), the result is identical whatever offset the instant carries,
/// and "now" comes only from the injected <see cref="TimeProvider"/>.
/// </summary>
public class NyClockTests
{
    private static readonly NyClock Clock = new(TimeProvider.System);

    [Fact]
    public void Resolves_the_new_york_zone_via_the_iana_id()
    {
        var act = NyClock.ResolveNewYorkZone;

        act.Should().NotThrow();
        NyClock.ResolveNewYorkZone().Id.Should().Be("America/New_York");
    }

    [Fact]
    public void Converts_summer_utc_to_edt_minus_four()
    {
        // 2024-07-01 12:00 UTC is EDT (UTC-4) in New York -> 08:00 local.
        var utc = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

        Clock.ToNewYork(utc).Offset.Should().Be(TimeSpan.FromHours(-4));
        Clock.NewYorkTimeOfDay(utc).Should().Be(new TimeOnly(8, 0));
        Clock.IsNewYorkDaylightSaving(utc).Should().BeTrue();
    }

    [Fact]
    public void Converts_winter_utc_to_est_minus_five()
    {
        // 2024-01-01 12:00 UTC is EST (UTC-5) in New York -> 07:00 local.
        var utc = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Clock.ToNewYork(utc).Offset.Should().Be(TimeSpan.FromHours(-5));
        Clock.NewYorkTimeOfDay(utc).Should().Be(new TimeOnly(7, 0));
        Clock.IsNewYorkDaylightSaving(utc).Should().BeFalse();
    }

    [Fact]
    public void New_york_time_is_identical_regardless_of_the_instants_offset()
    {
        // The same instant expressed in a Tokyo offset must classify to the same NY wall-clock time,
        // so killzone classification is host/timezone-independent.
        var utc = new DateTimeOffset(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var sameInstantInTokyo = utc.ToOffset(TimeSpan.FromHours(9));

        Clock.NewYorkTimeOfDay(sameInstantInTokyo).Should().Be(Clock.NewYorkTimeOfDay(utc));
        Clock.NewYorkDate(sameInstantInTokyo).Should().Be(Clock.NewYorkDate(utc));
    }

    [Fact]
    public void Utc_now_comes_only_from_the_injected_time_provider()
    {
        var instant = new DateTimeOffset(2024, 3, 10, 6, 30, 0, TimeSpan.Zero);
        var fake = new FakeTimeProvider(instant);

        var clock = new NyClock(fake);

        clock.UtcNow.Should().Be(instant);
        clock.NewYorkNow.Should().Be(clock.ToNewYork(instant));
    }
}
