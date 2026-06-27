using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Setups;

/// <summary>
/// Locks the confluence FSM (plan §2.5/§4.4): the MSS owns the direction lock, the sweep must precede it, the
/// premium/discount veto is realised by direction-consistency, event conditions age out while standing
/// conditions re-evaluate every candle, an invalidated MSS tears the candidate down, and a confirmed setup
/// resets so it is not re-emitted. A reduced required set + unit weights make the grade deterministic.
/// </summary>
public class SetupCandidateTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset Base = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);
    private static readonly FakeTimeProvider Time = new(Base);

    private static readonly ConfluenceCondition[] Required =
    [
        ConfluenceCondition.DisplacementMss,   // event, owns the lock
        ConfluenceCondition.LiquiditySweep,    // event, must precede the MSS
        ConfluenceCondition.BiasAligned,       // standing
        ConfluenceCondition.PremiumDiscountHalf, // standing, the entry-half veto
    ];

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static ConfluenceOptions Confluence() => new()
    {
        Weights = Required.ToDictionary(c => c, _ => 1.0m),
        RequiredConditions = Required,
        AlertMinimumGrade = SetupGrade.B,
    };

    private static SetupCandidate NewCandidate(SetupCandidateOptions? options = null)
    {
        var confluence = Confluence();
        return new SetupCandidate(confluence, options ?? new SetupCandidateOptions(), new SetupScorer(confluence));
    }

    private static Candle Candle(int i) =>
        new(Eurusd, Timeframe.M5, Base.AddMinutes(5 * i), 1.0850m, 1.0855m, 1.0845m, 1.0850m, 1m);

    private static MarketStructureShift Mss(Direction direction) =>
        new(direction, Timeframe.M5, new Price(1.0840m), new Price(1.0855m), Base);

    private static ConfluenceMatch Match(ConfluenceCondition condition, Direction? direction) =>
        new(condition, DetectorResult.Match(direction, 1.0850m, condition.ToString(), null));

    // Appends candle i and folds the given matches into the candidate, wiring ctx.LastMss when a shift is present.
    private static SetupConfirmation? Step(
        MarketContext ctx, SetupCandidate candidate, int i, MarketStructureShift? mss, params ConfluenceMatch[] matches)
    {
        var candle = Candle(i);
        ctx.Append(candle);
        if (mss is not null)
        {
            ctx.SetMarketStructureShift(mss);
        }

        return candidate.Observe(ctx, candle, matches);
    }

    [Fact]
    public void Confirms_an_advisory_graded_setup_from_the_accumulated_sequence()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        // Candle 0: the sweep forms (event, latched). No shift yet -> nothing to confirm.
        Step(ctx, candidate, 0, mss: null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish))
            .Should().BeNull();

        // Candle 1: the displacement MSS shifts structure bullish AFTER the sweep, with bias + discount half.
        var confirmation = Step(
            ctx, candidate, 1, Mss(Direction.Bullish),
            Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
            Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
            Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish));

        confirmation.Should().NotBeNull();
        confirmation!.Direction.Should().Be(Direction.Bullish);
        confirmation.Grade.Should().Be(SetupGrade.A); // all four weighted conditions matched -> 100
        confirmation.Score.Should().Be(100);
        confirmation.Symbol.Should().Be(Eurusd);
        confirmation.Timeframe.Should().Be(Timeframe.M5);
        confirmation.IsAdvisoryOnly.Should().BeTrue();
        confirmation.Confluences.Should().HaveCount(4);
        confirmation.Confluences[0].Condition.Should().Be(ConfluenceCondition.BiasAligned); // narrative order
        confirmation.Confluences[^1].Condition.Should().Be(ConfluenceCondition.PremiumDiscountHalf);
    }

    [Fact]
    public void Grade_A_is_reachable_through_the_FSM_when_the_optional_emitters_match()
    {
        // END-TO-END proof through the FSM (not just SetupScorer): under the DEFAULT §2.5.3 weights (Σ = 9.75), a
        // setup with ALL RequiredConditions PLUS the three optional confluences OteZone + OpenPriceReference +
        // MacroTime scores 7.80 / 9.75 = 80 -> Grade A. Before these emitters existed the model topped out at B.
        var confluence = new ConfluenceOptions(); // real §2.5.3 weights + the constant weighted universe
        var candidate = new SetupCandidate(confluence, new SetupCandidateOptions(), new SetupScorer(confluence));
        var ctx = NewContext();

        // Sweep first (event, precedes the shift).
        Step(ctx, candidate, 0, mss: null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));

        // The shift + every other RequiredCondition + the three optional Grade-A enablers, all bullish-aligned.
        var confirmation = Step(
            ctx, candidate, 1, Mss(Direction.Bullish),
            Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
            Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
            Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish),
            Match(ConfluenceCondition.FvgPresent, Direction.Bullish),
            Match(ConfluenceCondition.KillzoneEntry, direction: null),
            Match(ConfluenceCondition.CalendarClear, direction: null),
            Match(ConfluenceCondition.DrawTargetRrMet, Direction.Bullish),
            Match(ConfluenceCondition.OteZone, Direction.Bullish),
            Match(ConfluenceCondition.OpenPriceReference, Direction.Bullish),
            Match(ConfluenceCondition.MacroTime, direction: null));

        confirmation.Should().NotBeNull();
        confirmation!.Grade.Should().Be(SetupGrade.A);
        confirmation.Score.Should().Be(80);
    }

    [Fact]
    public void Does_not_confirm_while_a_required_condition_is_missing()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        Step(ctx, candidate, 0, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));

        // MSS + bias but NO premium/discount half -> required gate unmet -> no confirmation.
        Step(ctx, candidate, 1, Mss(Direction.Bullish),
                Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish))
            .Should().BeNull();
    }

    [Fact]
    public void The_premium_discount_veto_blocks_the_wrong_half_then_clears_on_the_right_half()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        Step(ctx, candidate, 0, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));

        // Bullish lock, but price is in PREMIUM (gate emits Bearish) -> never buy in premium -> not counted.
        Step(ctx, candidate, 1, Mss(Direction.Bullish),
                Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
                Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bearish))
            .Should().BeNull();

        // Next candle price is back in discount (gate emits Bullish) -> the standing half re-evaluates and the
        // setup confirms. The sweep + MSS are still latched within the assembly window.
        var confirmation = Step(ctx, candidate, 2, null,
            Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
            Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish));

        confirmation.Should().NotBeNull();
        confirmation!.Direction.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void A_sweep_recorded_after_the_shift_does_not_complete_the_setup()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        // MSS first (bar 0), THEN the sweep arrives (bar 1) — a sweep after the shift is a new liquidity event,
        // not completion of this setup, so LiquiditySweep stays unmatched and nothing confirms.
        Step(ctx, candidate, 0, Mss(Direction.Bullish),
            Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
            Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
            Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish));

        Step(ctx, candidate, 1, null,
                Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish),
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
                Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish))
            .Should().BeNull();
    }

    [Fact]
    public void A_stale_sweep_ages_out_of_the_assembly_window()
    {
        var ctx = NewContext();
        var candidate = NewCandidate(new SetupCandidateOptions { MaxAssemblyBars = 2 });

        Step(ctx, candidate, 0, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));

        // Idle past the window so the latched sweep ages out before the shift arrives.
        Step(ctx, candidate, 1, null);
        Step(ctx, candidate, 2, null);

        Step(ctx, candidate, 3, Mss(Direction.Bullish),
                Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
                Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish))
            .Should().BeNull(); // sweep expired -> required LiquiditySweep missing
    }

    [Fact]
    public void An_opposing_shift_reseeds_the_direction_lock()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        Step(ctx, candidate, 0, Mss(Direction.Bullish),
            Match(ConfluenceCondition.DisplacementMss, Direction.Bullish));
        candidate.LockedDirection.Should().Be(Direction.Bullish);

        Step(ctx, candidate, 1, Mss(Direction.Bearish),
            Match(ConfluenceCondition.DisplacementMss, Direction.Bearish));
        candidate.LockedDirection.Should().Be(Direction.Bearish); // intraday reversal = a new setup direction
    }

    [Fact]
    public void An_intraday_reversal_reseeds_and_confirms_the_new_direction_with_its_own_sweep()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        // A bullish leg that locks but does not complete (no bias/PD yet).
        Step(ctx, candidate, 0, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));
        Step(ctx, candidate, 1, Mss(Direction.Bullish), Match(ConfluenceCondition.DisplacementMss, Direction.Bullish));

        // The market reverses: a bearish sweep, then a bearish shift. The reseed must KEEP the new bearish
        // precedent sweep (it preceded the new shift) and drop the stale bullish structure, then confirm bearish.
        Step(ctx, candidate, 2, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bearish));
        var confirmation = Step(ctx, candidate, 3, Mss(Direction.Bearish),
            Match(ConfluenceCondition.DisplacementMss, Direction.Bearish),
            Match(ConfluenceCondition.BiasAligned, Direction.Bearish),
            Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bearish));

        confirmation.Should().NotBeNull();
        confirmation!.Direction.Should().Be(Direction.Bearish);
    }

    [Fact]
    public void An_invalidated_anchoring_mss_tears_the_candidate_down()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();
        var mss = Mss(Direction.Bullish);

        Step(ctx, candidate, 0, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));
        Step(ctx, candidate, 1, mss, Match(ConfluenceCondition.DisplacementMss, Direction.Bullish));
        candidate.LockedDirection.Should().Be(Direction.Bullish);

        mss.Invalidate(); // ITH/ITL breach: the directional premise is gone

        Step(ctx, candidate, 2, null,
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
                Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish))
            .Should().BeNull();
        candidate.LockedDirection.Should().BeNull();
        candidate.HasActivity.Should().BeFalse();
    }

    [Fact]
    public void A_confirmed_setup_resets_and_is_not_re_emitted_next_candle()
    {
        var ctx = NewContext();
        var candidate = NewCandidate();

        Step(ctx, candidate, 0, null, Match(ConfluenceCondition.LiquiditySweep, Direction.Bullish));
        Step(ctx, candidate, 1, Mss(Direction.Bullish),
                Match(ConfluenceCondition.DisplacementMss, Direction.Bullish),
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
                Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish))
            .Should().NotBeNull();

        // The standing filters still hold next candle, but the latched sweep + MSS are gone -> no duplicate.
        Step(ctx, candidate, 2, null,
                Match(ConfluenceCondition.BiasAligned, Direction.Bullish),
                Match(ConfluenceCondition.PremiumDiscountHalf, Direction.Bullish))
            .Should().BeNull();
        candidate.LockedDirection.Should().BeNull();
    }
}
