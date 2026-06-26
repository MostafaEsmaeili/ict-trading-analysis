using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks the TGR-1/2 SD-projection resolver (decisions register TGR-1/2; plan §2.4/§2.5.10 −1/−1.5/−2 SD
/// targets). The resolver is PURE and NON-scoring: it reads only <c>ctx.LastDisplacement</c> and prices each
/// SD tier by projecting the leg magnitude beyond the terminus in the draw direction
/// (<c>Terminus + s × n × legLength</c>). It is provably the SAME axis as the OTE fib — both go through
/// <c>Displacement.Project</c> — so the SD targets and the OTE entry can never drift (the single-source invariant).
/// </summary>
public class SdProjectionResolverTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    // A bullish leg 1.0800 -> 1.0900: legLength 0.0100, terminus 1.0900.
    private static Displacement BullishLeg() =>
        new(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base);

    // A bearish leg 1.0900 -> 1.0800: legLength 0.0100, terminus 1.0800.
    private static Displacement BearishLeg() =>
        new(Direction.Bearish, Timeframe.M5, new Price(1.0900m), new Price(1.0800m), Base);

    [Fact]
    public void Bullish_tiers_project_the_leg_beyond_the_terminus_in_exact_decimals()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());

        var projection = SdProjectionResolver.Resolve(ctx, new SdProjectionOptions());

        projection.Should().NotBeNull();
        projection!.Value.Direction.Should().Be(Direction.Bullish);
        projection.Value.LegLength.Should().Be(0.0100m);
        projection.Value.Terminus.Should().Be(1.0900m);
        // Terminus + n * legLength: -1 -> 1.1000, -1.5 -> 1.1050, -2 -> 1.1100.
        projection.Value.Tiers.Select(t => (t.Multiple, t.Price)).Should().Equal(
            (1.0m, 1.1000m), (1.5m, 1.1050m), (2.0m, 1.1100m));
    }

    [Fact]
    public void Bearish_tiers_project_below_the_terminus_in_exact_decimals()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(BearishLeg());

        var projection = SdProjectionResolver.Resolve(ctx, new SdProjectionOptions());

        projection.Should().NotBeNull();
        projection!.Value.Direction.Should().Be(Direction.Bearish);
        // Terminus - n * legLength: -1 -> 1.0700, -1.5 -> 1.0650, -2 -> 1.0600.
        projection.Value.Tiers.Select(t => (t.Multiple, t.Price)).Should().Equal(
            (1.0m, 1.0700m), (1.5m, 1.0650m), (2.0m, 1.0600m));
    }

    [Fact]
    public void The_sd_axis_is_the_same_method_as_the_ote_axis()
    {
        // Single-source invariant (TGR-2): the SD tier price MUST equal leg.Project(-multiple), the same
        // method OteEntryResolver retraces on — so the SD targets and the OTE entry cannot drift.
        var leg = BullishLeg();
        var ctx = NewContext();
        ctx.SetDisplacement(leg);

        var projection = SdProjectionResolver.Resolve(ctx, new SdProjectionOptions());

        foreach (var tier in projection!.Value.Tiers)
        {
            tier.Price.Should().Be(leg.Project(-tier.Multiple));
        }
    }

    [Fact]
    public void Tiers_are_ordered_shallow_to_deep_in_the_draw_direction()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());

        var projection = SdProjectionResolver.Resolve(ctx, new SdProjectionOptions());

        projection!.Value.Tiers.Select(t => t.Price).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Without_a_displacement_leg_there_is_no_projection()
    {
        var ctx = NewContext();

        SdProjectionResolver.Resolve(ctx, new SdProjectionOptions()).Should().BeNull();
    }

    [Fact]
    public void A_fully_retraced_leg_voids_the_projection()
    {
        var ctx = NewContext();
        var leg = BullishLeg();
        leg.MarkRetraced();
        ctx.SetDisplacement(leg);

        SdProjectionResolver.Resolve(ctx, new SdProjectionOptions()).Should().BeNull();
    }

    [Fact]
    public void A_zero_length_leg_voids_the_projection()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(
            Direction.Bullish, Timeframe.M5, new Price(1.0850m), new Price(1.0850m), Base));

        SdProjectionResolver.Resolve(ctx, new SdProjectionOptions()).Should().BeNull();
    }

    [Fact]
    public void An_identical_leg_yields_a_field_equal_projection()
    {
        var ctxA = NewContext();
        ctxA.SetDisplacement(BullishLeg());
        var ctxB = NewContext();
        ctxB.SetDisplacement(BullishLeg());

        var a = SdProjectionResolver.Resolve(ctxA, new SdProjectionOptions());
        var b = SdProjectionResolver.Resolve(ctxB, new SdProjectionOptions());

        a!.Value.Direction.Should().Be(b!.Value.Direction);
        a.Value.LegLength.Should().Be(b.Value.LegLength);
        a.Value.Terminus.Should().Be(b.Value.Terminus);
        a.Value.Tiers.Should().Equal(b.Value.Tiers);
    }

    [Fact]
    public void The_negative_fib_variant_replaces_the_multiples_with_its_coefficients()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(BullishLeg());
        var options = new SdProjectionOptions
        {
            NegativeFibVariant = new NegativeFibOptions { Enabled = true, Coefficients = [0.27m, 0.62m, 1.0m] },
        };

        var projection = SdProjectionResolver.Resolve(ctx, options);

        // The negative-fib coefficients REPLACE the SD multiples on the same terminus axis (leg.Project(-c)).
        projection!.Value.Tiers.Select(t => (t.Multiple, t.Price)).Should().Equal(
            (0.27m, 1.0927m), (0.62m, 1.0962m), (1.0m, 1.1000m));
    }
}
