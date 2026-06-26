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
/// Locks FVG-SEM-2b §3 — the wrong-order NIX rung in <see cref="EntryManager.Decide"/> (Ep3 L376-413: if price
/// reaches the FARTHER stacked gap before filling the entry limit, do NOT take the trade). Precedence is
/// killzone-end &gt; max-wait &gt; wrong-order-nix &gt; fill: a stacked armed entry whose bar trades to the farther
/// bound (inclusive) cancels with <see cref="EntryCancelReason.StackedFartherGapHitFirst"/> (the caller releases the
/// reservation). A non-stacked entry never nixes; a same-bar entry+farther touch nixes (no-trade beats trade).
/// </summary>
public class EntryManagerStackedNixTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly SymbolSpec Spec = SymbolSpec.FxMajor(Eurusd);
    private static readonly ContractSpec Contract = ContractSpec.FxMajor(Eurusd);
    private static readonly DateTimeOffset Utc = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BarClose = Utc.AddMinutes(5);
    private static readonly EntryContext Context = new(BarClose);

    private static readonly PaperTradeFactory Factory = new(new RiskOptions(), new RiskManager());

    private static EntryManager NewManager(int maxWaitMinutes = 240) => new(
        new EntryFillEvaluator(new EntryManagementOptions(), Spec),
        new FillEvaluator(new FillOptions()),
        new ExecutionCostModel(new ExecutionCostOptions()),
        new KillzoneClock(new NyClock(new FakeTimeProvider()), KillzoneSchedule.CreateDefault()),
        new KillzoneEntryOptions(),
        new EntryManagementOptions { MaxWaitMinutes = maxWaitMinutes });

    private static readonly EntryManager Manager = NewManager();

    private static readonly DateTimeOffset DeadTime = new(2024, 7, 1, 9, 30, 0, TimeSpan.Zero);   // 05:30 NY — no killzone
    private static readonly DateTimeOffset InKillzone = new(2024, 7, 1, 7, 45, 0, TimeSpan.Zero); // 03:45 NY — London Open

    private static PaperAccount Account() => new(Guid.NewGuid(), new Money(10_000m), 5m);

    // Long: entry 1.0832, stop 1.0800. The farther stacked bound 1.0820 sits BETWEEN the entry and the stop
    // (so a bar can reach the farther bound without yet reaching the stop — the nix is a distinct rung).
    private static Setup BullishSetup(decimal? stackedFartherBound)
    {
        var plan = new TradePlan(
            Direction.Bullish, new Price(1.0832m), new Price(1.0800m),
            new TargetLadder(Direction.Bullish, new Price(1.0876m), new Price(1.0920m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("bias; sweep; MSS; FVG; OTE"), Utc, stackedFartherBound);
    }

    private static Setup BearishSetup(decimal? stackedFartherBound)
    {
        var plan = new TradePlan(
            Direction.Bearish, new Price(1.0870m), new Price(1.0900m),
            new TargetLadder(Direction.Bearish, new Price(1.0840m), new Price(1.0790m)));
        return new Setup(
            Eurusd, TradeStyle.Intraday, Timeframe.M5, SetupGrade.B, 70, plan,
            new SetupReason("bias; sweep; MSS; FVG; OTE"), Utc, stackedFartherBound);
    }

    private static ArmedEntry Arm(PaperAccount account, Setup setup) => Factory.Arm(setup, account, Spec, Contract, Utc);

    private static Candle BarAt(DateTimeOffset openTime, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, openTime, open, high, low, close, 1_000m);

    private static Candle Bar(decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, BarClose, open, high, low, close, 1_000m);

    [Fact]
    public void A_stacked_long_bar_that_reaches_the_farther_gap_before_filling_is_nixed_and_released()
    {
        // §6(8): Low 1.0818 <= farther bound 1.0820, and the bar also dips to the entry (1.0832). Pre-fill the nix wins.
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0820m));

        var plan = Manager.Decide(armed, Bar(1.0830m, 1.0835m, 1.0818m, 1.0825m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Cancel);
        plan.Actions[0].CancelReason.Should().Be(EntryCancelReason.StackedFartherGapHitFirst);

        // Apply the cancel: the reservation is released and the cap self-heals.
        armed.Cancel(plan.Actions[0].CancelReason!.Value, plan.Actions[0].AtUtc);
        account.Release(armed.Id);
        account.OpenRisk.Amount.Should().Be(0m);
    }

    [Fact]
    public void An_exact_kiss_of_the_farther_bound_nixes_inclusively()
    {
        // §6(8) boundary: Low == farther bound 1.0820 -> inclusive touch nixes.
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0820m));

        var plan = Manager.Decide(armed, Bar(1.0830m, 1.0835m, 1.0820m, 1.0825m), Context);

        plan.Actions[0].CancelReason.Should().Be(EntryCancelReason.StackedFartherGapHitFirst);
    }

    [Fact]
    public void A_same_bar_entry_and_farther_touch_lets_the_nix_win_over_the_open()
    {
        // §6(9): the bar fills the entry (Low <= 1.0832) AND reaches the farther bound (Low <= 1.0820) the same bar
        // -> NIX wins (no Open). No-trade beats trade.
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0820m));

        var plan = Manager.Decide(armed, Bar(1.0830m, 1.0835m, 1.0815m, 1.0825m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Cancel);
        plan.Actions.Should().NotContain(a => a.Kind == EntryActionKind.Open);
    }

    [Fact]
    public void Killzone_end_outranks_the_stacked_nix()
    {
        // §6(10): the bar reaches the farther bound but the killzone is over -> killzone-end is reported (higher rung).
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0820m));

        var plan = Manager.Decide(
            armed, BarAt(DeadTime, 1.0830m, 1.0835m, 1.0818m, 1.0825m), new EntryContext(DeadTime.AddMinutes(5)));

        plan.Actions[0].CancelReason.Should().Be(EntryCancelReason.KillzoneEnded);
    }

    [Fact]
    public void Max_wait_outranks_the_stacked_nix()
    {
        // §6(11): inside the killzone but past the max-wait backstop, and the bar reaches the farther bound ->
        // max-wait is reported (it is the higher-precedence rung above the nix).
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0820m));

        var plan = NewManager(maxWaitMinutes: 30).Decide(
            armed, BarAt(InKillzone, 1.0830m, 1.0835m, 1.0818m, 1.0825m), new EntryContext(InKillzone.AddMinutes(5)));

        plan.Actions[0].CancelReason.Should().Be(EntryCancelReason.MaxWaitElapsed);
    }

    [Fact]
    public void A_non_stacked_entry_never_nixes_even_on_a_deep_bar()
    {
        // §6(12): no farther bound -> IsStacked == false short-circuits the nix; the deep bar simply fills.
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: null));

        var plan = Manager.Decide(armed, Bar(1.0830m, 1.0835m, 1.0815m, 1.0825m), Context);

        plan.Actions.Should().NotContain(a => a.Kind == EntryActionKind.Cancel);
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open);
    }

    [Fact]
    public void A_stacked_bar_that_stays_above_the_farther_bound_fills_normally()
    {
        // §6(13) downstream: a genuinely-deeper bound exists but the bar never reaches it -> no nix, a clean fill.
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0820m));

        var plan = Manager.Decide(armed, Bar(1.0835m, 1.0840m, 1.0828m, 1.0832m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open); // Low 1.0828 > farther 1.0820, <= entry 1.0832
    }

    [Fact]
    public void A_stacked_short_bar_that_reaches_the_farther_bound_is_nixed_bearish_mirror()
    {
        // §6(14): bearish mirror. farther bound 1.0882 (between entry 1.0870 and stop 1.0900). High >= 1.0882 nixes.
        var account = Account();
        var armed = Arm(account, BearishSetup(stackedFartherBound: 1.0882m));

        var plan = Manager.Decide(armed, Bar(1.0872m, 1.0884m, 1.0868m, 1.0875m), Context);

        plan.Actions[0].CancelReason.Should().Be(EntryCancelReason.StackedFartherGapHitFirst);
    }

    [Fact]
    public void A_stacked_armed_entry_filling_then_running_to_the_stop_books_the_normal_minus_one_R_straddle()
    {
        // §6(21): the bar fills the entry AND runs to the STOP (1.0800), NOT the farther bound (1.0820) — i.e. it
        // passes through the farther bound. But the bar Low 1.0795 also crosses the farther bound 1.0820, so the
        // NIX would fire first. To isolate the straddle the farther bound must be BELOW the stop here.
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0790m)); // farther below the stop 1.0800

        var plan = Manager.Decide(armed, Bar(1.0820m, 1.0825m, 1.0795m, 1.0805m), Context);

        // Low 1.0795 > farther 1.0790 -> no nix; fills entry, runs to stop -> [Open, Close] = -1R straddle.
        plan.Actions.Should().HaveCount(2);
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Open);
        plan.Actions[1].Kind.Should().Be(EntryActionKind.Close);
        plan.Actions[1].Reason.Should().Be(TradeCloseReason.StopHit);
    }

    [Fact]
    public void The_pre_fill_nix_outranks_the_post_fill_straddle_when_the_farther_bound_is_reached()
    {
        // §6(22): the farther bound 1.0810 sits ABOVE the stop 1.0800; the bar reaches BOTH (Low 1.0795). The pre-fill
        // nix outranks the would-be same-bar straddle -> a Cancel, no trade at all (not an Open+Close).
        var account = Account();
        var armed = Arm(account, BullishSetup(stackedFartherBound: 1.0810m));

        var plan = Manager.Decide(armed, Bar(1.0820m, 1.0825m, 1.0795m, 1.0805m), Context);

        plan.Actions.Should().ContainSingle();
        plan.Actions[0].Kind.Should().Be(EntryActionKind.Cancel);
        plan.Actions[0].CancelReason.Should().Be(EntryCancelReason.StackedFartherGapHitFirst);
    }
}
