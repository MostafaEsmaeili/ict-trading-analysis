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

    // ---- Silver-Bullet macro overlay (opt-in, narrows the KillzoneEntry gate) --------------------------------

    private static DetectorResult DetectSb(KillzoneEntryOptions options, SilverBulletOptions sb, DateTimeOffset openUtc)
    {
        var ctx = NewContext();
        var candle = CandleAt(openUtc);
        ctx.Append(candle);
        return new KillzoneEntryDetector(options, sb).Detect(ctx, candle);
    }

    // EDT (UTC-4): 14:30 UTC = 10:30 NY (inside the 10:00–11:00 Silver Bullet, classifies as LondonClose for FX);
    // 14:00 UTC = 10:00 NY (inclusive macro start); 12:30 UTC = 08:30 NY (inside NewYorkOpen, OUTSIDE the macro).
    private static readonly DateTimeOffset SbMacro1030 = new(2024, 7, 1, 14, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SbMacroStart1000 = new(2024, 7, 1, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NyOpen0830 = new(2024, 7, 1, 12, 30, 0, TimeSpan.Zero);

    private static readonly SilverBulletOptions SbOn = new() { Enabled = true };
    private static readonly KillzoneEntryOptions WithLondonClose =
        new() { ActiveKillzones = [Killzone.LondonOpen, Killzone.NewYorkOpen, Killzone.LondonClose] };

    [Fact]
    public void Silver_bullet_defaults_are_off_and_the_canonical_10_to_11_macro()
    {
        var defaults = new SilverBulletOptions();
        defaults.Enabled.Should().BeFalse();
        defaults.ResolvedMacroWindows.Should().ContainSingle()
            .Which.Should().Be(new SessionWindow(new TimeOnly(10, 0), new TimeOnly(11, 0)));
        defaults.Validate().Should().BeEmpty();
        new SilverBulletOptions { MacroWindows = null! }.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void Silver_bullet_exposes_the_three_named_macros_and_the_default_uses_the_ny_am_window()
    {
        SilverBulletOptions.LondonMacro.Should().Be(new SessionWindow(new TimeOnly(3, 0), new TimeOnly(4, 0)));
        SilverBulletOptions.NewYorkAmMacro.Should().Be(new SessionWindow(new TimeOnly(10, 0), new TimeOnly(11, 0)));
        SilverBulletOptions.NewYorkPmMacro.Should().Be(new SessionWindow(new TimeOnly(14, 0), new TimeOnly(15, 0)));
        SilverBulletOptions.AllMacroWindows.Should().Equal(
            SilverBulletOptions.LondonMacro, SilverBulletOptions.NewYorkAmMacro, SilverBulletOptions.NewYorkPmMacro);
        new SilverBulletOptions().ResolvedMacroWindows.Should().ContainSingle()
            .Which.Should().Be(SilverBulletOptions.NewYorkAmMacro);
    }

    [Fact]
    public void On_inside_the_ny_pm_macro_with_pm_active_matches()
    {
        // The faithful "third daily shot": 14:30 NY is inside NewYorkPmMacro AND the FX Pm killzone — with Pm in the
        // hunt-set the overlay confirms it (a 14:30 NY candle is 18:30 UTC in summer DST).
        var sb = new SilverBulletOptions { Enabled = true, MacroWindows = [SilverBulletOptions.NewYorkPmMacro] };
        var withPm = new KillzoneEntryOptions
        {
            ActiveKillzones = [Killzone.LondonOpen, Killzone.NewYorkOpen, Killzone.Pm],
        };
        DetectSb(withPm, sb, new DateTimeOffset(2024, 7, 1, 18, 30, 0, TimeSpan.Zero)).Matched.Should().BeTrue();
    }

    [Fact]
    public void Off_by_default_the_overlay_is_a_no_op()
        // SB disabled: an in-killzone-but-off-macro candle (08:30 NY, NewYorkOpen) still matches — byte-identical.
        => DetectSb(new KillzoneEntryOptions(), new SilverBulletOptions(), NyOpen0830).Matched.Should().BeTrue();

    [Fact]
    public void On_inside_the_macro_and_an_active_killzone_matches()
    {
        var result = DetectSb(WithLondonClose, SbOn, SbMacro1030);

        result.Matched.Should().BeTrue();
        result.Evidence.Should().ContainKey(EvidenceKeys.SilverBulletMacro);
    }

    [Fact]
    public void On_the_inclusive_macro_start_matches()
        => DetectSb(WithLondonClose, SbOn, SbMacroStart1000).Matched.Should().BeTrue();

    [Fact]
    public void On_inside_an_active_killzone_but_outside_the_macro_is_withheld()
        // 08:30 NY is inside the default NewYorkOpen killzone but OUTSIDE the 10–11 macro -> the overlay NARROWS it out.
        => DetectSb(new KillzoneEntryOptions(), SbOn, NyOpen0830).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void On_the_overlay_cannot_bypass_the_killzone_hunt_set()
        // 10:30 NY is in the macro AND in LondonClose, but LondonClose is NOT in the default active set -> still NoMatch
        // (AND-semantics: the SB narrows an active killzone, it never opens a disabled one).
        => DetectSb(new KillzoneEntryOptions(), SbOn, SbMacro1030).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void On_with_multiple_macros_matches_inside_any_of_them()
    {
        var sb = new SilverBulletOptions
        {
            Enabled = true,
            MacroWindows =
            [
                new SessionWindow(new TimeOnly(3, 0), new TimeOnly(4, 0)),
                new SessionWindow(new TimeOnly(10, 0), new TimeOnly(11, 0)),
                new SessionWindow(new TimeOnly(14, 0), new TimeOnly(15, 0)),
            ],
        };

        DetectSb(WithLondonClose, sb, SbMacro1030).Matched.Should().BeTrue();
    }
}
