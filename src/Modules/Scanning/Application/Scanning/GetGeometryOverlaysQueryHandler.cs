using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The Scanning module's live "engine view" read-side (plan §9.1): it answers the <see cref="GetGeometryOverlaysQuery"/>
/// the Host REST surface (<c>GET /api/chart/{symbol}</c>) routes over the bus, returning the current geometry snapshot
/// for a (symbol, timeframe) from the singleton <see cref="GeometryOverlayStore"/>. Pure read — the store owns the
/// bounded snapshot; this handler ORCHESTRATES only. Read-only/advisory (plan §6.3).
/// </summary>
public sealed class GetGeometryOverlaysQueryHandler(GeometryOverlayStore store)
    : IQueryHandler<GetGeometryOverlaysQuery, IReadOnlyList<GeometryOverlayDto>>
{
    private readonly GeometryOverlayStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<IReadOnlyList<GeometryOverlayDto>> HandleAsync(
        GetGeometryOverlaysQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(_store.Get(query.Symbol, query.Timeframe, query.Max, query.Model));
    }
}
