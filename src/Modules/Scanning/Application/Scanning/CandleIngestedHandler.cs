using IctTrader.Domain.Configuration;
using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The Scanning module's bus seam (plan §3.0a/§4.1): it reacts to each <see cref="CandleIngested"/>, maps the
/// wire candle to the domain, feeds it to the stateful per-(symbol, style) <see cref="SymbolScanner"/> for every
/// operator-active style, and on a confirmed advisory <see cref="Domain.Setups.Setup"/> publishes a
/// <see cref="SetupConfirmed"/> carrying its <see cref="SetupDto"/>. The handler ORCHESTRATES only — every
/// decision (detection, confluence grading, pricing) lives in the pure domain the scanner wraps; the mapping is
/// pure. The registry is a SINGLETON (the scan state persists across candles); this handler is the bus-scoped
/// per-dispatch unit-of-work.
/// </summary>
public sealed class CandleIngestedHandler(
    ISymbolScannerRegistry registry,
    IMessageBus bus,
    IOptions<MarketContextOptions> scanningOptions,
    ILogger<CandleIngestedHandler>? logger = null)
    : IEventHandler<CandleIngested>
{
    private readonly ISymbolScannerRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly MarketContextOptions _scanning =
        (scanningOptions ?? throw new ArgumentNullException(nameof(scanningOptions))).Value;
    private readonly ILogger<CandleIngestedHandler> _logger = logger ?? NullLogger<CandleIngestedHandler>.Instance;

    public async Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var candle = CandleDtoMapper.ToDomain(@event.Candle);

        foreach (var style in _scanning.ResolvedActiveStyles)
        {
            var scanner = _registry.GetOrCreate(candle.Symbol, style);
            var setup = scanner.OnCandle(candle);
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
