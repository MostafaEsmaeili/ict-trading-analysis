using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.Scanning.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks <see cref="GeometryOverlayMapper"/> — the live "engine view" projection of the scanner's
/// <see cref="MarketContext"/> into the chart's concept overlays (plan §9.1). It must surface each tracked concept
/// (FVG / OB / liquidity / sweep / MSS / OTE) with the correct geometry, derive the OTE band off the SAME
/// <see cref="Displacement.Project"/> axis the OTE detector uses, cap the many-instance layers newest-first, and
/// prioritise still-untapped liquidity. Pure read — it never mutates the context.
/// </summary>
public class GeometryOverlayMapperTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static IReadOnlyList<GeometryOverlayDto> Snapshot(MarketContext ctx) =>
        GeometryOverlayMapper.Snapshot(
            ctx, orderBlockMeanPercent: 0.50m, oteLowerFib: 0.62m, oteUpperFib: 0.79m, oteSweetSpotFib: 0.705m);

    [Fact]
    public void Empty_context_snapshots_no_overlays()
    {
        Snapshot(NewContext()).Should().BeEmpty();
    }

    [Fact]
    public void Maps_each_tracked_concept_to_its_overlay()
    {
        var ctx = NewContext();
        // Bullish leg 1.0800 -> 1.0900 (Origin 1.0800, Terminus 1.0900).
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0832m), Base));
        // Bullish OB: open == bodyHigh (anchor's a down candle); mean(0.5) = (bodyLow 1.0695 + bodyHigh 1.0705) / 2.
        ctx.RegisterOrderBlock(new OrderBlock(
            Direction.Bullish, Timeframe.M5,
            open: new Price(1.0705m), high: new Price(1.0708m), low: new Price(1.0692m),
            bodyLow: new Price(1.0695m), bodyHigh: new Price(1.0705m), formedAtUtc: Base));
        ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(1.0920m), strength: 2, Base));
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0790m, Base, 1));
        ctx.SetMarketStructureShift(new MarketStructureShift(
            Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), Base));

        var overlays = Snapshot(ctx);

        var fvg = overlays.Should().ContainSingle(o => o.Kind == "fvg").Subject;
        fvg.Top.Should().Be(1.0832m);
        fvg.Bottom.Should().Be(1.0828m);
        fvg.State.Should().Be("Open");
        fvg.Direction.Should().Be("Bullish");

        var ob = overlays.Should().ContainSingle(o => o.Kind == "orderBlock").Subject;
        ob.Top.Should().Be(1.0708m);
        ob.Bottom.Should().Be(1.0692m);
        ob.Mid.Should().Be(1.0700m);

        var liq = overlays.Should().ContainSingle(o => o.Kind == "liquidity").Subject;
        liq.Price.Should().Be(1.0920m);
        liq.Side.Should().Be("BuySide");
        liq.Swept.Should().BeFalse();
        liq.Strength.Should().Be(2);

        overlays.Should().ContainSingle(o => o.Kind == "sweep").Which.Price.Should().Be(1.0790m);
        overlays.Should().ContainSingle(o => o.Kind == "mss").Which.Price.Should().Be(1.0810m);

        // OTE band off the leg's own Project axis: 62% = 1.09 - 0.0062, 79% = 1.09 - 0.0079, 70.5% = 1.09 - 0.00705.
        var ote = overlays.Should().ContainSingle(o => o.Kind == "ote").Subject;
        ote.Top.Should().Be(1.0838m);
        ote.Bottom.Should().Be(1.0821m);
        ote.Mid.Should().Be(1.08295m);
    }

    [Fact]
    public void Fvgs_are_capped_newest_first()
    {
        var ctx = NewContext();
        for (var i = 0; i < GeometryOverlayMapper.MaxFvgs + 3; i++)
        {
            var bottom = 1.0800m + (i * 0.0010m);
            ctx.RegisterFvg(new FairValueGap(
                Direction.Bullish, Timeframe.M5, new Price(bottom), new Price(bottom + 0.0004m), Base.AddMinutes(i)));
        }

        var fvgs = Snapshot(ctx).Where(o => o.Kind == "fvg").ToList();

        fvgs.Should().HaveCount(GeometryOverlayMapper.MaxFvgs);
        // Newest-first: the last-registered gap (highest bottom) leads.
        fvgs[0].Bottom.Should().Be(1.0800m + ((GeometryOverlayMapper.MaxFvgs + 2) * 0.0010m));
    }

    [Fact]
    public void Liquidity_prioritises_untapped_then_consumed()
    {
        var ctx = NewContext();
        var swept = new LiquidityPool(LiquiditySide.SellSide, new Price(1.0700m), 1, Base);
        swept.MarkSwept();
        ctx.RegisterLiquidityPool(swept);
        ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(1.0900m), 1, Base));

        var liq = Snapshot(ctx).Where(o => o.Kind == "liquidity").ToList();

        liq.Should().HaveCount(2);
        liq[0].Swept.Should().BeFalse();
        liq[1].Swept.Should().BeTrue();
    }
}
