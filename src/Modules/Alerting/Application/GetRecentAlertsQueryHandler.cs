using IctTrader.Alerting.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Alerting.Application;

/// <summary>
/// The Alerting module's read-side (plan §9): it answers the frozen <see cref="GetRecentAlertsQuery"/> the Host
/// REST surface (<c>GET /api/alerts</c>) routes over the bus, returning the most-recent alerts NEWEST-FIRST from
/// the singleton <see cref="AlertLog"/>. Pure read — the handler ORCHESTRATES only (the log owns the window).
/// </summary>
public sealed class GetRecentAlertsQueryHandler(AlertLog log)
    : IQueryHandler<GetRecentAlertsQuery, IReadOnlyList<AlertDto>>
{
    private readonly AlertLog _log = log ?? throw new ArgumentNullException(nameof(log));

    public Task<IReadOnlyList<AlertDto>> HandleAsync(
        GetRecentAlertsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(_log.Recent(query.Max));
    }
}
