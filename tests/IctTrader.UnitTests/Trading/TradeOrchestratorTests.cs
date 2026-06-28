using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §3.4 <see cref="TradeOrchestrator"/> — the per-candle process that finally composes the entry
/// (<see cref="EntryManager"/>) and exit (<see cref="ExitManager"/>) DECIDE halves into a runnable cycle and APPLIES
/// their plans. Built with the REAL sub-managers (not fakes) so these are genuine composition tests of the whole
/// lifecycle: a confirmed advisory <see cref="Setup"/> arms a limit, reserves its risk, fills on the retrace, opens
/// the trade, and is managed to close — with the §2.5.1-step-7 entry path re-feeding the SAME bar to the exit pass so
/// a same-bar runner that genuinely traded is booked (not missed), the same-bar straddle booking −1R, the no-chase
/// cancellation freeing the reservation, and every terminal close settling promptly so the portfolio cap self-heals.
/// </summary>
public class TradeOrchestratorTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);

    // 07:00 UTC on 2024-07-01 = 03:00 NY = inside the London Open killzone (NY is UTC−4 in July). Every bar below
    // opens within 03:00–05:00 NY so the no-chase rung never fires on the happy path.
    private static readonly DateTimeOffset ArmedAt = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeadTime = new(2024, 7, 1, 9, 30, 0, TimeSpan.Zero); // 05:30 NY — no killzone

    private static readonly PaperTradeFactory Factory = new(new RiskOptions(), new RiskManager());

    private static readonly TradeOrchestrator Armed = BuildOrchestrator(new EntryManagementOptions());
    private static readonly TradeOrchestrator Immediate =
        BuildOrchestrator(new EntryManagementOptions { Mode = EntryMode.Immediate });

    private static TradeOrchestrator BuildOrchestrator(EntryManagementOptions entryOptions)
    {
        var entryManager = new EntryManager(
            new EntryFillEvaluator(entryOptions, Spec),
            new FillEvaluator(new FillOptions()),
            new ExecutionCostModel(new ExecutionCostOptions()),
            new KillzoneClock(new NyClock(new FakeTimeProvider()), KillzoneSchedule.CreateDefault()),
            new KillzoneEntryOptions(),
            entryOptions);

        var exitManager = new ExitManager(
            new FillEvaluator(new FillOptions()),
            new StopTrailPolicy(new StopTrailOptions()),
            new ExecutionCostModel(new ExecutionCostOptions()),
            new ExitManagementOptions(),
            new NyClock(new FakeTimeProvider()),
            new TradeStyleOptions(),
            Contract);

        return new TradeOrchestrator(entryManager, exitManager, Factory, entryOptions);
    }

    // A guard-wired orchestrator (Armed mode) with the §2.4/§2.5.5 circuit-breaker ENABLED — halt after 3 losses or 2%.
    private static readonly TradeOrchestrator Guarded = BuildGuardedOrchestrator(
        new DailyRiskGuardOptions { Enabled = true, ConsecutiveLossHaltThreshold = 3, DailyLossCapPercent = 2.0m });

    private static TradeOrchestrator BuildGuardedOrchestrator(DailyRiskGuardOptions guardOptions)
    {
        var entryOptions = new EntryManagementOptions();
        var entryManager = new EntryManager(
            new EntryFillEvaluator(entryOptions, Spec),
            new FillEvaluator(new FillOptions()),
            new ExecutionCostModel(new ExecutionCostOptions()),
            new KillzoneClock(new NyClock(new FakeTimeProvider()), KillzoneSchedule.CreateDefault()),
            new KillzoneEntryOptions(),
            entryOptions);
        var exitManager = new ExitManager(
            new FillEvaluator(new FillOptions()),
            new StopTrailPolicy(new StopTrailOptions()),
            new ExecutionCostModel(new ExecutionCostOptions()),
            new ExitManagementOptions(),
            new NyClock(new FakeTimeProvider()),
            new TradeStyleOptions(),
            Contract);
        return new TradeOrchestrator(
            entryManager, exitManager, Factory, entryOptions, new DailyRiskGuard(), guardOptions);
    }

    private static PaperAccount Account() => new(Guid.NewGuid(), new Money(10_000m), 5m);

    // Settles `count` −1R losses onto the account so its consecutive-loss streak advances (drives the guard's rung 1).
    private static PaperAccount AccountWithLosses(int count)
    {
        var account = Account();
        for (var i = 0; i < count; i++)
        {
            var position = Immediate.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);
            var stopBar = Bar(ArmedAt.AddMinutes(5 * (i + 1)), 1.0805m, 1.0806m, 1.0795m, 1.0801m);
            Immediate.Advance(position, account, stopBar, Close(stopBar)); // stop-out → settles a loss
            position.Trade!.Status.Should().Be(TradeStatus.Closed);
        }

        return account;
    }

    // ---- The §2.4/§2.5.5 daily risk guard --------------------------------------------------------------------

    [Fact]
    public void The_guard_suppresses_a_setup_after_the_consecutive_loss_threshold()
    {
        var account = AccountWithLosses(3); // streak now at 3 (the default halt threshold)
        var riskBefore = account.OpenRisk.Amount;

        var position = Guarded.OnSetupConfirmed(
            BullishSetup(), account, Spec, Contract, ArmedAt, dayRealizedPnl: account.Equity - new Money(10_000m));

        position.IsSuppressed.Should().BeTrue();   // no ArmedEntry, no PaperTrade
        position.Armed.Should().BeNull();
        position.Trade.Should().BeNull();
        position.IsComplete.Should().BeTrue();      // the handler stops — nothing to manage
        account.OpenRisk.Amount.Should().Be(riskBefore); // NOTHING reserved against the cap
    }

    [Fact]
    public void The_guard_suppresses_a_setup_once_the_daily_loss_cap_is_reached()
    {
        var account = Account(); // no streak — isolate the cap rung
        // −2.5% on the day exceeds the 2% cap.
        var position = Guarded.OnSetupConfirmed(
            BullishSetup(), account, Spec, Contract, ArmedAt, dayRealizedPnl: new Money(-250m));

        position.IsSuppressed.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);
    }

    [Fact]
    public void The_guard_admits_a_setup_on_a_clean_day()
    {
        var account = Account();

        var position = Guarded.OnSetupConfirmed(
            BullishSetup(), account, Spec, Contract, ArmedAt, dayRealizedPnl: Money.Zero);

        position.IsSuppressed.Should().BeFalse();
        position.Armed!.Status.Should().Be(ArmedEntryStatus.Armed); // armed normally
        account.OpenRisk.Amount.Should().Be(99.2m);
    }

    [Fact]
    public void Omitting_the_day_pnl_skips_the_guard_even_when_wired()
    {
        var account = AccountWithLosses(3); // would halt IF the guard were consulted

        // No dayRealizedPnl passed → the guard is not consulted → the setup arms (byte-identical unguarded path).
        var position = Guarded.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        position.IsSuppressed.Should().BeFalse();
        position.Armed!.Status.Should().Be(ArmedEntryStatus.Armed);
    }

    // Long: entry 1.0832, stop 1.0800 (32-pip 1R → 0.31 lots → 99.2 reserved), T1 1.0876, runner 1.0920 (+2.75R).
    private static Setup BullishSetup()
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("bias; sweep; MSS; FVG; OTE"), ArmedAt);
    }

    private static Candle Bar(DateTimeOffset openUtc, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, openUtc, open, high, low, close, 1_000m);

    private static DateTimeOffset Close(Candle bar) => bar.OpenTimeUtc.AddMinutes(5);

    // ---- The setup → position boundary (EntryMode) -------------------------------------------------------------

    [Fact]
    public void Confirming_in_armed_mode_rests_a_limit_and_reserves_its_risk()
    {
        var account = Account();

        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        position.Armed!.Status.Should().Be(ArmedEntryStatus.Armed);
        position.Trade.Should().BeNull();
        position.IsComplete.Should().BeFalse();
        account.OpenRisk.Amount.Should().Be(99.2m); // the resting limit reserves against the cap
    }

    [Fact]
    public void Confirming_in_immediate_mode_opens_a_trade_directly()
    {
        var account = Account();

        var position = Immediate.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        position.Armed.Should().BeNull();
        position.Trade!.Status.Should().Be(TradeStatus.Open);
        account.OpenRisk.Amount.Should().Be(99.2m); // the open trade reserves the same risk
    }

    // ---- The entry pass --------------------------------------------------------------------------------------

    [Fact]
    public void A_clean_retrace_opens_the_trade_and_holds_the_reservation()
    {
        var account = Account();
        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        // Fills (Low 1.0825 ≤ entry 1.0832) without reaching the stop, T1, or runner — the re-fed exit pass is a no-op.
        var bar = Bar(ArmedAt.AddMinutes(5), 1.0835m, 1.0840m, 1.0825m, 1.0830m);
        Armed.Advance(position, account, bar, Close(bar));

        position.Trade!.Status.Should().Be(TradeStatus.Open);
        position.Trade.Id.Should().Be(position.Armed!.Id); // opened under the reservation's id
        position.IsComplete.Should().BeFalse();
        account.OpenRisk.Amount.Should().Be(99.2m);         // still reserved while open
    }

    [Fact]
    public void A_same_bar_fill_then_stop_books_minus_one_R_and_frees_the_cap()
    {
        var account = Account();
        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        // Low 1.0795 ≤ stop 1.0800: the limit fills AND the same bar reaches the stop → the −1R straddle.
        var bar = Bar(ArmedAt.AddMinutes(5), 1.0820m, 1.0825m, 1.0795m, 1.0805m);
        Armed.Advance(position, account, bar, Close(bar));

        position.Trade!.Status.Should().Be(TradeStatus.Closed);
        position.Trade.CloseReason.Should().Be(TradeCloseReason.StopHit);
        position.Trade.RealizedR!.Value.Should().BeApproximately(-1m, 0.0001m); // gross −1R vs the frozen 1R
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);            // settled promptly — the cap self-heals
        account.Equity.Amount.Should().BeLessThan(10_000m); // the loss (plus costs) is booked to equity
    }

    [Fact]
    public void A_same_bar_runner_is_booked_by_the_re_feed()
    {
        var account = Account();
        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        // Fills (Low 1.0830 ≤ entry 1.0832) and the SAME bar reaches the runner (High 1.0925 ≥ 1.0920) without the
        // stop. The entry path books only the protective straddle — so the runner is captured by the same-bar re-feed.
        var bar = Bar(ArmedAt.AddMinutes(5), 1.0835m, 1.0925m, 1.0830m, 1.0915m);
        Armed.Advance(position, account, bar, Close(bar));

        position.Trade!.Status.Should().Be(TradeStatus.Closed);
        position.Trade.CloseReason.Should().Be(TradeCloseReason.TargetHit);
        position.Trade.RealizedR!.Value.Should().BeApproximately(2.75m, 0.0001m); // +2.75R at the runner
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);
        account.Equity.Amount.Should().BeGreaterThan(10_000m);
    }

    [Fact]
    public void An_unfilled_limit_past_its_killzone_is_cancelled_and_releases_its_reservation()
    {
        var account = Account();
        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        // The bar WOULD fill, but its open (05:30 NY) is past every active killzone → no-chase cancellation.
        var bar = Bar(DeadTime, 1.0835m, 1.0840m, 1.0825m, 1.0830m);
        Armed.Advance(position, account, bar, Close(bar));

        position.Armed!.Status.Should().Be(ArmedEntryStatus.Cancelled);
        position.Trade.Should().BeNull();
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m); // the reservation is released
    }

    // ---- The exit pass on an already-open trade --------------------------------------------------------------

    [Fact]
    public void An_open_trade_runs_to_its_runner_on_a_later_bar()
    {
        var account = Account();
        var position = Immediate.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        var bar = Bar(ArmedAt.AddMinutes(5), 1.0900m, 1.0925m, 1.0895m, 1.0915m); // High 1.0925 ≥ runner 1.0920
        Immediate.Advance(position, account, bar, Close(bar));

        position.Trade!.Status.Should().Be(TradeStatus.Closed);
        position.Trade.RealizedR!.Value.Should().BeApproximately(2.75m, 0.0001m);
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);
    }

    // ---- The full multi-candle cycle (the flagship) ----------------------------------------------------------

    [Fact]
    public void The_full_cycle_arms_fills_and_runs_to_target_over_three_candles()
    {
        var account = Account();
        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        // 1. A bar whose low never reaches the limit — it keeps resting.
        var rest = Bar(ArmedAt.AddMinutes(5), 1.0845m, 1.0850m, 1.0838m, 1.0842m);
        Armed.Advance(position, account, rest, Close(rest));
        position.Armed!.Status.Should().Be(ArmedEntryStatus.Armed);
        position.Trade.Should().BeNull();
        account.OpenRisk.Amount.Should().Be(99.2m); // the reservation is held across a non-filling bar

        // 2. A clean retrace fills the limit and opens the trade (the re-fed exit pass is a no-op on this bar).
        var fill = Bar(ArmedAt.AddMinutes(10), 1.0835m, 1.0840m, 1.0825m, 1.0830m);
        Armed.Advance(position, account, fill, Close(fill));
        position.Trade!.Status.Should().Be(TradeStatus.Open);

        // 3. A later bar reaches the runner — the trade closes and settles.
        var run = Bar(ArmedAt.AddMinutes(15), 1.0900m, 1.0925m, 1.0895m, 1.0915m);
        Armed.Advance(position, account, run, Close(run));

        position.Trade.Status.Should().Be(TradeStatus.Closed);
        position.Trade.RealizedR!.Value.Should().BeApproximately(2.75m, 0.0001m);
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);
        account.Equity.Amount.Should().BeGreaterThan(10_000m); // the winning runner is booked to equity
    }

    [Fact]
    public void A_partial_then_runner_books_a_blended_R_and_settles_once()
    {
        var account = Account();
        var position = Immediate.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);

        // 1. A bar that reaches T1 (1.0876) but not the runner — take the partial; the stop trails toward breakeven.
        var t1 = Bar(ArmedAt.AddMinutes(5), 1.0860m, 1.0880m, 1.0855m, 1.0875m);
        Immediate.Advance(position, account, t1, Close(t1));
        position.Trade!.HasScaledOut.Should().BeTrue();
        position.Trade.Status.Should().Be(TradeStatus.Open);

        // 2. A later bar reaches the runner (1.0920) without dipping to the trailed stop — the runner leg closes it.
        var run = Bar(ArmedAt.AddMinutes(10), 1.0905m, 1.0925m, 1.0900m, 1.0915m);
        Immediate.Advance(position, account, run, Close(run));

        position.Trade.Status.Should().Be(TradeStatus.Closed);
        // The 0.31-lot size × PartialFraction 0.50 = 0.155, FLOORED to the 0.01 lot step ([15]) → a 0.15 partial leg +
        // a 0.16 residual runner: (0.15 @ +1.375R + 0.16 @ +2.75R) / 0.31 = +2.08468R blended vs the frozen 1R.
        position.Trade.RealizedR!.Value.Should().BeApproximately(2.08468m, 0.0001m);
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);            // a single terminal settle releases the whole reservation
        account.Equity.Amount.Should().BeGreaterThan(10_000m);
    }

    // ---- Idempotence ------------------------------------------------------------------------------------------

    [Fact]
    public void Advancing_a_completed_position_is_a_no_op()
    {
        var account = Account();
        var position = Immediate.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);
        var run = Bar(ArmedAt.AddMinutes(5), 1.0900m, 1.0925m, 1.0895m, 1.0915m);
        Immediate.Advance(position, account, run, Close(run));
        var equityAfterClose = account.Equity;

        // A further bar after the trade is done changes nothing and does not throw.
        var later = Bar(ArmedAt.AddMinutes(10), 1.0930m, 1.0940m, 1.0700m, 1.0720m);
        var act = () => Immediate.Advance(position, account, later, Close(later));

        act.Should().NotThrow();
        position.IsComplete.Should().BeTrue();
        account.Equity.Should().Be(equityAfterClose);
    }

    [Fact]
    public void Advancing_a_cancelled_position_is_a_no_op()
    {
        var account = Account();
        var position = Armed.OnSetupConfirmed(BullishSetup(), account, Spec, Contract, ArmedAt);
        var dead = Bar(DeadTime, 1.0835m, 1.0840m, 1.0825m, 1.0830m);
        Armed.Advance(position, account, dead, Close(dead)); // cancels (past the killzone), releasing the reservation
        position.IsComplete.Should().BeTrue();
        account.OpenRisk.Amount.Should().Be(0m);

        // Any further bar changes nothing — the cancelled limit is not re-evaluated and no trade is born.
        var later = Bar(DeadTime.AddMinutes(10), 1.0835m, 1.0840m, 1.0825m, 1.0830m);
        var act = () => Armed.Advance(position, account, later, Close(later));

        act.Should().NotThrow();
        position.Trade.Should().BeNull();
        account.OpenRisk.Amount.Should().Be(0m);
    }
}
