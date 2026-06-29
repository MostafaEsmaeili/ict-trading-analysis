using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Application.Scanning;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the matrix registry: the SINGLETON cache now keys per (symbol, timeframe, style), so a multi-granularity
/// feed never mixes timeframes into one scanner's FSM. Distinct keys get distinct instances; the same key returns
/// the cached instance; a runtime-settings revision change evicts and rebuilds.
/// </summary>
public sealed class SymbolScannerRegistryTests
{
    private static readonly Symbol Eurusd = new("EURUSD");
    private static readonly Symbol Gbpusd = new("GBPUSD");

    [Fact]
    public void Distinct_symbol_timeframe_style_keys_get_distinct_instances()
    {
        var factory = new CountingScannerFactory();
        var registry = new SymbolScannerRegistry(factory, new RuntimeSettings());

        var a = registry.GetOrCreate(Eurusd, Timeframe.M5, TradeStyle.Intraday);
        var bByTimeframe = registry.GetOrCreate(Eurusd, Timeframe.M1, TradeStyle.Intraday);
        var cByStyle = registry.GetOrCreate(Eurusd, Timeframe.M5, TradeStyle.Scalp);
        var dBySymbol = registry.GetOrCreate(Gbpusd, Timeframe.M5, TradeStyle.Intraday);

        new[] { a, bByTimeframe, cByStyle, dBySymbol }.Should().OnlyHaveUniqueItems();
        factory.CreateCount.Should().Be(4);
    }

    [Fact]
    public void Same_key_returns_the_same_cached_instance()
    {
        var factory = new CountingScannerFactory();
        var registry = new SymbolScannerRegistry(factory, new RuntimeSettings());

        var first = registry.GetOrCreate(Eurusd, Timeframe.M5, TradeStyle.Intraday);
        var second = registry.GetOrCreate(Eurusd, Timeframe.M5, TradeStyle.Intraday);

        second.Should().BeSameAs(first);
        factory.CreateCount.Should().Be(1);
    }

    [Fact]
    public void A_settings_revision_change_evicts_and_rebuilds()
    {
        var factory = new CountingScannerFactory();
        var settings = new RuntimeSettings();
        var registry = new SymbolScannerRegistry(factory, settings);

        var before = registry.GetOrCreate(Eurusd, Timeframe.M5, TradeStyle.Intraday);

        // Bump the revision (a live per-instrument settings change) → the cache drops and the next get rebuilds.
        settings.SetInstrumentOverride("EURUSD", new InstrumentOptionOverrides { MinRequiredConditions = 6 });

        var after = registry.GetOrCreate(Eurusd, Timeframe.M5, TradeStyle.Intraday);

        after.Should().NotBeSameAs(before);
        factory.CreateCount.Should().Be(2);
    }

    /// <summary>A real-scanner factory that counts its Create calls so the test can assert cache behaviour.</summary>
    private sealed class CountingScannerFactory : ISymbolScannerFactory
    {
        public int CreateCount { get; private set; }

        public SymbolScanner Create(Symbol symbol, Timeframe timeframe, TradeStyle style, ConfluenceOptions? confluence = null)
        {
            CreateCount++;
            return new SymbolScanner(
                symbol, timeframe, style, new FakeTimeProvider(), DefaultOptions(confluence), InstrumentCatalog.Default);
        }

        private static ScannerOptions DefaultOptions(ConfluenceOptions? confluence) => new()
        {
            MarketContext = new MarketContextOptions(),
            Confluence = confluence ?? new ConfluenceOptions(),
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
    }
}
