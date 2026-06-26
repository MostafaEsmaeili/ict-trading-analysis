using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks FVG-SEM-2b §2/§3.5 — the stacked-farther-bound THREAD: the draw-on-liquidity detector writes
/// <see cref="EvidenceKeys.StackedFartherBound"/> ONLY when <see cref="FvgOptions.StrictFirstFvg"/> is on, the
/// resolved OTE carries a stacked farther gap, AND that far-edge sits beyond the entry on the stop side (the §3.5
/// overlapping-gap guard); it then threads evidence -> <see cref="PricedFrame"/> -> <see cref="Setup"/> ->
/// <see cref="ArmedEntry.IsStacked"/>. The default OFF leaves every new field null and the TradePlan invariant intact.
/// </summary>
public class DrawOnLiquidityStackedThreadingTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static readonly Candle Current = new(Eurusd, Timeframe.M5, Base, 1.0830m, 1.0835m, 1.0825m, 1.0830m, 1m);

    private static DrawOnLiquidityDetector StrictDetector() => new(
        new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions(),
        new FvgOptions { StrictFirstFvg = true }, new SdProjectionOptions());

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    // Stacked bullish frame (entry 1.0830, farther far-edge 1.0822 below entry & below the 1.0827 swept low).
    private static MarketContext ArrangeStacked()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0832m), Base)); // entry 1.0830
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0822m), new Price(1.0826m), Base)); // farther 1.0824, far-edge 1.0822
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0827m, Base, 1));
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0827m), new Price(1.0850m), Base));
        ctx.SetBias(Direction.Bullish);
        ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(1.0920m), 1, Base));
        return ctx;
    }

    // The same frame but a single (non-stacked) entry gap -> no farther bound.
    private static MarketContext ArrangeUnstacked()
    {
        var ctx = NewContext();
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), Base));
        ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0832m), Base)); // entry 1.0830 only
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0810m, Base, 1));
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), Base));
        ctx.SetBias(Direction.Bullish);
        ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(1.0920m), 1, Base));
        return ctx;
    }

    [Fact]
    public void A_genuinely_deeper_stacked_selection_writes_the_farther_bound_evidence()
    {
        // §6(13) positive branch: a real deeper far-edge (1.0822 < entry 1.0830) -> evidence written.
        var result = StrictDetector().Detect(ArrangeStacked(), Current);

        result.Matched.Should().BeTrue();
        result.Evidence!.Should().ContainKey(EvidenceKeys.StackedFartherBound);
        ((decimal)result.Evidence[EvidenceKeys.StackedFartherBound]).Should().Be(1.0822m);
    }

    [Fact]
    public void A_non_stacked_selection_writes_no_farther_bound_evidence()
    {
        // §6(13) negative branch: a single entry gap -> no farther bound -> no spurious stacking carried.
        var result = StrictDetector().Detect(ArrangeUnstacked(), Current);

        result.Matched.Should().BeTrue();
        result.Evidence!.Should().NotContainKey(EvidenceKeys.StackedFartherBound);
    }

    [Fact]
    public void The_farther_bound_threads_through_priced_frame_setup_and_armed_entry()
    {
        // §6(6): evidence -> PricedFrame -> Setup.StackedFartherBound -> ArmedEntry.IsStacked == true.
        var result = StrictDetector().Detect(ArrangeStacked(), Current);

        var frame = PricedFrame.TryFromEvidence(Direction.Bullish, result.Evidence);
        frame.Should().NotBeNull();
        frame!.Value.StackedFartherBound.Should().Be(1.0822m);

        var confirmation = new SetupConfirmation(
            Eurusd, Direction.Bullish, Timeframe.M5, SetupGrade.B, 70, Base,
            [new ConfluenceContribution(ConfluenceCondition.DrawTargetRrMet, Direction.Bullish, 1.0920m, "draw")],
            frame);
        var setup = new SetupFactory(new TargetLadderOptions(), new TradeStyleOptions())
            .Create(confirmation, TradeStyle.Intraday);

        setup.StackedFartherBound.Should().Be(1.0822m);

        var account = new PaperAccount(Guid.NewGuid(), new Money(10_000m), 5m);
        var armed = new PaperTradeFactory(new RiskOptions(), new RiskManager())
            .Arm(setup, account, Spec, Contract, Base);

        armed.IsStacked.Should().BeTrue();
        armed.StackedFartherBound.Should().Be(1.0822m);
    }

    [Fact]
    public void Default_off_threads_a_null_farther_bound_and_leaves_the_trade_plan_intact()
    {
        // §6(7): the unstacked frame (no evidence key) -> every new field null; the priced ladder is the byte-identical
        // two tiers (stop < entry < T1 < T2 order invariant untouched — the farther bound is NOT a tier).
        var result = StrictDetector().Detect(ArrangeUnstacked(), Current);
        var frame = PricedFrame.TryFromEvidence(Direction.Bullish, result.Evidence);

        frame!.Value.StackedFartherBound.Should().BeNull();

        var confirmation = new SetupConfirmation(
            Eurusd, Direction.Bullish, Timeframe.M5, SetupGrade.B, 70, Base,
            [new ConfluenceContribution(ConfluenceCondition.DrawTargetRrMet, Direction.Bullish, 1.0920m, "draw")],
            frame);
        var setup = new SetupFactory(new TargetLadderOptions(), new TradeStyleOptions())
            .Create(confirmation, TradeStyle.Intraday);

        setup.StackedFartherBound.Should().BeNull();
        setup.Plan.Targets.TierCount.Should().Be(2);                       // T1 + draw, no extra tier
        setup.Plan.Stop.Value.Should().BeLessThan(setup.Plan.Entry.Value); // order invariant holds

        var account = new PaperAccount(Guid.NewGuid(), new Money(10_000m), 5m);
        var armed = new PaperTradeFactory(new RiskOptions(), new RiskManager())
            .Arm(setup, account, Spec, Contract, Base);

        armed.IsStacked.Should().BeFalse();
        armed.StackedFartherBound.Should().BeNull();
    }
}
