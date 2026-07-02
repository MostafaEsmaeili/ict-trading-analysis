using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.MarketStructure;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Application;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.Scanning.Application.Signals;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks WP7 slice 2c — the Scanning module's scan loop on the in-memory bus (plan §4.1/§3.0a). It feeds a
/// <see cref="CandleIngested"/> through the REAL bus into the REAL <see cref="CandleIngestedHandler"/> →
/// <see cref="SymbolScanner"/> (the pinned domain pipeline + FSM + <see cref="SetupFactory"/>) and asserts a
/// <see cref="SetupConfirmed"/> is published whose <see cref="SetupDto"/> carries the EXPECTED direction, grade,
/// entry, stop, targets (in canonical wire order T1, runner, …) and reward-to-risk — proving the full path AND
/// the DTO mapping fidelity, not merely that something published.
///
/// <para>It reuses the proven <c>ScanSessionTests</c> "real confluence detectors" fixture: a non-scoring seeder
/// stages the structural arrays (range/displacement/FVG/sweep/MSS/pool) and three scripted confluence detectors
/// emit sweep/MSS/FVG, so the bias/PD/killzone/OTE/draw/calendar REAL detectors + the FSM do the grading. Those
/// seeders ride the <see cref="SymbolScanner"/>'s test-only <c>prependDetectors</c> seam (production passes none),
/// so the canonical real pipeline is unchanged.</para>
/// </summary>
public class ScanLoopTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly DateTimeOffset London = new(2024, 7, 1, 6, 30, 0, TimeSpan.Zero); // 02:30 NY London Open, discount

    // A discount London-Open M5 candle that does NOT fully-fill the seeded FVG [1.0828, 1.0836] — the low stays
    // at/above the gap bottom so the REAL FairValueGapDetector leaves the entry FVG alive (a single touch < 3 does
    // not void it). Close 1.0830 ≤ EQ 1.0850 ⇒ discount ⇒ bullish bias.
    private static readonly CandleDto ConfirmingCandle = new(
        Eurusd.Value, Timeframe.M5.ToString(), London,
        Open: 1.0830m, High: 1.0835m, Low: 1.0829m, Close: 1.0830m, Volume: 1m);

    [Fact]
    public async Task CandleIngested_drives_the_real_pipeline_and_publishes_a_mapped_SetupConfirmed()
    {
        var sink = new CapturedSetups();
        using var provider = BuildHost(sink);

        await provider.GetRequiredService<IMessageBus>().PublishAsync(new CandleIngested(ConfirmingCandle));

        sink.Setups.Should().ContainSingle();
        var setup = sink.Setups.Single();

        // The full path produced an advisory setup for the active (default Intraday) style, for this symbol.
        setup.Symbol.Should().Be(Eurusd.Value);
        setup.Style.Should().Be(TradeStyle.Intraday.ToString());
        setup.IsAdvisoryOnly.Should().BeTrue();

        // Enum wire fields carry the domain member NAMES (the frozen, language-neutral contract).
        setup.Direction.Should().Be(Direction.Bullish.ToString());
        setup.Grade.Should().BeOneOf(SetupGrade.A.ToString(), SetupGrade.B.ToString());
        setup.Killzone.Should().Be(Killzone.LondonOpen.ToString());
        setup.TriggerTimeframe.Should().Be(Timeframe.M5.ToString());

        // The priced plan maps faithfully: entry at the OTE array level, the gated draw as the runner.
        setup.Entry.Should().Be(1.0832m);
        setup.Stop.Should().BeLessThan(setup.Entry);
        setup.RewardRatio.Should().BeGreaterThanOrEqualTo(2.5m);

        // CANONICAL wire ordering: Targets[0] = T1 partial (entry→runner equilibrium), Targets[1] = the runner draw.
        setup.Targets.Should().HaveCount(2);
        setup.Targets[0].Should().Be(1.0876m);   // 1.0832 + 0.5 * (1.0920 - 1.0832)
        setup.Targets[1].Should().Be(1.0920m);   // the gated draw runner

        // Detection is stamped with the confirming candle's OPEN time (the identity of the signal bar, and the same
        // instant the PaperTrading seam opens the trade — so the no-same-bar-look-ahead filter stays calibrated).
        setup.DetectedAtUtc.Should().Be(London);
        setup.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_scan_loop_is_deterministic_for_the_same_candle()
    {
        var first = new CapturedSetups();
        using (var p1 = BuildHost(first))
        {
            await Publish(p1);
        }

        var second = new CapturedSetups();
        using (var p2 = BuildHost(second))
        {
            await Publish(p2);
        }

        first.Setups.Should().ContainSingle();
        second.Setups.Should().ContainSingle();
        // The whole DTO replays identically — INCLUDING the id, which is derived deterministically from the
        // setup's natural identity (so a redelivered candle is a free idempotency key for the consumer).
        second.Setups.Single().Should().BeEquivalentTo(first.Setups.Single());
        second.Setups.Single().Id.Should().Be(first.Setups.Single().Id);
    }

    private static Task Publish(ServiceProvider provider)
        => provider.GetRequiredService<IMessageBus>().PublishAsync(new CandleIngested(ConfirmingCandle));

    private static ServiceProvider BuildHost(CapturedSetups sink)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(London));
        services.AddSingleton(sink);
        services.AddScoped<IEventHandler<SetupConfirmed>, CapturingSetupHandler>();

        // The handler reads Ict:Scanning:ActiveStyles from MarketContextOptions (default [Intraday]).
        services.AddSingleton<IOptions<MarketContextOptions>>(Options.Create(new MarketContextOptions()));

        // The matrix router the handler uses to scan each active style on its canonical entry TF (default
        // Intraday → M5). Built over the default Ict:TradeStyles policy via the classifier (no hardcoded TF).
        services.AddSingleton(new StyleTimeframeMap(new TradeStyleClassifier(new TradeStyleOptions())));

        // The seeded scan: the registry is the real singleton; the factory prepends the proven structural seeders.
        services.AddSingleton<ISymbolScannerFactory>(new SeededScannerFactory(new FakeTimeProvider(London)));
        services.AddSingleton<IctTrader.Domain.Configuration.IRuntimeSettings>(
            new IctTrader.Domain.Configuration.RuntimeSettings()); // the registry watches it for live setting changes
        services.AddSingleton<ISymbolScannerRegistry, SymbolScannerRegistry>();

        // The recent-setup chart read-model: the Scanning.Application assembly also carries the
        // SetupConfirmedChartProjectionHandler (it subscribes to the SetupConfirmed this loop publishes), so the
        // bus fans the confirmed setup out to it too — it needs the store the production AddScanningModule registers.
        services.AddSingleton<RecentSetupStore>();
        // The live "engine view" geometry read-model the CandleIngestedHandler writes on every candle it scans.
        services.AddSingleton<GeometryOverlayStore>();

        // The signals feed read-model: the same assembly also carries the SetupConfirmedSignalFeedHandler (another
        // SetupConfirmed subscriber the bus fans out to), so the feed services it depends on must resolve too.
        var signalOptions = new SignalRankingOptions();
        services.AddSingleton(new SignalFeedStore(signalOptions));
        services.AddSingleton(new IctTrader.Domain.Confluence.SignalRanker(signalOptions));
        services.AddSingleton(sp => new SignalRankingService(
            sp.GetRequiredService<SignalFeedStore>(),
            sp.GetRequiredService<IctTrader.Domain.Confluence.SignalRanker>(),
            signalOptions));

        // Scan ONLY the Scanning.Application assembly for the production CandleIngestedHandler; the SetupConfirmed
        // sink is registered explicitly above (scanning the whole test assembly would pull in unrelated handlers).
        services.AddMessaging(typeof(CandleIngestedHandler).Assembly);

        return services.BuildServiceProvider();
    }

    private sealed class CapturedSetups
    {
        public List<SetupDto> Setups { get; } = [];
    }

    private sealed class CapturingSetupHandler(CapturedSetups captured) : IEventHandler<SetupConfirmed>
    {
        public Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
        {
            captured.Setups.Add(@event.Setup);
            return Task.CompletedTask;
        }
    }

    /// <summary>Builds a <see cref="SymbolScanner"/> over the DEFAULT validated options with the proven
    /// structural seeders prepended (the test-only seam) so the real bias/PD/OTE/draw detectors + FSM confirm.</summary>
    private sealed class SeededScannerFactory(TimeProvider timeProvider) : ISymbolScannerFactory
    {
        private static readonly ScannerOptions Options = new()
        {
            MarketContext = new MarketContextOptions(),
            Confluence = new ConfluenceOptions(),
            SetupCandidate = new SetupCandidateOptions(),
            Swing = new SwingOptions(),
            Liquidity = new LiquidityOptions(),
            Displacement = new DisplacementOptions(),
            MarketStructureShift = new MarketStructureShiftOptions(),
            Fvg = new FvgOptions(),
            OrderBlock = new OrderBlockOptions(),
            DailyBias = new DailyBiasOptions(),
            PremiumDiscount = new PremiumDiscountOptions(),
            Ote = new OteOptions(),
            DrawOnLiquidity = new DrawOnLiquidityOptions(),
            SdProjection = new SdProjectionOptions(),
            KillzoneEntry = new KillzoneEntryOptions(),
            SilverBullet = new SilverBulletOptions(),
            Calendar = new CalendarOptions(),
            TradeStyles = new TradeStyleOptions(),
            TargetLadder = new TargetLadderOptions(),
            OpenPriceReference = new OpenPriceReferenceOptions(),
            MacroTime = new MacroTimeOptions(),
            CleanPriceAction = new CleanPriceActionOptions(),
            CalendarDriver = new CalendarDriverOptions(),
        };

        public SymbolScanner Create(
            Symbol symbol, Timeframe timeframe, TradeStyle style,
            IctTrader.Domain.Configuration.ConfluenceOptions? confluence = null,
            SetupModel model = SetupModel.Ict2022)
            => new(
                symbol, timeframe, style, timeProvider,
                confluence is null ? Options : Options with { Confluence = confluence },
                InstrumentCatalog.Default, Seeders());

        private static ISetupDetector[] Seeders() =>
        [
            new ScriptedDetector(condition: null, (ctx, _) =>
            {
                ctx.SetDailyRange(new DealingRange(new Price(1.0800m), new Price(1.0900m), London)); // EQ 1.0850 -> discount
                ctx.SetDisplacement(new Displacement(Direction.Bullish, Timeframe.M5, new Price(1.0800m), new Price(1.0900m), London));
                ctx.RegisterFvg(new FairValueGap(Direction.Bullish, Timeframe.M5, new Price(1.0828m), new Price(1.0836m), London)); // mid 1.0832 in OTE band
                ctx.SetSweep(new SweepRecord(Direction.Bullish, 1.0810m, London, ctx.BarsProcessed));
                ctx.SetMarketStructureShift(new MarketStructureShift(Direction.Bullish, Timeframe.M5, new Price(1.0810m), new Price(1.0850m), London));
                ctx.RegisterLiquidityPool(new LiquidityPool(LiquiditySide.BuySide, new Price(1.0920m), 1, London)); // 2.75R draw
                return DetectorResult.NoMatch;
            }),
            new ScriptedDetector(ConfluenceCondition.LiquiditySweep, (_, _) => Bullish("sweep")),
            new ScriptedDetector(ConfluenceCondition.DisplacementMss, (_, _) => Bullish("mss")),
            new ScriptedDetector(ConfluenceCondition.FvgPresent, (_, _) => Bullish("fvg")),
        ];

        private static DetectorResult Bullish(string reason) => DetectorResult.Match(Direction.Bullish, 1.0850m, reason, null);
    }

    private sealed class ScriptedDetector(ConfluenceCondition? condition, Func<MarketContext, Candle, DetectorResult> detect)
        : ISetupDetector
    {
        public ConfluenceCondition? Condition => condition;

        public DetectorResult Detect(MarketContext context, Candle current) => detect(context, current);
    }
}
