using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the OPTIONAL <see cref="ConfluenceCondition.MacroTime"/> emitter (0.45). The macro anchors (08:30 / 09:30 /
/// 13:30 / 15:00 NY) are CONFORMANT (§2.5.5/§2.5.8); the window WIDTH (<see cref="MacroTimeOptions.MacroWindowMinutes"/>,
/// default 10) is INVENTED-flagged. It is non-directional (a time gate, like KillzoneEntry): the confirming candle's
/// NY open time must fall within ±window of a macro anchor. NY times are derived through the DST-aware clock.
/// </summary>
public class MacroTimeDetectorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle CandleAt(DateTimeOffset openUtc) =>
        new(Eurusd, Timeframe.M5, openUtc, 1.0800m, 1.0805m, 1.0795m, 1.0800m, 1m);

    private static DetectorResult Detect(MacroTimeOptions options, DateTimeOffset openUtc)
    {
        var ctx = NewContext();
        var candle = CandleAt(openUtc);
        ctx.Append(candle);
        return new MacroTimeDetector(new NyClock(Time), options).Detect(ctx, candle);
    }

    // 2024-07-01 is EDT (UTC-4). 12:30 UTC = 08:30 NY (the macro anchor); 13:30 UTC = 09:30 NY (anchor);
    // 17:30 UTC = 13:30 NY (anchor); 19:00 UTC = 15:00 NY (anchor). 12:35 UTC = 08:35 NY (inside ±10).
    // 12:45 UTC = 08:45 NY (15 min after 08:30, OUTSIDE the ±10 window and not within 09:30). 06:30 UTC = 02:30 NY (no macro).
    private static readonly DateTimeOffset Macro0830 = new(2024, 7, 1, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Macro0930 = new(2024, 7, 1, 13, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Macro1330 = new(2024, 7, 1, 17, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Macro1500 = new(2024, 7, 1, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Inside0830 = new(2024, 7, 1, 12, 35, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Outside = new(2024, 7, 1, 12, 45, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NoMacro = new(2024, 7, 1, 6, 30, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(MacroAnchors))]
    public void Each_macro_anchor_matches_non_directionally(DateTimeOffset anchorUtc)
    {
        var result = Detect(new MacroTimeOptions(), anchorUtc);

        result.Matched.Should().BeTrue();
        result.Direction.Should().BeNull(); // a time gate, not a direction
    }

    public static TheoryData<DateTimeOffset> MacroAnchors() => new() { Macro0830, Macro0930, Macro1330, Macro1500 };

    [Fact]
    public void Inside_the_window_around_an_anchor_matches()
        => Detect(new MacroTimeOptions(), Inside0830).Matched.Should().BeTrue();

    [Fact]
    public void The_boundary_of_the_window_is_inclusive()
    {
        // Exactly +10 minutes from 08:30 (08:40 NY) sits on the inclusive window edge -> matches.
        var edge = new DateTimeOffset(2024, 7, 1, 12, 40, 0, TimeSpan.Zero);
        Detect(new MacroTimeOptions { MacroWindowMinutes = 10 }, edge).Matched.Should().BeTrue();
    }

    [Fact]
    public void Outside_every_macro_window_does_not_match()
        => Detect(new MacroTimeOptions(), Outside).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_time_far_from_any_macro_does_not_match()
        => Detect(new MacroTimeOptions(), NoMacro).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void A_disabled_detector_never_matches()
        => Detect(new MacroTimeOptions { Enabled = false }, Macro0830).Should().Be(DetectorResult.NoMatch);

    [Fact]
    public void Default_configuration_validates_clean()
        => new MacroTimeOptions().Validate().Should().BeEmpty();

    [Fact]
    public void A_non_positive_window_is_rejected()
        => new MacroTimeOptions { MacroWindowMinutes = 0 }.Validate().Should().NotBeEmpty();

    [Fact]
    public void An_empty_anchor_set_falls_back_to_the_default_anchors()
    {
        // Per the binder-append convention an EMPTY configured list means "use the §2.5.5 defaults" — so it is VALID
        // and resolves to the four macro anchors (08:30 / 09:30 / 13:30 / 15:00 NY).
        new MacroTimeOptions { MacroAnchors = [] }.Validate().Should().BeEmpty();
        new MacroTimeOptions().ResolvedMacroAnchors
            .Should().Equal(new TimeOnly(8, 30), new TimeOnly(9, 30), new TimeOnly(13, 30), new TimeOnly(15, 0));
    }

    [Fact]
    public void Configured_anchors_replace_the_default()
    {
        var configured = new[] { new TimeOnly(10, 0) };
        new MacroTimeOptions { MacroAnchors = configured }.ResolvedMacroAnchors.Should().Equal(configured);
    }
}
