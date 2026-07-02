using IctTrader.Domain.Configuration;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The Scanning module's bus seam (plan §3.0a/§4.1): it reacts to each <see cref="CandleIngested"/>, maps the
/// wire candle to the domain, and routes it to the stateful per-(symbol, timeframe, style)
/// <see cref="SymbolScanner"/> for every operator-active style whose canonical ENTRY timeframe matches the
/// candle's granularity (the <see cref="StyleTimeframeMap"/>) — so a multi-granularity feed scans every active
/// style on its own entry TF, and a candle whose TF no active style enters on is a no-op. On a confirmed advisory
/// <see cref="Domain.Setups.Setup"/> it publishes a <see cref="SetupConfirmed"/> carrying its <see cref="SetupDto"/>.
/// The handler ORCHESTRATES only — every decision (detection, confluence grading, pricing) lives in the pure
/// domain the scanner wraps; the mapping is pure. The registry is a SINGLETON (the scan state persists across
/// candles); this handler is the bus-scoped per-dispatch unit-of-work.
/// </summary>
public sealed class CandleIngestedHandler(
    ISymbolScannerRegistry registry,
    IMessageBus bus,
    IOptions<MarketContextOptions> scanningOptions,
    StyleTimeframeMap styleTimeframeMap,
    GeometryOverlayStore geometryStore,
    ILogger<CandleIngestedHandler>? logger = null)
    : IEventHandler<CandleIngested>
{
    private readonly ISymbolScannerRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly MarketContextOptions _scanning =
        (scanningOptions ?? throw new ArgumentNullException(nameof(scanningOptions))).Value;
    private readonly StyleTimeframeMap _styleTimeframeMap =
        styleTimeframeMap ?? throw new ArgumentNullException(nameof(styleTimeframeMap));
    private readonly GeometryOverlayStore _geometryStore =
        geometryStore ?? throw new ArgumentNullException(nameof(geometryStore));
    private readonly ILogger<CandleIngestedHandler> _logger = logger ?? NullLogger<CandleIngestedHandler>.Instance;

    public async Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var candle = CandleDtoMapper.ToDomain(@event.Candle);

        // Route this candle to the active styles whose canonical entry TF is its granularity — the matrix cell.
        // A TF no active style enters on yields an empty set, so the candle is simply not scanned (no-op).
        foreach (var style in _styleTimeframeMap.StylesFor(candle.Timeframe, _scanning.ResolvedActiveStyles))
        {
            await ScanCellAsync(candle, style, cancellationToken).ConfigureAwait(false);
        }
    }

    // One (symbol, timeframe, style) matrix cell, scanned once per ACTIVE setup model (plan §16) — each model
    // holds its own registry-keyed scanner, so two models on the same cell confirm independent setups.
    private async Task ScanCellAsync(Candle candle, TradeStyle style, CancellationToken cancellationToken)
    {
        foreach (var model in _scanning.ResolvedActiveModels)
        {
            var scanner = _registry.GetOrCreate(candle.Symbol, candle.Timeframe, style, model);
            var setup = scanner.OnCandle(candle);

            // Refresh the live "engine view" geometry for this (symbol, timeframe) EVERY candle — not just on a
            // confirmation — so the chart's concept toggles show what the scanner is tracking right now (open FVGs /
            // OBs / liquidity, the latest sweep / MSS, the OTE band), even between the rare confirmed setups (plan
            // §9.1). Captured on the scan thread (sequential per dispatch) as an immutable snapshot; a read-only sink.
            _geometryStore.Set(candle.Symbol.Value, candle.Timeframe, scanner.SnapshotGeometry());

            if (setup is null)
            {
                continue;
            }

            // Detection is stamped with the confirming candle's OPEN time, by design — it is the identity of the bar
            // that produced the signal, and it is the SAME instant the PaperTrading seam uses to OPEN/ARM the trade
            // (setup.ConfirmedAtUtc == DetectedAtUtc) so the no-same-bar-look-ahead filter (a position is managed only
            // from the bar AFTER its open bar) is calibrated to it. It also feeds the DeterministicId hash, so the open
            // time keeps "same candle → same id" stable. (The alert feed / chart overlay therefore show the bar-open
            // instant; that one-bar display offset is accepted to keep the open-stamp and the look-ahead filter aligned.)
            // The killzone is the scanner's session for this candle.
            var dto = SetupDtoMapper.ToDto(setup, scanner.CurrentKillzone, candle.OpenTimeUtc);

            // Observability (WP7): a confirmed advisory setup is a notable, infrequent event — surface it so an
            // operator (and a backtest) can see the scanner working without per-candle noise. Advisory only.
            _logger.LogInformation(
                "Setup confirmed: {Symbol} {Style} {Direction} grade {Grade} entry {Entry} stop {Stop} RR {RewardRatio} @ {DetectedAtUtc:o}",
                dto.Symbol, dto.Style, dto.Direction, dto.Grade, dto.Entry, dto.Stop, dto.RewardRatio, dto.DetectedAtUtc);

            await _bus.PublishAsync(new SetupConfirmed(dto), cancellationToken).ConfigureAwait(false);
        }
    }
}
