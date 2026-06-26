using IctTrader.Domain.Configuration;
using IctTrader.MarketData.Contracts;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
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
    IOptions<MarketContextOptions> scanningOptions)
    : IEventHandler<CandleIngested>
{
    private readonly ISymbolScannerRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly MarketContextOptions _scanning =
        (scanningOptions ?? throw new ArgumentNullException(nameof(scanningOptions))).Value;

    public async Task HandleAsync(CandleIngested @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var candle = CandleDtoMapper.ToDomain(@event.Candle);

        foreach (var style in _scanning.ActiveStyles)
        {
            var scanner = _registry.GetOrCreate(candle.Symbol, style);
            var setup = scanner.OnCandle(candle);
            if (setup is null)
            {
                continue;
            }

            // The bar-close time stamps the detection; the killzone is the scanner's session for this candle.
            var dto = SetupDtoMapper.ToDto(setup, scanner.CurrentKillzone, candle.OpenTimeUtc);
            await _bus.PublishAsync(new SetupConfirmed(dto), cancellationToken).ConfigureAwait(false);
        }
    }
}
