using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.UnitTests.Trading;

/// <summary>
/// Locks the §2.4/§2.5.5 <see cref="DailyRiskGuard"/> — the pure circuit-breaker that HALTS new entries for the rest of
/// the NY day after a run of losses (the loss-ladder exhausted) or once the day's realized loss reaches the cap
/// (Ep41 revenge/loser's-cycle, Ep37 "stop pushing buttons", Ep18 "walk away"). DECIDE-only: it reads the account's
/// streak/equity snapshot + the caller-owned day P&amp;L tally and returns allow/halt — it never touches a clock or the
/// account. A cost-only scratch is a breakeven (not a loss) upstream, so the streak it reads already excludes it.
/// </summary>
public class DailyRiskGuardTests
{
    private static readonly DailyRiskGuard Guard = new();

    // Default knobs: halt after 3 consecutive losses, or a 2% realized daily loss.
    private static DailyRiskGuardOptions Enabled(int lossHalt = 3, decimal capPercent = 2.0m) =>
        new() { Enabled = true, ConsecutiveLossHaltThreshold = lossHalt, DailyLossCapPercent = capPercent };

    private static RiskState State(int consecutiveLosses, decimal equity = 10_000m) =>
        new(0, consecutiveLosses, new Money(equity), new Money(equity), new Money(equity));

    [Fact]
    public void Disabled_guard_always_admits_even_after_many_losses_and_a_big_day_loss()
    {
        var options = new DailyRiskGuardOptions { Enabled = false };

        var decision = Guard.Evaluate(State(consecutiveLosses: 9), new Money(-5_000m), options);

        decision.EntriesAllowed.Should().BeTrue();
        decision.Reason.Should().Be(DailyRiskHaltReason.None);
    }

    [Fact]
    public void A_fresh_day_with_no_streak_admits()
    {
        var decision = Guard.Evaluate(State(consecutiveLosses: 0), Money.Zero, Enabled());

        decision.EntriesAllowed.Should().BeTrue();
        decision.Reason.Should().Be(DailyRiskHaltReason.None);
    }

    [Theory]
    [InlineData(2, true)]  // below the threshold of 3 — still trading (laddered down, but admitted)
    [InlineData(3, false)] // reaches the threshold — halt
    [InlineData(4, false)] // beyond the threshold — halt
    public void Consecutive_loss_threshold_is_inclusive(int losses, bool allowed)
    {
        var decision = Guard.Evaluate(State(losses), Money.Zero, Enabled(lossHalt: 3));

        decision.EntriesAllowed.Should().Be(allowed);
        if (!allowed)
        {
            decision.Reason.Should().Be(DailyRiskHaltReason.ConsecutiveLosses);
        }
    }

    [Fact]
    public void Daily_loss_cap_halts_at_or_beyond_the_percent_of_equity()
    {
        var options = Enabled(lossHalt: 99, capPercent: 2.0m); // isolate the cap rung
        // 2% of 10,000 = 200. Just under stays in; exactly at and beyond halts.
        Guard.Evaluate(State(0), new Money(-199.99m), options).EntriesAllowed.Should().BeTrue();
        Guard.Evaluate(State(0), new Money(-200m), options).EntriesAllowed.Should().BeFalse();
        Guard.Evaluate(State(0), new Money(-250m), options).Reason.Should().Be(DailyRiskHaltReason.DailyLossCap);
    }

    [Fact]
    public void A_green_day_never_halts_on_the_cap()
    {
        // Net positive on the day — even a large gain is fine; the cap only watches realized LOSS.
        var decision = Guard.Evaluate(State(consecutiveLosses: 0), new Money(5_000m), Enabled(capPercent: 2.0m));

        decision.EntriesAllowed.Should().BeTrue();
    }

    [Fact]
    public void The_cap_scales_with_current_equity()
    {
        var options = Enabled(lossHalt: 99, capPercent: 3.0m);
        // 3% of 20,000 = 600 → a 500 loss is still inside the cap on a bigger account.
        Guard.Evaluate(State(0, equity: 20_000m), new Money(-500m), options).EntriesAllowed.Should().BeTrue();
        Guard.Evaluate(State(0, equity: 20_000m), new Money(-600m), options).EntriesAllowed.Should().BeFalse();
    }

    [Fact]
    public void The_loss_streak_rung_outranks_the_daily_cap_when_both_trip()
    {
        // Both the streak (3) and the cap (−300 ≥ 2% of 10k) are tripped → the streak reason wins (checked first).
        var decision = Guard.Evaluate(State(consecutiveLosses: 3), new Money(-300m), Enabled());

        decision.EntriesAllowed.Should().BeFalse();
        decision.Reason.Should().Be(DailyRiskHaltReason.ConsecutiveLosses);
    }
}
