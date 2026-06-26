using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks EG-3 v1 — the close-proximity FILL PRICE on <see cref="EntryFillEvaluator"/> (Ep10/29/07/22/35). With
/// <see cref="EntryManagementOptions.UseCloseProximityEntry"/> OFF (default) the fill is byte-identical at
/// <c>Plan.Entry</c>. With it ON the recorded fill price is the touched price clamped to a small entry-anchored band
/// (<see cref="EntryManagementOptions.CloseProximityTolerancePips"/>). CRITICAL: the trade still OPENS at
/// <c>Plan.Entry</c> (OpenArmed ignores the action fill price), so the frozen-1R invariant holds — an EG-3 trade
/// stopped out still books exactly −1R. EG-3 v1 changes only the diagnostic recorded fill price.
/// </summary>
public class EntryFillEvaluatorCloseProximityTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static EntryFillEvaluator Off() => new(new EntryManagementOptions());
    private static EntryFillEvaluator On(decimal tolPips = 2m) =>
        new(new EntryManagementOptions { UseCloseProximityEntry = true, CloseProximityTolerancePips = tolPips });

    // Long: entry 1.0832 (the OTE/FVG limit, in discount), stop 1.0800 (32-pip 1R), T1 1.0876, runner 1.0920.
    private static Setup BullishSetup()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("Daily bias Bullish; sweep; MSS; FVG; OTE"), Utc);
    }

    // Short mirror: entry 1.0870 (in premium), stop 1.0900, T1 1.0840, runner 1.0790.
    private static Setup BearishSetup()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0840m), new Price(1.0790m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("Daily bias Bearish; sweep; MSS; FVG; OTE"), Utc);
    }

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, Utc, open, high, low, close, 1_000m);

    [Fact]
    public void With_close_proximity_off_the_long_fill_is_the_plan_entry()
    {
        // §6(15): OFF -> byte-identical, fills at the limit level even on a deep bar.
        var decision = Off().Evaluate(BullishSetup(), Bar(1.0820m, 1.0825m, 1.0810m, 1.0815m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0832m);
    }

    [Fact]
    public void With_close_proximity_on_a_long_low_within_tolerance_records_the_touched_price()
    {
        // §6(16): Low 1.0831 is within 2p of entry 1.0832 -> recorded fill == the touched Low.
        var decision = On().Evaluate(BullishSetup(), Bar(1.0834m, 1.0836m, 1.0831m, 1.0833m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0831m); // candle.Low, within the band
    }

    [Fact]
    public void With_close_proximity_on_a_long_low_beyond_tolerance_clamps_to_the_band_edge()
    {
        // §6(17): Low 1.0810 is 22p below entry -> clamp to entry - tol = 1.0832 - 0.0002 = 1.0830.
        var decision = On().Evaluate(BullishSetup(), Bar(1.0820m, 1.0825m, 1.0810m, 1.0815m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0830m); // clamped band edge
    }

    [Fact]
    public void With_close_proximity_on_a_short_high_beyond_tolerance_clamps_to_the_band_edge_bearish_mirror()
    {
        // §6(18): High 1.0895 is 25p above entry 1.0870 -> clamp to entry + tol = 1.0870 + 0.0002 = 1.0872.
        var decision = On().Evaluate(BearishSetup(), Bar(1.0880m, 1.0895m, 1.0875m, 1.0885m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0872m);
    }

    [Fact]
    public void With_close_proximity_on_a_short_high_within_tolerance_records_the_touched_price()
    {
        // Short mirror of §6(16): High 1.0871 within 2p of entry 1.0870 -> recorded fill == the touched High.
        var decision = On().Evaluate(BearishSetup(), Bar(1.0868m, 1.0871m, 1.0866m, 1.0869m));

        decision.IsFilled.Should().BeTrue();
        decision.FillPrice!.Value.Value.Should().Be(1.0871m);
    }

    [Fact]
    public void A_close_proximity_filled_trade_stopped_out_books_exactly_minus_one_R()
    {
        // §6(19) — the load-bearing frozen-R lock. EG-3 records a different FILL PRICE, but OpenArmed opens the trade
        // at Plan.Entry, so InitialRiskPerUnit/RiskBudget are vs the original 1R. A stop-out books exactly −1R gross.
        var account = new PaperAccount(Guid.NewGuid(), new Money(10_000m), 5m);
        var factory = new PaperTradeFactory(new RiskOptions(), new RiskManager());
        var armed = factory.Arm(BullishSetup(), account, Spec, Contract, Utc);

        // The bar trades deep (Low 1.0810): EG-3 records the clamped fill price 1.0830, but the OPEN is at Plan.Entry.
        var fill = On().Evaluate(armed.Setup, Bar(1.0820m, 1.0825m, 1.0810m, 1.0815m));
        fill.FillPrice!.Value.Value.Should().Be(1.0830m); // the EG-3 diagnostic fill price...

        var trade = factory.OpenArmed(armed, account, Utc.AddMinutes(5));
        trade.Entry.Value.Should().Be(1.0832m); // ...but the trade OPENS at Plan.Entry — frozen-1R preserved

        trade.Close(new Price(1.0800m), TradeCloseReason.StopHit, TradeCosts.Zero, Utc.AddMinutes(10));
        trade.RealizedR!.Value.Should().BeApproximately(-1m, 0.0001m); // exactly −1R
    }
}
