using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.5.1-step-7 <see cref="EntryManager"/> fill path: a no-touch bar is <see cref="EntryPlan.NoOp"/>; a
/// retrace into the limit emits a single <see cref="EntryActionKind.Open"/>; and a fast bar that fills the limit AND
/// runs to the stop the same bar emits an apply-ordered Open then Close (the −1R straddle) resolved by the ONE exit
/// <see cref="FillEvaluator"/> authority — while a same-bar runner is NOT credited (the entry path never grants a free
/// same-bar win). DECIDE-only: the caller opens via <see cref="PaperTradeFactory.OpenArmed"/> and closes via
/// <see cref="PaperTrade.Close"/>; the no-chase cancellation is the next cut.
/// </summary>
public class EntryManagerTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BarClose = Utc.AddMinutes(5);
    private static readonly EntryContext Context = new(BarClose);

    private static readonly PaperTradeFactory Factory = new(new RiskOptions());
    private static readonly EntryManager Manager = new(
        new EntryFillEvaluator(), new FillEvaluator(new FillOptions()), new ExecutionCostModel(new ExecutionCostOptions()));

    private static PaperAccount Account() => new(Guid.NewGuid(), new Money(10_000m), 5m);

    // Long: entry 1.0832, stop 1.0800 (32-pip 1R), T1 1.0876, runner 1.0920.
    private static Setup BullishSetup()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan, new SetupReason("bias; sweep; MSS; FVG; OTE"), Utc);
    }

    // Short mirror: entry 1.0870, stop 1.0900, T1 1.0840, runner 1.0790.
    private static Setup BearishSetup()
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0840m), new Price(1.0790m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan, new SetupReason("bias; sweep; MSS; FVG; OTE"), Utc);
    }

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, BarClose, open, high, low, close, 1_000m);

    private static ArmedEntry Arm(PaperAccount account, Setup setup) => Factory.Arm(setup, account, Spec, Contract, Utc);

    private static PaperTrade? Apply(PaperAccount account, ArmedEntry armed, EntryPlan plan)
    {
        PaperTrade? trade = null;
        foreach (var a in plan.Actions)
        {
            switch (a.Kind)
            {
                case EntryActionKind.Open:
                    trade = Factory.OpenArmed(armed, account, a.AtUtc);
                    break;
                case EntryActionKind.Close:
                    trade!.Close(a.Price, a.Reason!.Value, a.Costs, a.AtUtc);
                    break;
            }
        }

        return trade;
    }

    [Fact]
    public void A_bar_that_never_retraces_into_the_limit_decides_nothing()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        var plan = Manager.Decide(armed, Bar(1.0840m, 1.0850m, 1.0835m, 1.0845m), Context);

        plan.HasActions.Should().BeFalse(); // Low 1.0835 > entry 1.0832 — the limit stays resting
    }

    [Fact]
    public void A_clean_retrace_into_the_limit_decides_a_single_open_at_the_entry_level()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        var plan = Manager.Decide(armed, Bar(1.0835m, 1.0840m, 1.0825m, 1.0830m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open);
        plan.Actions[0].Price.Value.Should().Be(1.0832m); // the limit level
        plan.Actions[0].AtUtc.Should().Be(BarClose);
    }

    [Fact]
    public void A_clean_fill_applies_to_an_open_trade_carrying_its_reservation()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        var trade = Apply(account, armed, Manager.Decide(armed, Bar(1.0835m, 1.0840m, 1.0825m, 1.0830m), Context));

        trade!.Status.Should().Be(TradeStatus.Open);
        trade.Id.Should().Be(armed.Id);                  // the trade opened under the reservation's id
        account.OpenRisk.Amount.Should().Be(99.2m);      // the reservation is now the open trade's risk, still reserved
    }

    [Fact]
    public void A_fast_bar_that_fills_then_runs_to_the_stop_decides_an_open_then_a_minus_one_R_close()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        // Low 1.0795 ≤ stop 1.0800 (< entry 1.0832): the limit fills AND the same bar reaches the stop.
        var plan = Manager.Decide(armed, Bar(1.0820m, 1.0825m, 1.0795m, 1.0805m), Context);

        plan.Actions.Should().HaveCount(2);
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open);
        plan.Actions[0].Price.Value.Should().Be(1.0832m);
        plan.Actions[1].Kind.Should().Be(EntryActionKind.Close);
        plan.Actions[1].Reason.Should().Be(TradeCloseReason.StopHit);
        plan.Actions[1].Price.Value.Should().Be(1.0800m);            // the stop level
        plan.Actions[1].Costs.Total.Amount.Should().BeGreaterThan(0m); // the full round trip is costed
        plan.Actions.Should().OnlyContain(a => a.AtUtc == BarClose);   // both stamped at the bar close
    }

    [Fact]
    public void The_same_bar_straddle_applies_to_a_closed_trade_at_exactly_minus_one_R()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        var trade = Apply(account, armed, Manager.Decide(armed, Bar(1.0820m, 1.0825m, 1.0795m, 1.0805m), Context));

        trade!.Status.Should().Be(TradeStatus.Closed);
        trade.CloseReason.Should().Be(TradeCloseReason.StopHit);
        trade.RealizedR!.Value.Should().BeApproximately(-1m, 0.0001m);   // gross −1R vs the frozen 1R
        trade.NetR!.Value.Should().BeLessThan(-1m);                      // costs make the net worse than −1R
        trade.OpenedAtUtc.Should().Be(trade.ClosedAtUtc!.Value);        // the equal-timestamp open→close is legal
        account.OpenRisk.Amount.Should().Be(99.2m);                     // still reserved until the account settles it
    }

    [Fact]
    public void A_same_bar_runner_is_not_credited_only_the_open_is_decided()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        // Low 1.0830 ≤ entry 1.0832 (fills) and High 1.0925 ≥ runner 1.0920 — but the runner is left for the exit pass.
        var plan = Manager.Decide(armed, Bar(1.0835m, 1.0925m, 1.0830m, 1.0915m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open); // no free same-bar win
    }

    [Fact]
    public void A_short_fast_bar_that_fills_then_runs_to_the_stop_decides_the_mirror_straddle()
    {
        var account = Account();
        var armed = Arm(account, BearishSetup());

        // High 1.0905 ≥ stop 1.0900 (> entry 1.0870): the short limit fills AND the same bar reaches the stop.
        var plan = Manager.Decide(armed, Bar(1.0880m, 1.0905m, 1.0875m, 1.0895m), Context);

        plan.Actions.Should().HaveCount(2);
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open);
        plan.Actions[0].Price.Value.Should().Be(1.0870m);
        plan.Actions[1].Kind.Should().Be(EntryActionKind.Close);
        plan.Actions[1].Price.Value.Should().Be(1.0900m);
        plan.Actions[1].Reason.Should().Be(TradeCloseReason.StopHit);
    }

    [Fact]
    public void The_same_inputs_decide_an_identical_plan()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());
        var candle = Bar(1.0820m, 1.0825m, 1.0795m, 1.0805m);

        Manager.Decide(armed, candle, Context).Actions
            .Should().Equal(Manager.Decide(armed, candle, Context).Actions);
    }

    [Fact]
    public void A_triggered_entry_cannot_be_evaluated_again()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());
        Factory.OpenArmed(armed, account, BarClose); // now Triggered

        var act = () => Manager.Decide(armed, Bar(1.0835m, 1.0840m, 1.0825m, 1.0830m), Context);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_candle_for_a_different_symbol_is_rejected()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());
        var foreignBar = new Candle(new Symbol("GBPUSD"), Timeframe.M5, BarClose, 1.0835m, 1.0840m, 1.0825m, 1.0830m, 1_000m);

        var act = () => Manager.Decide(armed, foreignBar, Context);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_default_or_pre_arm_context_is_rejected_before_stamping()
    {
        var account = Account();
        var armed = Arm(account, BullishSetup());

        var act = () => Manager.Decide(armed, Bar(1.0835m, 1.0840m, 1.0825m, 1.0830m), default);

        act.Should().Throw<DomainException>(); // default(EntryContext).BarCloseUtc is MinValue < the arm time
    }

    [Fact]
    public void A_plan_is_immutable_and_rejects_null_actions()
    {
        var source = new List<EntryAction> { EntryAction.Open(new Price(1.0832m), BarClose) };
        var plan = new EntryPlan(source);

        source.Clear();

        plan.Actions.Should().ContainSingle();
        ((Action)(() => _ = new EntryPlan(null!))).Should().Throw<ArgumentNullException>();
    }
}
