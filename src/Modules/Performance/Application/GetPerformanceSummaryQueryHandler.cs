using IctTrader.Domain.Services;
using IctTrader.Performance.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Performance.Application;

/// <summary>
/// The Performance module's summary read-side (plan §5.3): it answers the frozen
/// <see cref="GetPerformanceSummaryQuery"/> the Host REST surface (<c>GET /api/performance</c>) routes over the bus.
/// It folds the accumulated closed-trade R stream through the pure <see cref="PerformanceCalculator"/> and maps the
/// domain summary to the wire <see cref="PerformanceSummaryDto"/>, so the endpoint returns REAL metrics instead of a
/// zero stub. Pure read — the handler ORCHESTRATES only (the calculator owns every formula).
/// </summary>
public sealed class GetPerformanceSummaryQueryHandler(PerformanceState state)
    : IQueryHandler<GetPerformanceSummaryQuery, PerformanceSummaryDto>
{
    private readonly PerformanceState _state = state ?? throw new ArgumentNullException(nameof(state));

    public Task<PerformanceSummaryDto> HandleAsync(
        GetPerformanceSummaryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var summary = PerformanceCalculator.Summarize(_state.Snapshot());
        return Task.FromResult(PerformanceMapper.ToDto(summary));
    }
}
