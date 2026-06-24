using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.4/§2.5.5 adaptive money-management model: the pure <see cref="RiskManager"/> picks the effective
/// per-trade risk from the account's <see cref="RiskState"/> (win-cycle override → base/restored → loss-ladder), the
/// <see cref="PaperAccount"/> advances that state at its single win/loss boundary (Settle), and
/// <see cref="PaperTradeFactory"/> sizes the next trade from it — so a drawdown shrinks risk and a win-streak protects
/// profits, instead of the old flat base risk.
/// </summary>
public class RiskManagerTests
{
    private static readonly RiskManager Manager = new();
    private static readonly RiskOptions Options = new();

    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly DateTimeOffset T0 = new(2024, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static Money M(decimal amount) => new(amount);

    private static PaperAccount Account() => new(Guid.NewGuid(), M(10_000m), 5m);

    // ── Pure EffectiveRisk (explicitly-built RiskState) ────────────────────────────────────────────────

    [Fact]
    public void A_fresh_state_sizes_at_base_risk()
        => Manager.EffectiveRisk(RiskState.Initial(M(10_000m)), Options).Value.Should().Be(1.0m);

    [Fact]
    public void One_unrecovered_loss_steps_down_the_ladder()
    {
        // 1 loss, equity at the trough (no recovery): first ladder reduction.
        var state = new RiskState(0, 1, M(9_900m), M(10_000m), M(9_900m));
        Manager.EffectiveRisk(state, Options).Value.Should().Be(0.5m);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    public void Two_or_more_unrecovered_losses_reach_the_lowest_unit(int losses)
    {
        var state = new RiskState(0, losses, M(9_800m), M(10_000m), M(9_800m));
        Manager.EffectiveRisk(state, Options).Value.Should().Be(0.25m);
    }

    [Fact]
    public void A_drawdown_recovered_to_the_threshold_restores_base_risk()
    {
        // dip = 100 (10,000 → 9,900); equity back to 9,950 = exactly 50% recovered → restore base.
        var state = new RiskState(0, 1, M(9_950m), M(10_000m), M(9_900m));
        Manager.EffectiveRisk(state, Options).Value.Should().Be(1.0m);
    }

    [Fact]
    public void A_drawdown_below_the_recovery_threshold_stays_on_the_ladder()
    {
        // dip = 100; equity 9,920 = only 20% recovered → still reduced.
        var state = new RiskState(0, 1, M(9_920m), M(10_000m), M(9_900m));
        Manager.EffectiveRisk(state, Options).Value.Should().Be(0.5m);
    }

    [Fact]
    public void Five_consecutive_wins_drop_to_the_lowest_unit()
    {
        // The win-cycle override wins even with no drawdown in play (protect the run's profits).
        var state = new RiskState(5, 0, M(10_500m), M(10_500m), M(10_500m));
        Manager.EffectiveRisk(state, Options).Value.Should().Be(0.25m);
    }

    [Fact]
    public void The_win_cycle_fires_at_each_milestone_and_does_not_latch_low()
    {
        // A 6th consecutive win sizes at base again — the cycle restarted after the 5-win milestone, it does not stay
        // latched at the lowest unit for the rest of a long streak (Primer Ep15 "then the procedure starts again").
        Manager.EffectiveRisk(new RiskState(6, 0, M(10_600m), M(10_600m), M(10_600m)), Options).Value.Should().Be(1.0m);
        Manager.EffectiveRisk(new RiskState(9, 0, M(10_900m), M(10_900m), M(10_900m)), Options).Value.Should().Be(1.0m);
        // ...and the next milestone (10 wins) drops to the lowest unit again.
        Manager.EffectiveRisk(new RiskState(10, 0, M(11_000m), M(11_000m), M(11_000m)), Options).Value.Should().Be(0.25m);
    }

    [Fact]
    public void The_configured_base_is_honored_when_no_streak_or_drawdown_is_active()
        => Manager.EffectiveRisk(RiskState.Initial(M(10_000m)), new RiskOptions { BaseRiskPercent = 2.0m })
            .Value.Should().Be(2.0m);

    // ── PaperAccount advances the state at Settle ──────────────────────────────────────────────────────

    [Fact]
    public void Two_losses_then_the_manager_sizes_on_the_lowest_ladder_unit()
    {
        var account = Account();
        SettleLoss(account);
        SettleLoss(account);

        account.RiskState.ConsecutiveLosses.Should().Be(2);
        Manager.EffectiveRisk(account.RiskState, Options).Value.Should().Be(0.25m);
    }

    [Fact]
    public void A_new_equity_high_clears_the_loss_streak_and_restores_base_risk()
    {
        // A win that prints a new equity high is a FULL recovery — the drawdown is over, so the ladder clears.
        var account = Account();
        SettleLoss(account);
        SettleWin(account); // runner win → above the prior peak

        account.RiskState.ConsecutiveLosses.Should().Be(0);
        account.RiskState.ConsecutiveWins.Should().Be(1);
        Manager.EffectiveRisk(account.RiskState, Options).Value.Should().Be(1.0m);
    }

    [Fact]
    public void A_new_equity_high_resets_the_drawdown_trough()
    {
        var account = Account();
        SettleLoss(account);                       // 10,000 → 9,995, trough 9,995
        SettleWin(account);                        // 9,995 → 10,005, a new high

        account.RiskState.PeakEquity.Amount.Should().Be(10_005m);
        account.RiskState.DipTrough.Amount.Should().Be(10_005m); // drawdown reset at the new high
    }

    [Fact]
    public void A_win_inside_an_unrecovered_drawdown_does_not_restore_base_risk()
    {
        // Recovery-gated (§2.5.5, decisions register TGR-5): a small win that does not recover 50% of the dip keeps
        // risk suppressed — the loss-ladder PERSISTS through the win, unlike a naive consecutive-loss-streak clear.
        var account = Account();
        SettleLoss(account);
        SettleLoss(account);        // 0.25% tier, deep in the drawdown
        SettleSmallWin(account);    // a small win — equity climbs but well under the 50% recovery threshold

        account.RiskState.ConsecutiveLosses.Should().Be(2);   // the ladder is NOT cleared by a single win
        account.RiskState.CurrentEquity.Amount.Should().BeLessThan(account.RiskState.PeakEquity.Amount);
        Manager.EffectiveRisk(account.RiskState, Options).Value.Should().Be(0.25m);
    }

    [Fact]
    public void A_partial_recovery_to_the_threshold_restores_base_risk()
    {
        // Once equity claws back >= 50% of the dip, base risk is restored — even though the loss count still persists
        // (it clears fully only on a new high). This is the recovery that the win-gated reading skipped.
        var account = Account();
        SettleLoss(account);
        SettleLoss(account);        // a ~10 dip from the 10,000 peak (two -5 losses)
        SettleMediumWin(account);   // +5 → exactly 50% of the dip recovered, still below the peak

        account.RiskState.ConsecutiveLosses.Should().Be(2);   // count persists (no new high yet)
        account.RiskState.CurrentEquity.Amount.Should().BeLessThan(account.RiskState.PeakEquity.Amount);
        Manager.EffectiveRisk(account.RiskState, Options).Value.Should().Be(1.0m); // recovery-gated restore fired
    }

    [Fact]
    public void A_breakeven_ends_a_win_streak_but_does_not_advance_the_ladder()
    {
        var account = Account();
        SettleWin(account);
        SettleBreakeven(account);

        account.RiskState.ConsecutiveWins.Should().Be(0);   // a breakeven is not a win
        account.RiskState.ConsecutiveLosses.Should().Be(0); // nor a loss
    }

    [Fact]
    public void A_cost_bearing_scratch_is_a_breakeven_not_a_loss()
    {
        // A stop trailed to entry exits at gross 0 but pays spread + commission → net slightly negative. The streak is
        // the STRUCTURAL (gross) outcome, so the loss-ladder does NOT advance — even though the costs dip equity.
        var account = Account();
        SettleScratchWithCosts(account);

        account.RiskState.ConsecutiveLosses.Should().Be(0);  // a cost-only scratch is not a loss
        account.RiskState.ConsecutiveWins.Should().Be(0);    // nor a win
        account.Equity.Amount.Should().BeLessThan(10_000m);  // but the costs do dip equity (drawdown tracks net)
    }

    [Fact]
    public void A_breakeven_inside_a_drawdown_holds_the_ladder()
    {
        var account = Account();
        SettleLoss(account);
        SettleLoss(account);
        SettleBreakeven(account); // neither recovers equity nor adds a loss

        account.RiskState.ConsecutiveLosses.Should().Be(2);
        Manager.EffectiveRisk(account.RiskState, Options).Value.Should().Be(0.25m);
    }

    // ── End-to-end: the factory sizes from the adaptive state ──────────────────────────────────────────

    [Fact]
    public void After_two_losses_the_factory_opens_a_smaller_position()
    {
        var fresh = Account();
        var freshLots = OpenViaFactory(fresh).Size.Lots; // base 1%

        var drawn = Account();
        SettleLoss(drawn);
        SettleLoss(drawn);
        var reducedLots = OpenViaFactory(drawn).Size.Lots; // lowest unit 0.25%

        // The 0.25% lowest unit vs the 1% base ⇒ well under half the lots (the exact 4× ratio blurs only because
        // the sizer floors to the lot step; the precise effective-percent is locked by the EffectiveRisk tests above).
        reducedLots.Should().BeLessThan(freshLots);
        reducedLots.Should().BeLessThan(freshLots * 0.5m);
    }

    private static PaperTrade OpenViaFactory(PaperAccount account)
    {
        var factory = new PaperTradeFactory(Options, Manager);
        return factory.Open(BullishSetup(), account, Spec, Contract, T0);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────

    private static TradePlan BullishPlan() => new(
        Direction.Bullish, new Price(1.0900m), new Price(1.0850m),
        new TargetLadder(Direction.Bullish, new Price(1.0950m), new Price(1.1000m)));

    private static Setup BullishSetup() => new(
        Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, BullishPlan(),
        new SetupReason("bias; sweep; MSS; FVG; OTE"), T0);

    private static void Settle(PaperAccount account, decimal exitPrice, TradeCloseReason reason)
    {
        var trade = new PaperTrade(
            Guid.NewGuid(), account.Id, Eurusd, TradeStyle.Intraday, Timeframe.M5,
            BullishPlan(), new PositionSize(0.1m), pipSize: 0.0001m, valuePerPip: 1m, T0);
        account.RegisterOpen(trade);
        trade.Close(new Price(exitPrice), reason, TradeCosts.Zero, T0.AddMinutes(10));
        account.Settle(trade);
    }

    private static void SettleWin(PaperAccount account) => Settle(account, 1.1000m, TradeCloseReason.TargetHit);

    private static void SettleLoss(PaperAccount account) => Settle(account, 1.0850m, TradeCloseReason.StopHit);

    private static void SettleSmallWin(PaperAccount account) => Settle(account, 1.0920m, TradeCloseReason.TargetHit);

    private static void SettleMediumWin(PaperAccount account) => Settle(account, 1.0950m, TradeCloseReason.TargetHit);

    private static void SettleBreakeven(PaperAccount account) => Settle(account, 1.0900m, TradeCloseReason.TimeExit);

    private static void SettleScratchWithCosts(PaperAccount account)
    {
        // Exits AT the entry (gross 0) but pays spread + commission, so the NET is slightly negative.
        var trade = new PaperTrade(
            Guid.NewGuid(), account.Id, Eurusd, TradeStyle.Intraday, Timeframe.M5,
            BullishPlan(), new PositionSize(0.1m), pipSize: 0.0001m, valuePerPip: 1m, T0);
        account.RegisterOpen(trade);
        trade.Close(new Price(1.0900m), TradeCloseReason.TimeExit, new TradeCosts(new Money(0.5m), new Money(1m)), T0.AddMinutes(10));
        account.Settle(trade);
    }
}
