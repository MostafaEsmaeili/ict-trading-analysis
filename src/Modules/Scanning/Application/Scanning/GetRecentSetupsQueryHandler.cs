using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The Scanning module's chart-overlay read-side (plan §9.1): it answers the <see cref="GetRecentSetupsQuery"/> the
/// Host REST surface (<c>GET /api/chart/{symbol}</c>) routes over the bus, returning the most-recent confirmed
/// setups NEWEST-FIRST from the singleton <see cref="RecentSetupStore"/>. Pure read — the handler ORCHESTRATES only
/// (the store owns the bounded window).
/// </summary>
public sealed class GetRecentSetupsQueryHandler(RecentSetupStore store)
    : IQueryHandler<GetRecentSetupsQuery, IReadOnlyList<SetupDto>>
{
    private readonly RecentSetupStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<IReadOnlyList<SetupDto>> HandleAsync(
        GetRecentSetupsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(_store.Recent(query.Symbol, query.Max));
    }
}
