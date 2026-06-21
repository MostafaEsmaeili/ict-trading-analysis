using FluentAssertions;
using IctTrader.Domain.Common;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks entry-arming cut 2a (plan §2.5.1 step 7 / §2.5.10): a confirmed advisory <see cref="Setup"/> ARMS as a
/// resting limit whose risk is reserved against the SAME portfolio cap an open trade uses (so a same-bar burst of
/// armed limits cannot breach it), and the trigger opens the trade under the SAME id WITHOUT re-reserving — a key
/// re-label that keeps <see cref="PaperAccount.RegisterOpen"/> / <see cref="PaperAccount.Settle"/> byte-unchanged and
/// the reserved money exactly equal to the trade's derived <see cref="PaperTrade.RiskBudget"/> (no drift).
/// </summary>
public class ArmedEntryTests
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

    private static PaperAccount Account(decimal equity = 10_000m) => new(Guid.NewGuid(), new Money(equity), 5m);

    [Fact]
    public void Arming_a_setup_reserves_its_sized_risk_against_the_cap()
    {
        var account = Account();

        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);

        armed.Status.Should().Be(ArmedEntryStatus.Armed);
        armed.AccountId.Should().Be(account.Id);
        armed.Size.Lots.Should().Be(0.31m);          // 1% of 10,000 over a 32-pip stop, floored to the lot step
        armed.RiskBudget.Amount.Should().Be(99.2m);
        account.OpenRisk.Amount.Should().Be(99.2m);  // the resting limit competes for the SAME 5% cap
        account.Equity.Amount.Should().Be(10_000m);  // reserving books no money
    }

    [Fact]
    public void An_arm_that_would_breach_the_cap_is_refused_and_leaves_the_account_untouched()
    {
        // Each arm reserves 99.2 (1% of 10,000 over 32 pips); five fit under the 500 cap, the sixth does not.
        var account = Account();
        for (var i = 0; i < 5; i++)
        {
            Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);
        }

        var act = () => Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);

        act.Should().Throw<DomainException>();
        account.OpenRisk.Amount.Should().Be(496m);   // 5 × 99.2 — the refused arm reserved nothing
    }

    [Fact]
    public void A_same_bar_burst_of_arms_cannot_breach_the_cap()
    {
        // Two 3%-risk arms are individually within the 500 cap but together over it; the second is refused because the
        // first reservation already counts — proving reserve-at-ARM closes the same-bar-burst breach window.
        var bigRisk = new PaperTradeFactory(new RiskOptions { BaseRiskPercent = 3m });
        var account = Account();

        var first = bigRisk.Arm(BullishSetup(), account, Spec, Contract, Utc);
        var act = () => bigRisk.Arm(BullishSetup(), account, Spec, Contract, Utc);

        first.RiskBudget.Amount.Should().Be(297.6m); // 0.93 lots (3% of 10k floored over 32 pips) × 320
        act.Should().Throw<DomainException>();        // 297.6 + 297.6 = 595.2 > 500 cap
        account.OpenRisk.Amount.Should().Be(297.6m);
    }

    [Fact]
    public void Triggering_an_armed_entry_opens_a_trade_under_the_same_id_with_the_reserved_risk()
    {
        var account = Account();
        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);

        var trade = Factory.OpenArmed(armed, Spec, Contract, Utc.AddMinutes(5));

        trade.Id.Should().Be(armed.Id);                       // a key re-label, not a new id
        trade.AccountId.Should().Be(account.Id);
        trade.Size.Lots.Should().Be(armed.Size.Lots);
        trade.RiskBudget.Should().Be(armed.RiskBudget);       // no drift: the derived budget == the reserved budget
        trade.Status.Should().Be(TradeStatus.Open);
        armed.Status.Should().Be(ArmedEntryStatus.Triggered);
        account.OpenRisk.Amount.Should().Be(99.2m);           // unchanged across the handoff (still one reservation)
    }

    [Fact]
    public void Opening_an_armed_entry_does_not_double_reserve_its_risk()
    {
        var account = Account();
        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);

        var trade = Factory.OpenArmed(armed, Spec, Contract, Utc.AddMinutes(5));

        account.OpenRisk.Amount.Should().Be(99.2m);           // ONE reservation, not 198.4
        // The id is already keyed, so a RegisterOpen would be a (rejected) double-reserve — proving OpenArmed skips it.
        ((Action)(() => account.RegisterOpen(trade))).Should().Throw<DomainException>();
    }

    [Fact]
    public void The_armed_round_trip_settles_through_the_unchanged_settle()
    {
        var account = Account();
        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);
        var trade = Factory.OpenArmed(armed, Spec, Contract, Utc.AddMinutes(5));

        trade.Close(trade.Plan.Targets.Runner, TradeCloseReason.TargetHit, TradeCosts.Zero, Utc.AddHours(1));
        account.Settle(trade);

        account.OpenRisk.Amount.Should().Be(0m);              // the carried reservation released by the existing Settle
        account.Equity.Amount.Should().Be(10_272.8m);         // identical booking to an immediately-opened trade
    }

    [Fact]
    public void An_armed_entry_can_be_triggered_only_once()
    {
        var account = Account();
        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);
        Factory.OpenArmed(armed, Spec, Contract, Utc.AddMinutes(5));

        var act = () => Factory.OpenArmed(armed, Spec, Contract, Utc.AddMinutes(6));

        act.Should().Throw<DomainException>();                // MarkTriggered rejects a second trigger
    }

    [Fact]
    public void A_reservation_requires_a_non_empty_unique_id()
    {
        var account = Account();
        var budget = new Money(50m);

        ((Action)(() => account.Reserve(Guid.Empty, budget))).Should().Throw<DomainException>();

        var id = Guid.NewGuid();
        account.Reserve(id, budget);
        ((Action)(() => account.Reserve(id, budget))).Should().Throw<DomainException>(); // already reserved
        account.Equity.Amount.Should().Be(10_000m);           // reserving never touches equity
    }

    [Fact]
    public void A_non_utc_arm_time_is_rejected_before_anything_is_reserved()
    {
        // The atomicity guarantee: the ArmedEntry is constructed BEFORE the reservation, so a bad arm time throws
        // before the ledger is touched — a refused arm leaves the account untouched.
        var account = Account();
        var localTime = new DateTimeOffset(2024, 7, 1, 3, 0, 0, TimeSpan.FromHours(-4));

        var act = () => Factory.Arm(BullishSetup(), account, Spec, Contract, localTime);

        act.Should().Throw<DomainException>();
        account.OpenRisk.Amount.Should().Be(0m);
    }

    [Fact]
    public void Arming_raises_an_entry_armed_event()
    {
        var account = Account();

        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);

        armed.DomainEvents.OfType<EntryArmed>().Should().ContainSingle()
            .Which.Should().Match<EntryArmed>(e =>
                e.ArmedEntryId == armed.Id && e.AccountId == account.Id && e.RiskBudget.Amount == 99.2m);
    }

    [Fact]
    public void Triggering_raises_an_entry_triggered_event_stamped_at_the_fill_time()
    {
        var account = Account();
        var armed = Factory.Arm(BullishSetup(), account, Spec, Contract, Utc);
        var fillTime = Utc.AddMinutes(5);

        Factory.OpenArmed(armed, Spec, Contract, fillTime);

        armed.DomainEvents.OfType<EntryTriggered>().Should().ContainSingle()
            .Which.Should().Match<EntryTriggered>(e =>
                e.ArmedEntryId == armed.Id && e.OccurredOnUtc == fillTime);
    }
}
