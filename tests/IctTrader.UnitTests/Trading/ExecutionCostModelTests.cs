using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §5.4 execution-cost model (this slice: round-trip spread + commission). Spread is charged on BOTH
/// legs (entries cross the spread, §5.4); commission is a round-turn per-lot charge levied once; both read the
/// trade's own money geometry so a cost can never disagree with the booked P&amp;L; zero-cost config is a no-op.
/// </summary>
public class ExecutionCostModelTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static PaperTrade Trade(decimal lots)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new PaperTrade(
            Guid.NewGuid(), Guid.NewGuid(), Eurusd, TradeStyle.Intraday, Timeframe.M5,
            plan, new PositionSize(lots), pipSize: 0.0001m, valuePerPip: 10m, Utc);
    }

    [Fact]
    public void Computes_round_trip_spread_and_commission_from_the_trade_geometry()
    {
        var model = new ExecutionCostModel(new ExecutionCostOptions());

        var costs = model.Compute(Trade(0.31m)); // value-per-pip for the position = 10 * 0.31 = 3.1

        costs.SpreadCost.Amount.Should().Be(4.34m); // 2 legs * 0.7 pips * 3.1 per pip
        costs.Commission.Amount.Should().Be(1.86m); // 6.0 per lot * 0.31 lots (round-turn, levied once)
        costs.Total.Amount.Should().Be(6.2m);
    }

    [Fact]
    public void Commission_scales_with_lots_not_legs()
    {
        var model = new ExecutionCostModel(new ExecutionCostOptions());

        model.Compute(Trade(0.1m)).Commission.Amount.Should().Be(0.6m); // 6.0 * 0.1, not doubled for two legs
    }

    [Fact]
    public void Zero_cost_options_produce_no_cost()
    {
        var model = new ExecutionCostModel(new ExecutionCostOptions
        {
            Spread = new SpreadOptions { BasePips = 0m },
            Commission = new CommissionOptions { PerLotRoundTripUsd = 0m },
        });

        model.Compute(Trade(0.31m)).Total.Amount.Should().Be(0m);
    }

    [Fact]
    public void A_null_trade_or_null_options_is_rejected()
    {
        var model = new ExecutionCostModel(new ExecutionCostOptions());

        var nullTrade = () => model.Compute(null!);
        nullTrade.Should().Throw<ArgumentNullException>();

        var nullOptions = () => new ExecutionCostModel(null!);
        nullOptions.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void A_negative_cost_component_is_rejected()
    {
        var act = () => new TradeCosts(new Money(-1m), Money.Zero);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Entry_and_full_exit_legs_sum_to_the_round_trip()
    {
        var model = new ExecutionCostModel(new ExecutionCostOptions());
        var trade = Trade(0.31m);

        var entry = model.ComputeEntryLeg(trade);
        var exit = model.ComputeExitLeg(trade, trade.Size);
        var roundTrip = model.Compute(trade);

        (entry.Total.Amount + exit.Total.Amount).Should().Be(roundTrip.Total.Amount); // no double-count
        roundTrip.Total.Amount.Should().Be(6.2m);
    }

    [Fact]
    public void The_entry_leg_is_spread_only_with_no_commission()
    {
        var entry = new ExecutionCostModel(new ExecutionCostOptions()).ComputeEntryLeg(Trade(0.31m));

        entry.SpreadCost.Amount.Should().Be(2.17m); // 0.7 pips * 3.1 per pip, one crossing
        entry.Commission.Amount.Should().Be(0m);    // commission rides on the exit leg(s)
    }

    [Fact]
    public void An_exit_leg_scales_spread_and_commission_by_its_lots()
    {
        var exit = new ExecutionCostModel(new ExecutionCostOptions())
            .ComputeExitLeg(Trade(0.30m), new PositionSize(0.15m));

        exit.SpreadCost.Amount.Should().Be(1.05m); // 0.7 * (3.0/0.30 per lot) * 0.15
        exit.Commission.Amount.Should().Be(0.9m);  // 6.0 per lot * 0.15
    }

    [Fact]
    public void Split_exit_legs_sum_to_one_full_exit_for_an_awkward_lot_size()
    {
        // A 0.33-lot trade split 0.11 / 0.22 — the per-lot value-per-pip reconstruction must keep the partial +
        // runner exit costs equal to one full exit crossing, with no rounding double-count.
        var model = new ExecutionCostModel(new ExecutionCostOptions());
        var trade = Trade(0.33m);

        var partial = model.ComputeExitLeg(trade, new PositionSize(0.11m));
        var runner = model.ComputeExitLeg(trade, new PositionSize(0.22m));
        var full = model.ComputeExitLeg(trade, trade.Size);

        (partial.Total.Amount + runner.Total.Amount).Should().BeApproximately(full.Total.Amount, 1e-7m);
    }
}
