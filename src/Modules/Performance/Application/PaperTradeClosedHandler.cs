using IctTrader.Domain.Services;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Performance.Application;

/// <summary>
/// The Performance module's write-side bus seam (plan §3.0a/§5.3): it reacts to each
/// <see cref="PaperTradeClosed"/> from the PaperTrading module, appends the closed trade's R outcome to the
/// singleton <see cref="PerformanceState"/>, recomputes the §5.3 summary via the pure
/// <see cref="PerformanceCalculator"/>, and publishes <see cref="PerformanceUpdated"/> so the dashboard
/// refreshes live. The handler ORCHESTRATES only — every formula lives in the pure domain calculator.
///
/// <para><b>R-based:</b> the money P&amp;L is not on the wire, so the R multiple (<see cref="PaperTradeDto.RealizedR"/>)
/// is the recorded signal. A settled trade always carries a non-null R and close time; the null-coalescing is
/// purely defensive (R -> 0 is treated as a scratch; the close time falls back to the injected clock's UTC now —
/// never <c>DateTime.Now</c>, plan §4.8).</para>
///
/// <para>Read-only analytics (plan §6.3 guardrail): consuming a closed-trade notification routes nowhere near an
/// order path.</para>
/// </summary>
public sealed class PaperTradeClosedHandler(
    PerformanceState state, IMessageBus bus, TimeProvider timeProvider)
    : IEventHandler<PaperTradeClosed>
{
    private readonly PerformanceState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IMessageBus _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task HandleAsync(PaperTradeClosed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var trade = @event.Trade;
        var closedAtUtc = trade.ClosedAtUtc ?? _timeProvider.GetUtcNow();
        _state.Record(new ClosedTradeR(trade.RealizedR ?? 0m, closedAtUtc));

        var summary = PerformanceCalculator.Summarize(_state.Snapshot());
        await _bus.PublishAsync(new PerformanceUpdated(PerformanceMapper.ToDto(summary)), cancellationToken)
            .ConfigureAwait(false);
    }
}
