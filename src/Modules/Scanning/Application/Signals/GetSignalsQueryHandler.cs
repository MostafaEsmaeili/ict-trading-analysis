using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.Scanning.Application.Signals;

/// <summary>
/// The signals feed's read-side (plan §9): it answers the <see cref="GetSignalsQuery"/> the Host REST surface
/// (<c>GET /api/signals</c>) routes over the bus, returning the ranked, filtered "best opportunities" top-N from the
/// singleton <see cref="SignalFeedStore"/> via the <see cref="SignalRankingService"/>. Pure read — the handler
/// ORCHESTRATES only (the ranker DECIDES the order; the store owns the bounded set). Read-only/advisory (§6.3).
/// </summary>
public sealed class GetSignalsQueryHandler(SignalRankingService ranking, TimeProvider timeProvider)
    : IQueryHandler<GetSignalsQuery, IReadOnlyList<RankedSignalDto>>
{
    private readonly SignalRankingService _ranking = ranking ?? throw new ArgumentNullException(nameof(ranking));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public Task<IReadOnlyList<RankedSignalDto>> HandleAsync(
        GetSignalsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ranked = _ranking.Rank(
            _timeProvider.GetUtcNow(), query.Symbol, query.Style, query.MinGrade, query.Max);
        return Task.FromResult(ranked);
    }
}
