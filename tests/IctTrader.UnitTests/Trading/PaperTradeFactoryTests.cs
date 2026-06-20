using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the WP4 Definition-of-Done (plan §11/§5.1): a confirmed advisory <see cref="Setup"/> becomes exactly
/// ONE sized <see cref="PaperTrade"/> against a <see cref="PaperAccount"/> with the correct size and the plan's
/// reward-to-risk, the account reserves the trade's risk, and a trade that would breach the portfolio cap is
/// refused. Closing the round-trip settles the account.
/// </summary>
public class PaperTradeFactoryTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly PaperTradeFactory Factory = new(new RiskOptions());

    private static Setup BullishSetup()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("Daily bias Bullish; sweep; MSS; FVG; OTE"), Utc);
    }

    [Fact]
    public void Builds_one_sized_trade_from_a_setup_and_reserves_its_risk()
    {
        var account = new PaperAccount(Guid.NewGuid(), new Money(10_000m), 5m);

        var trade = Factory.Open(BullishSetup(), account, Spec, Contract, Utc);

        trade.AccountId.Should().Be(account.Id);
        trade.Direction.Should().Be(Direction.Bullish);
        trade.Style.Should().Be(TradeStyle.Intraday);
        trade.Size.Lots.Should().Be(0.31m);                          // 1% of 10,000 over a 32-pip stop, floored
        trade.RiskBudget.Amount.Should().Be(99.2m);
        trade.Plan.RewardRatio.Value.Should().BeApproximately(2.75m, 0.0001m);
        account.OpenRisk.Amount.Should().Be(99.2m);                  // the account reserved it
    }

    [Fact]
    public void The_round_trip_to_target_settles_the_account()
    {
        var account = new PaperAccount(Guid.NewGuid(), new Money(10_000m), 5m);
        var trade = Factory.Open(BullishSetup(), account, Spec, Contract, Utc);

        trade.Close(trade.Plan.Targets.Runner, TradeCloseReason.TargetHit, TradeCosts.Zero, Utc.AddHours(1));
        account.Settle(trade);

        account.OpenRisk.Amount.Should().Be(0m);
        account.Equity.Amount.Should().Be(10_272.8m);
    }

    [Fact]
    public void A_trade_that_would_breach_the_portfolio_cap_is_refused()
    {
        // Each trade reserves 99.2 (1% of 10,000 over a 32-pip stop); five fit under the 500 cap, the sixth does not.
        var account = new PaperAccount(Guid.NewGuid(), new Money(10_000m), 5m);
        for (var i = 0; i < 5; i++)
        {
            Factory.Open(BullishSetup(), account, Spec, Contract, Utc);
        }

        var act = () => Factory.Open(BullishSetup(), account, Spec, Contract, Utc);

        act.Should().Throw<DomainException>();
        account.OpenRisk.Amount.Should().Be(496m); // 5 * 99.2 — unchanged, the sixth open was atomic
    }
}
