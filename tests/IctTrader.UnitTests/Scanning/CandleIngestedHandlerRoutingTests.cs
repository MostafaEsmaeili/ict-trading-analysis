using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the matrix ROUTING in <see cref="CandleIngestedHandler"/>: a candle is scanned only by the active styles
/// whose canonical entry timeframe is its granularity (the <see cref="StyleTimeframeMap"/>), each in its own
/// (symbol, timeframe, style) cell — so a multi-granularity feed scans every active style on its own entry TF and
/// a no-match timeframe is a no-op. The single-TF default ([Intraday] over an M5 candle) is byte-identical to
/// the prior behaviour: exactly the (symbol, M5, Intraday) cell.
/// </summary>
public sealed class CandleIngestedHandlerRoutingTests
{
    private static readonly StyleTimeframeMap Map = new(new TradeStyleClassifier(new TradeStyleOptions()));

    [Fact]
    public async Task An_M5_candle_with_Intraday_active_scans_exactly_the_M5_Intraday_cell()
    {
        // The default active set is [Intraday] (entry TF M5) — byte-identical to the prior single-TF behaviour.
        var registry = new RecordingRegistry();
        var handler = NewHandler(registry, new MarketContextOptions());

        await handler.HandleAsync(new CandleIngested(Candle("EURUSD", Timeframe.M5)));

        registry.Requested.Should().ContainSingle()
            .Which.Should().Be(("EURUSD", Timeframe.M5, TradeStyle.Intraday));
    }

    [Fact]
    public async Task An_M1_candle_with_Scalp_and_Intraday_active_scans_only_the_Scalp_cell()
    {
        // Both Scalp (entry M1) and Intraday (entry M5) are active; an M1 candle maps to Scalp ONLY.
        var registry = new RecordingRegistry();
        var options = new MarketContextOptions { ActiveStyles = [TradeStyle.Scalp, TradeStyle.Intraday] };
        var handler = NewHandler(registry, options);

        await handler.HandleAsync(new CandleIngested(Candle("EURUSD", Timeframe.M1)));

        registry.Requested.Should().ContainSingle()
            .Which.Should().Be(("EURUSD", Timeframe.M1, TradeStyle.Scalp));
    }

    [Fact]
    public async Task A_timeframe_no_active_style_enters_on_is_a_no_op()
    {
        // H1 is no active style's entry TF, so the handler creates no scanner for an H1 candle.
        var registry = new RecordingRegistry();
        var options = new MarketContextOptions { ActiveStyles = [TradeStyle.Scalp, TradeStyle.Intraday] };
        var handler = NewHandler(registry, options);

        await handler.HandleAsync(new CandleIngested(Candle("EURUSD", Timeframe.H1)));

        registry.Requested.Should().BeEmpty();
    }

    private static CandleIngestedHandler NewHandler(RecordingRegistry registry, MarketContextOptions options) =>
        new(registry, new NoOpBus(), Options.Create(options), Map, new GeometryOverlayStore());

    private static CandleDto Candle(string symbol, Timeframe timeframe) => new(
        symbol, timeframe.ToString(), new DateTimeOffset(2024, 7, 1, 6, 30, 0, TimeSpan.Zero),
        Open: 1.08m, High: 1.081m, Low: 1.079m, Close: 1.0805m, Volume: 1m);

    /// <summary>Records each (symbol, timeframe, style) the handler asks for, returning a real default scanner that
    /// never confirms from a single candle (so the routing — not the FSM — is what is asserted).</summary>
    private sealed class RecordingRegistry : ISymbolScannerRegistry
    {
        public List<(string Symbol, Timeframe Timeframe, TradeStyle Style)> Requested { get; } = [];

        public SymbolScanner GetOrCreate(
            Symbol symbol, Timeframe timeframe, TradeStyle style, SetupModel model = SetupModel.Ict2022)
        {
            Requested.Add((symbol.Value, timeframe, style));
            return new SymbolScanner(
                symbol, timeframe, style, new FakeTimeProvider(), DefaultOptions(), InstrumentCatalog.Default);
        }

        private static ScannerOptions DefaultOptions() => new()
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
    }

    private sealed class NoOpBus : IMessageBus
    {
        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand => Task.CompletedTask;

        public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IEvent => Task.CompletedTask;
    }
}
