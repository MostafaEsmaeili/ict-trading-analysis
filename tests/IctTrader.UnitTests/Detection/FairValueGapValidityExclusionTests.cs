using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Detection;

/// <summary>
/// Locks FVG-SEM-3 (§2.5.10 validity-exclusion additions): the five exclusion predicates are computed on
/// every FVG formation and attached as FLAG-ONLY evidence (independent of the flag), and — only when
/// <see cref="FvgOptions.ApplyValidityExclusions"/> is on — the detector vetoes solely on the two
/// FSM-unowned exclusions (Asian-range / overlapping-wicks). The other three (no-sweep / counter-bias /
/// no-CHoCH) are already FSM RequiredConditions, so they annotate but never veto here.
/// The default (OFF) path stays byte-identical: the gap matches exactly as before, plus six inert keys.
/// </summary>
public class FairValueGapValidityExclusionTests
{
    private static readonly Symbol Eurusd = new("EURUSD");

    // 2024-07-01 (US DST, EDT = UTC-4). 07:00 UTC = 03:00 NY = London Open (an FX session, NOT Asian).
    private static readonly DateTimeOffset LondonBase = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    // 23:00 UTC = 19:00 NY = Asian session start (19:00-00:00).
    private static readonly DateTimeOffset AsianBase = new(2024, 7, 1, 23, 0, 0, TimeSpan.Zero);

    private static readonly FakeTimeProvider Time = new(LondonBase);

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static Candle Candle(DateTimeOffset at, decimal open, decimal high, decimal low, decimal close)
        => new(Eurusd, Timeframe.M5, at, open, high, low, close, 1m);

    // A 3-candle bullish gap with bottom 1.0820 / top 1.0830, formed at the middle candle.
    private static DetectorResult FeedBullishGap(MarketContext ctx, FairValueGapDetector detector, DateTimeOffset start)
    {
        ctx.Append(Candle(start, 1.0815m, 1.0820m, 1.0810m, 1.0818m));
        detector.Detect(ctx, Candle(start, 1.0815m, 1.0820m, 1.0810m, 1.0818m));

        var middle = Candle(start.AddMinutes(5), 1.0828m, 1.0850m, 1.0825m, 1.0845m);
        ctx.Append(middle);
        detector.Detect(ctx, middle);

        var c3 = Candle(start.AddMinutes(10), 1.0835m, 1.0860m, 1.0830m, 1.0855m);
        ctx.Append(c3);
        return detector.Detect(ctx, c3);
    }

    // The displacement leg whose 50% (1.0835) keeps the bullish gap top (1.0830) in DISCOUNT (a confluence).
    private static void SeedDiscountDisplacement(MarketContext ctx) =>
        ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0860m), LondonBase));

    // A precedent bullish sweep STRICTLY before the gap's FormedAtUtc (= the middle candle open).
    private static void SeedPrecedentBullishSweep(MarketContext ctx, DateTimeOffset start) =>
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0810m, start, ctx.BarsProcessed));

    private static void SeedConfirmedBullishMss(MarketContext ctx) =>
        ctx.SetMarketStructureShift(new MarketStructureShift(
            Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), LondonBase));

    private static bool Excluded(DetectorResult result, string key)
        => (bool)result.Evidence![key];

    // 1. Clean in-dir sweep + bias + MSS, FX session, OFF -> match; all five excluded*=false; any=false.
    [Fact]
    public void All_clear_off_matches_and_all_exclusions_are_false()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, LondonBase);

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoSweep).Should().BeFalse();
        Excluded(result, EvidenceKeys.ExcludedAsianRange).Should().BeFalse();
        Excluded(result, EvidenceKeys.ExcludedCounterBias).Should().BeFalse();
        Excluded(result, EvidenceKeys.ExcludedNoChoch).Should().BeFalse();
        Excluded(result, EvidenceKeys.ExcludedOverlappingWicks).Should().BeFalse();
        Excluded(result, EvidenceKeys.AnyValidityExclusion).Should().BeFalse();
    }

    // 2. No precedent sweep, OFF -> match unchanged; excludedNoSweep=true.
    [Fact]
    public void No_precedent_sweep_off_still_matches_and_flags_no_sweep()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        // no sweep seeded

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoSweep).Should().BeTrue();
        Excluded(result, EvidenceKeys.AnyValidityExclusion).Should().BeTrue();
    }

    // A sweep that lands at/after the gap's formation does NOT precede it -> still excluded.
    [Fact]
    public void A_sweep_not_strictly_preceding_the_gap_flags_no_sweep()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        // sweep stamped at the middle candle open (= FormedAtUtc) -> AtUtc >= fAt, not strictly before
        ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0810m, LondonBase.AddMinutes(5), ctx.BarsProcessed));

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoSweep).Should().BeTrue();
    }

    // 3. FVG dir opposes ctx.Bias, OFF -> excludedCounterBias=true (match per current rules).
    [Fact]
    public void Counter_bias_off_still_matches_and_flags_counter_bias()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bearish); // opposes the bullish gap
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, LondonBase);

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedCounterBias).Should().BeTrue();
        Excluded(result, EvidenceKeys.AnyValidityExclusion).Should().BeTrue();
    }

    // counter-bias is also TRUE when no bias is set at all (ctx.Bias is null).
    [Fact]
    public void No_bias_flags_counter_bias()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, LondonBase);
        // no bias seeded

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedCounterBias).Should().BeTrue();
    }

    // 4. No confirmed in-dir MSS, OFF -> excludedNoChoch=true (match unchanged).
    [Fact]
    public void No_confirmed_in_direction_mss_off_still_matches_and_flags_no_choch()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedPrecedentBullishSweep(ctx, LondonBase);
        // no MSS seeded

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoChoch).Should().BeTrue();
        Excluded(result, EvidenceKeys.AnyValidityExclusion).Should().BeTrue();
    }

    // An invalidated MSS is not a confirmed CHoCH -> still flagged.
    [Fact]
    public void An_invalidated_mss_flags_no_choch()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedPrecedentBullishSweep(ctx, LondonBase);
        var mss = new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), LondonBase);
        mss.Invalidate();
        ctx.SetMarketStructureShift(mss);

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoChoch).Should().BeTrue();
    }

    // 5. Candle classified Killzone.Asian, OFF -> excludedAsianRange=true (match unchanged).
    [Fact]
    public void Asian_session_off_still_matches_and_flags_asian_range()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, AsianBase);

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), AsianBase);

        ctx.Session.Killzone.Should().Be(Killzone.Asian); // the c3 candle classified Asian
        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedAsianRange).Should().BeTrue();
        Excluded(result, EvidenceKeys.AnyValidityExclusion).Should().BeTrue();
    }

    // 6. Any FVG, OFF/ON -> excludedOverlappingWicks=false (always; tautologically false at formation).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overlapping_wicks_is_always_false_at_formation(bool applyExclusions)
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, LondonBase);

        var result = FeedBullishGap(
            ctx, new FairValueGapDetector(new FvgOptions { ApplyValidityExclusions = applyExclusions }), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedOverlappingWicks).Should().BeFalse();
    }

    // 7. Asian-session FVG, ON -> vetoed (no match), but the veto STILL carries the diagnostic evidence so the
    // operator can see WHY the FVG was suppressed.
    [Fact]
    public void Asian_session_on_vetoes_the_fvg_but_keeps_the_diagnostic()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, AsianBase);

        var result = FeedBullishGap(
            ctx, new FairValueGapDetector(new FvgOptions { ApplyValidityExclusions = true }), AsianBase);

        result.Matched.Should().BeFalse();                                       // vetoed -> FvgPresent not counted
        result.Evidence.Should().NotBeNull();                                    // but the diagnostic survives the veto
        Excluded(result, EvidenceKeys.ExcludedAsianRange).Should().BeTrue();
        Excluded(result, EvidenceKeys.AnyValidityExclusion).Should().BeTrue();
        ctx.OpenFvgs.Should().ContainSingle(); // still registered as an array
    }

    // FVG-SEM-3 (CodeRabbit #66): Asian-range is classified from the gap's OWN formation candle (the middle), not the
    // ambient session of the latest candle. A gap that FORMS pre-Asian but whose c3 ticks into the Asian window is NOT
    // an Asian-range exclusion.
    [Fact]
    public void Asian_range_is_classified_from_the_formation_candle_not_the_latest()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        // c1 18:50 NY, middle 18:55 NY (both pre-Asian "None"), c3 19:00 NY (Asian start). 18:50 NY = 22:50 UTC (EDT).
        var start = new DateTimeOffset(2024, 7, 1, 22, 50, 0, TimeSpan.Zero);
        SeedPrecedentBullishSweep(ctx, start);

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), start);

        ctx.Session.Killzone.Should().Be(Killzone.Asian);                          // the LATEST candle (c3) is Asian
        ctx.KillzoneAt(start.AddMinutes(5)).Should().NotBe(Killzone.Asian);        // but the formation candle is not
        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedAsianRange).Should().BeFalse();      // classified by formation, not c3
    }

    // 8. No-sweep / counter-bias / no-CHoCH FVG, FX session, ON -> match (NOT vetoed — the FSM owns these).
    [Fact]
    public void No_sweep_on_is_not_vetoed_fsm_owns_it()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedConfirmedBullishMss(ctx);
        // no sweep

        var result = FeedBullishGap(
            ctx, new FairValueGapDetector(new FvgOptions { ApplyValidityExclusions = true }), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoSweep).Should().BeTrue();
    }

    [Fact]
    public void Counter_bias_on_is_not_vetoed_fsm_owns_it()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bearish);
        SeedConfirmedBullishMss(ctx);
        SeedPrecedentBullishSweep(ctx, LondonBase);

        var result = FeedBullishGap(
            ctx, new FairValueGapDetector(new FvgOptions { ApplyValidityExclusions = true }), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedCounterBias).Should().BeTrue();
    }

    [Fact]
    public void No_choch_on_is_not_vetoed_fsm_owns_it()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);
        ctx.SetBias(Direction.Bullish);
        SeedPrecedentBullishSweep(ctx, LondonBase);
        // no MSS

        var result = FeedBullishGap(
            ctx, new FairValueGapDetector(new FvgOptions { ApplyValidityExclusions = true }), LondonBase);

        result.Matched.Should().BeTrue();
        Excluded(result, EvidenceKeys.ExcludedNoChoch).Should().BeTrue();
    }

    // The OFF default path is byte-identical: an in-discount gap still matches with the same direction/key level.
    [Fact]
    public void Off_default_path_is_byte_identical_for_the_match()
    {
        var ctx = NewContext();
        SeedDiscountDisplacement(ctx);

        var result = FeedBullishGap(ctx, new FairValueGapDetector(new FvgOptions()), LondonBase);

        result.Matched.Should().BeTrue();
        result.Direction.Should().Be(Direction.Bullish);
        result.KeyLevel.Should().Be(1.0830m);
    }

    // 10. The new evidence keys are SCORING-INERT: the FSM reads only Condition/Direction/KeyLevel/ReasonFragment
    // off a match (PricedFrame is read only on DrawTargetRrMet), so an FVG match carrying the six new keys must
    // fold to the IDENTICAL grade/score/matched-set as the same match without them.
    [Fact]
    public void The_new_fvg_evidence_keys_are_scoring_inert()
    {
        var withKeys = Drive(fvgEvidence: ExclusionEvidence());
        var withoutKeys = Drive(fvgEvidence: null);

        withKeys.Should().NotBeNull();
        withoutKeys.Should().NotBeNull();
        withKeys!.Grade.Should().Be(withoutKeys!.Grade);
        withKeys.Score.Should().Be(withoutKeys.Score);
        withKeys.Confluences.Select(c => c.Condition)
            .Should().BeEquivalentTo(withoutKeys.Confluences.Select(c => c.Condition));
    }

    // Folds a minimal sweep -> MSS -> bias -> FVG stream through the FSM; the FVG match optionally carries the
    // six new (inert) evidence keys.
    private static SetupConfirmation? Drive(IReadOnlyDictionary<string, object>? fvgEvidence)
    {
        ConfluenceCondition[] required =
        [
            ConfluenceCondition.DisplacementMss,
            ConfluenceCondition.LiquiditySweep,
            ConfluenceCondition.BiasAligned,
            ConfluenceCondition.FvgPresent,
        ];
        var confluence = new ConfluenceOptions
        {
            Weights = required.ToDictionary(c => c, _ => 1.0m),
            RequiredConditions = required,
            AlertMinimumGrade = SetupGrade.B,
        };
        var candidate = new SetupCandidate(confluence, new SetupCandidateOptions(), new SetupScorer(confluence));
        var ctx = NewContext();

        // Bar 1: the sweep (must strictly precede the MSS).
        var b1 = Candle(LondonBase, 1.0850m, 1.0855m, 1.0845m, 1.0850m);
        ctx.Append(b1);
        candidate.Observe(ctx, b1,
            [new ConfluenceMatch(ConfluenceCondition.LiquiditySweep, DetectorResult.Match(Direction.Bullish, 1.0850m, "sweep", null))]);

        // Bar 2: MSS (locks the direction) + bias + the FVG match (with or without the new evidence keys).
        var b2 = Candle(LondonBase.AddMinutes(5), 1.0850m, 1.0855m, 1.0845m, 1.0850m);
        ctx.Append(b2);
        ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0840m), new Price(1.0855m), b2.OpenTimeUtc));
        return candidate.Observe(ctx, b2,
        [
            new ConfluenceMatch(ConfluenceCondition.DisplacementMss, DetectorResult.Match(Direction.Bullish, 1.0850m, "mss", null)),
            new ConfluenceMatch(ConfluenceCondition.BiasAligned, DetectorResult.Match(Direction.Bullish, 1.0850m, "bias", null)),
            new ConfluenceMatch(ConfluenceCondition.FvgPresent, DetectorResult.Match(Direction.Bullish, 1.0830m, "fvg", fvgEvidence)),
        ]);
    }

    private static Dictionary<string, object> ExclusionEvidence() => new()
    {
        [EvidenceKeys.ExcludedNoSweep] = false,
        [EvidenceKeys.ExcludedAsianRange] = false,
        [EvidenceKeys.ExcludedCounterBias] = false,
        [EvidenceKeys.ExcludedNoChoch] = false,
        [EvidenceKeys.ExcludedOverlappingWicks] = false,
        [EvidenceKeys.AnyValidityExclusion] = false,
    };
}
