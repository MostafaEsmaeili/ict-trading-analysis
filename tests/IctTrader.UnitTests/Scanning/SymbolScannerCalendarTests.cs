using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Application.Scanning;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the slice-3 wiring: a <see cref="SymbolScanner"/> loads the shared <see cref="IEconomicCalendarStore"/>'s
/// events into its <c>MarketContext</c> so the §2.5.2 gate can fire — on the first candle once the store has loaded,
/// and again whenever the store's revision moves (a refresh). With no store wired the context is never loaded (the
/// gate stays fail-open), so the test/backtest path is byte-identical.
/// </summary>
public sealed class SymbolScannerCalendarTests
{
    private static readonly DateTimeOffset London = new(2024, 1, 8, 8, 0, 0, TimeSpan.Zero);

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
        Calendar = new CalendarOptions(),
        TradeStyles = new TradeStyleOptions(),
        TargetLadder = new TargetLadderOptions(),
        OpenPriceReference = new OpenPriceReferenceOptions(),
        MacroTime = new MacroTimeOptions(),
        CleanPriceAction = new CleanPriceActionOptions(),
        CalendarDriver = new CalendarDriverOptions(),
    };

    [Fact]
    public void Loads_the_store_events_into_the_context_on_the_first_candle()
    {
        var store = new EconomicCalendarStore();
        store.Load([new EconomicEvent(new DateOnly(2024, 1, 8), CalendarEventType.Fomc)]);
        var capture = new CalendarCaptureDetector();
        var scanner = NewScanner(store, capture);

        scanner.OnCandle(Candle(London));

        capture.IsCalendarLoaded.Should().BeTrue();
        capture.EventCount.Should().Be(1);
    }

    [Fact]
    public void Without_a_store_the_context_is_never_loaded_the_gate_stays_fail_open()
    {
        var capture = new CalendarCaptureDetector();
        var scanner = NewScanner(calendarStore: null, capture);

        scanner.OnCandle(Candle(London));

        capture.IsCalendarLoaded.Should().BeFalse();
        capture.EventCount.Should().Be(0);
    }

    [Fact]
    public void Reloads_when_the_store_revision_moves()
    {
        var store = new EconomicCalendarStore();
        store.Load([new EconomicEvent(new DateOnly(2024, 1, 8), CalendarEventType.Fomc)]);
        var capture = new CalendarCaptureDetector();
        var scanner = NewScanner(store, capture);

        scanner.OnCandle(Candle(London));
        capture.EventCount.Should().Be(1);

        // A refresh adds an event + bumps the revision → the next candle re-loads it into the same context.
        store.Load(
        [
            new EconomicEvent(new DateOnly(2024, 1, 8), CalendarEventType.Fomc),
            new EconomicEvent(new DateOnly(2024, 1, 12), CalendarEventType.Nfp),
        ]);

        scanner.OnCandle(Candle(London.AddMinutes(5)));
        capture.EventCount.Should().Be(2);
    }

    private static SymbolScanner NewScanner(IEconomicCalendarStore? calendarStore, CalendarCaptureDetector capture) =>
        new(
            new Symbol("EURUSD"),
            TradeStyle.Intraday,
            new FakeTimeProvider(London),
            Options,
            InstrumentCatalog.Default,
            prependDetectors: [capture],
            calendarStore: calendarStore);

    private static Candle Candle(DateTimeOffset openUtc) =>
        new(new Symbol("EURUSD"), Timeframe.M5, openUtc, 1.08m, 1.081m, 1.079m, 1.0805m, 100m);

    /// <summary>A non-scoring detector (Condition null → never counted) that records the context's calendar state
    /// each candle, so the test can assert the scanner loaded the store into the MarketContext.</summary>
    private sealed class CalendarCaptureDetector : ISetupDetector
    {
        public ConfluenceCondition? Condition => null;

        public bool IsCalendarLoaded { get; private set; }

        public int EventCount { get; private set; }

        public DetectorResult Detect(MarketContext context, Candle current)
        {
            IsCalendarLoaded = context.IsCalendarLoaded;
            EventCount = context.EconomicEvents.Count;
            return DetectorResult.NoMatch;
        }
    }
}
