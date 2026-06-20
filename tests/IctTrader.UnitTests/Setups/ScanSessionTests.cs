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

namespace IctTrader.UnitTests.Setups;

/// <summary>
/// Locks the scan driver (plan §4.1): it runs the pinned detector pipeline, feeds the matches into the
/// SetupCandidate, confirms a graded setup when the sequence completes, resets the in-flight candidate across
/// a NY-day rollover or killzone change, and — because the pipeline runs SwingPointDetector before the MSS —
/// still fires a legitimate MSS on a swing the breach already marked (spec §5 item 19).
/// </summary>
public class ScanSessionTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));

    private static readonly ConfluenceCondition[] Required =
    [
        ConfluenceCondition.DisplacementMss, ConfluenceCondition.LiquiditySweep,
        ConfluenceCondition.BiasAligned, ConfluenceCondition.PremiumDiscountHalf,
    ];

    private static MarketContext NewContext() => new(
        SymbolSpec.FxMajor(Eurusd),
        new KillzoneClock(new NyClock(Time), KillzoneSchedule.CreateDefault()),
        new MarketContextOptions());

    private static SetupCandidate NewCandidate()
    {
        var confluence = new ConfluenceOptions
        {
            Weights = Required.ToDictionary(c => c, _ => 1.0m),
            RequiredConditions = Required,
            AlertMinimumGrade = SetupGrade.B,
        };
        return new SetupCandidate(confluence, new SetupCandidateOptions(), new SetupScorer(confluence));
    }

    private static Candle CandleAt(DateTimeOffset openUtc, decimal close = 1.0850m) =>
        new(Eurusd, Timeframe.M5, openUtc, 1.0850m, Math.Max(1.0850m, close) + 0.0005m, Math.Min(1.0850m, close) - 0.0005m, close, 1m);

    private static DetectorResult Bullish(string reason) => DetectorResult.Match(Direction.Bullish, 1.0850m, reason, null);

    private sealed class ScriptedDetector(ConfluenceCondition? condition, Func<MarketContext, Candle, DetectorResult> detect)
        : ISetupDetector
    {
        public ConfluenceCondition? Condition => condition;

        public DetectorResult Detect(MarketContext context, Candle current) => detect(context, current);
    }

    private static ScriptedDetector SweepOnFirstBar() => new(
        ConfluenceCondition.LiquiditySweep,
        (ctx, _) => ctx.BarsProcessed == 1 ? Bullish("sweep") : DetectorResult.NoMatch);

    [Fact]
    public void Confirms_a_setup_when_the_pipeline_completes_the_sequence()
    {
        var ctx = NewContext();
        var detectors = new ISetupDetector[]
        {
            SweepOnFirstBar(),
            new ScriptedDetector(ConfluenceCondition.DisplacementMss, (c, candle) =>
            {
                if (c.BarsProcessed != 2)
                {
                    return DetectorResult.NoMatch;
                }

                c.SetMarketStructureShift(new MarketStructureShift(
                    Direction.Bullish, candle.Timeframe, new Price(1.0840m), new Price(candle.Close), candle.OpenTimeUtc));
                return Bullish("mss");
            }),
            new ScriptedDetector(ConfluenceCondition.BiasAligned, (_, _) => Bullish("bias")),
            new ScriptedDetector(ConfluenceCondition.PremiumDiscountHalf, (_, _) => Bullish("pd")),
        };
        var session = new ScanSession(ctx, detectors, NewCandidate(), new SetupCandidateOptions());

        // 06:30 / 06:35 UTC = 02:30 NY = London Open (stable killzone, same NY day).
        var london = new DateTimeOffset(2024, 7, 1, 6, 30, 0, TimeSpan.Zero);
        session.OnCandle(CandleAt(london)).Should().BeNull(); // only the sweep so far

        var confirmation = session.OnCandle(CandleAt(london.AddMinutes(5)));

        confirmation.Should().NotBeNull();
        confirmation!.Direction.Should().Be(Direction.Bullish);
        confirmation.Grade.Should().Be(SetupGrade.A);
        confirmation.Symbol.Should().Be(Eurusd);
    }

    [Fact]
    public void Resets_the_in_flight_candidate_across_a_new_york_day_rollover()
    {
        var ctx = NewContext();
        var session = new ScanSession(
            ctx, [SweepOnFirstBar()], NewCandidate(),
            new SetupCandidateOptions { ResetOnKillzoneChange = false }); // isolate the rollover trigger

        // 03:30 UTC = 23:30 NY on Jun-30; 04:30 UTC = 00:30 NY on Jul-01 -> the financial day rolls over.
        session.OnCandle(CandleAt(new DateTimeOffset(2024, 7, 1, 3, 30, 0, TimeSpan.Zero)));
        session.Candidate.HasActivity.Should().BeTrue(); // sweep latched on day one

        session.OnCandle(CandleAt(new DateTimeOffset(2024, 7, 1, 4, 30, 0, TimeSpan.Zero)));
        session.Candidate.HasActivity.Should().BeFalse(); // rollover cleared the stale sweep
    }

    [Fact]
    public void Resets_the_in_flight_candidate_when_the_active_killzone_changes()
    {
        var ctx = NewContext();
        var session = new ScanSession(ctx, [SweepOnFirstBar()], NewCandidate(), new SetupCandidateOptions());

        // 06:30 UTC = 02:30 NY (London Open) then 09:30 UTC = 05:30 NY (dead time) on the same NY day.
        session.OnCandle(CandleAt(new DateTimeOffset(2024, 7, 1, 6, 30, 0, TimeSpan.Zero)));
        session.Candidate.HasActivity.Should().BeTrue();

        session.OnCandle(CandleAt(new DateTimeOffset(2024, 7, 1, 9, 30, 0, TimeSpan.Zero)));
        session.Candidate.HasActivity.Should().BeFalse(); // leaving the killzone cleared the candidate
    }

    [Fact]
    public void The_pinned_pipeline_still_fires_the_mss_after_the_swing_detector_breaches_the_swing()
    {
        // SwingPointDetector runs BEFORE the MSS in the canonical order and breaches the swing the displacement
        // closes through; the MSS must still fire on that same-candle-breached swing (spec §5 item 19).
        var ctx = NewContext();
        var seeder = new ScriptedDetector(condition: null, (c, candle) =>
        {
            if (c.SwingPoints.Count == 0)
            {
                c.RegisterSwingPoint(new SwingPoint(SwingKind.High, candle.Timeframe, new Price(1.0900m), candle.OpenTimeUtc));
            }

            c.SetDisplacement(new Displacement(Direction.Bullish, candle.Timeframe, new Price(1.0890m), new Price(candle.High), candle.OpenTimeUtc));
            c.SetSweep(new SweepRecord(Direction.Bullish, 1.0850m, candle.OpenTimeUtc, c.BarsProcessed));
            return DetectorResult.NoMatch;
        });
        var detectors = new ISetupDetector[]
        {
            seeder,
            new SwingPointDetector(new SwingOptions()),               // breaches the 1.0900 swing this candle
            new MarketStructureShiftDetector(new MarketStructureShiftOptions()),
        };
        var session = new ScanSession(ctx, detectors, NewCandidate(), new SetupCandidateOptions());

        session.OnCandle(CandleAt(new DateTimeOffset(2024, 7, 1, 6, 30, 0, TimeSpan.Zero), close: 1.0920m));

        ctx.SwingPoints.Single().State.Should().Be(SwingState.Breached); // breached this candle, claimed anyway
        ctx.LastMss.Should().NotBeNull();
        ctx.LastMss!.IsConfirmed.Should().BeTrue();
        ctx.LastMss.Direction.Should().Be(Direction.Bullish);
    }

    [Fact]
    public void The_real_confluence_detectors_confirm_a_graded_setup_end_to_end()
    {
        // The milestone: with all §2.5.2 RequiredConditions present, the real bias/PD/killzone/OTE/draw/calendar
        // detectors + the FSM grade and confirm a setup under the DEFAULT weights. The structural events
        // (sweep/MSS/FVG, tested in isolation) are seeded so this exercises the confluence-reading layer.
        var ctx = NewContext();
        var london = new DateTimeOffset(2024, 7, 1, 6, 30, 0, TimeSpan.Zero); // 02:30 NY London Open, discount close
        var candle = new Candle(Eurusd, Timeframe.M5, london, 1.0830m, 1.0835m, 1.0825m, 1.0830m, 1m);

        var seeder = new ScriptedDetector(condition: null, (c, _) =>
        {
            c.SetDailyRange(new DealingRange(new Price(1.0800m), new Price(1.0900m), london)); // EQ 1.0850 -> discount
            c.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), london));
            c.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0836m), london)); // mid 1.0832 in OTE band
            c.SetSweep(new SweepRecord(Direction.Bullish, 1.0810m, london, c.BarsProcessed));
            c.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), london));
            c.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(1.0920m), 1, london)); // 2.75R draw
            return DetectorResult.NoMatch;
        });

        var detectors = new ISetupDetector[]
        {
            seeder,
            new ScriptedDetector(ConfluenceCondition.LiquiditySweep, (_, _) => Bullish("sweep")),
            new ScriptedDetector(ConfluenceCondition.DisplacementMss, (_, _) => Bullish("mss")),
            new ScriptedDetector(ConfluenceCondition.FvgPresent, (_, _) => Bullish("fvg")),
            new KillzoneEntryDetector(new KillzoneEntryOptions()),
            new DailyBiasDetector(new DailyBiasOptions()),
            new PremiumDiscountGateDetector(new PremiumDiscountOptions()),
            new OteFibDetector(new OteOptions()),
            new DrawOnLiquidityDetector(new DrawOnLiquidityOptions(), new OteOptions(), new TradeStyleOptions()),
            new CalendarGateDetector(new CalendarOptions()),
        };
        var confluence = new ConfluenceOptions();
        var candidate = new SetupCandidate(confluence, new SetupCandidateOptions(), new SetupScorer(confluence));
        var session = new ScanSession(ctx, detectors, candidate, new SetupCandidateOptions());

        var confirmation = session.OnCandle(candle);

        confirmation.Should().NotBeNull();
        confirmation!.Direction.Should().Be(Direction.Bullish);
        confirmation.Grade.Should().BeOneOf(SetupGrade.A, SetupGrade.B); // all 8 required + OTE -> >= B floor
        confirmation.Confluences.Select(c => c.Condition).Should().Contain(
        [
            ConfluenceCondition.KillzoneEntry, ConfluenceCondition.DrawTargetRrMet,
            ConfluenceCondition.BiasAligned, ConfluenceCondition.PremiumDiscountHalf, ConfluenceCondition.CalendarClear,
        ]);
    }
}
