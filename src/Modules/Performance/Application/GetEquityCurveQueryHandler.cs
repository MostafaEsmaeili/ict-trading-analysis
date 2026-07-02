using IctTrader.Domain.Services;
using IctTrader.Performance.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Performance.Application;

/// <summary>
/// The Performance module's equity-curve read-side (plan §5.3): it answers the frozen
/// <see cref="GetEquityCurveQuery"/> the Host REST surface (<c>GET /api/equity</c>) routes over the bus. It builds the
/// cumulative-R curve over the accumulated closes via the pure <see cref="PerformanceCalculator"/> and projects each
/// domain point to the wire <see cref="EquityPointDto"/>. The curve is in R units (the default zero baseline), ordered
/// by close time. Pure read — the handler ORCHESTRATES only.
/// </summary>
public sealed class GetEquityCurveQueryHandler(PerformanceState state)
    : IQueryHandler<GetEquityCurveQuery, IReadOnlyList<EquityPointDto>>
{
    private readonly PerformanceState _state = state ?? throw new ArgumentNullException(nameof(state));

    public Task<IReadOnlyList<EquityPointDto>> HandleAsync(
        GetEquityCurveQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var curve = PerformanceCalculator.EquityCurve(_state.Snapshot(query.Model));
        IReadOnlyList<EquityPointDto> dtos = curve.Select(PerformanceMapper.ToDto).ToList();
        return Task.FromResult(dtos);
    }
}
