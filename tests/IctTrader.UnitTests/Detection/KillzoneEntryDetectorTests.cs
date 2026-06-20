using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the killzone time gate (plan §2.5.1 step 3, §4.6): the §2.5.2 KillzoneEntry RequiredCondition matches
/// non-directionally only inside an operator-ENABLED killzone, never in dead time, a disabled killzone, or the
/// hard lunch window. NY times are derived through the DST-aware clock, so the UTC inputs map to NY wall-clock.
/// </summary>
public class KillzoneEntryDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle CandleAt(DateTimeOffset openUtc) =>
        new(Eurusd, Timeframe.M5, openUtc, 1.0800m, 1.0805m, 1.0795m, 1.0800m, 1m);

    private static DetectorResult Detect(KillzoneEntryOptions options, DateTimeOffset openUtc)
    {
        var ctx = NewContext();
        var candle = CandleAt(openUtc);
        ctx.Append(candle);
        return new KillzoneEntryDetector(options).Detect(ctx, candle);
    }

    // 2024-07-01 is EDT (UTC-4): 06:30 UTC = 02:30 NY (London Open); 09:30 UTC = 05:30 NY (dead time);
    // 00:00 UTC = 20:00 NY (Asian); 16:30 UTC = 12:30 NY (hard lunch).
    private static readonly DateTimeOffset LondonOpen = new(2024, 7, 1, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeadTime = new(2024, 7, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Asian = new(2024, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Lunch = new(2024, 7, 1, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public void An_enabled_killzone_matches_non_directionally()
    {
        var result = Detect(new KillzoneEntryOptions(), LondonOpen);

        result.Matched.Should().BeTrue();
        result.Direction.Should().BeNull(); // a time gate, not a direction
    }

    [Fact]
    public void Dead_time_does_not_match()
        => Detect(new KillzoneEntryOptions(), DeadTime).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_killzone_outside_the_enabled_set_does_not_match()
        => Detect(new KillzoneEntryOptions(), Asian).Should().Be(DetectorResult.NoMatch); // Asian off by default

    [Fact]
    public void The_hard_lunch_window_does_not_match()
        => Detect(new KillzoneEntryOptions(), Lunch).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Enabling_a_killzone_lets_it_match()
        => Detect(new KillzoneEntryOptions { ActiveKillzones = [Killzone.Asian] }, Asian)
            .Matched.Should().BeTrue();

    [Fact]
    public void Default_configuration_validates_clean()
        => new KillzoneEntryOptions().Validate().Should().BeEmpty();
}
