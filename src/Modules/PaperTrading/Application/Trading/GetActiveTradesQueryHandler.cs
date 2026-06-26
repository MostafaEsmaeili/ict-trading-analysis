using IctTrader.Domain.Repositories;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The PaperTrading module's active-trades read-side (plan §4.1): it answers the frozen
/// <see cref="GetActiveTradesQuery"/> the Host REST surface routes over the bus. It loads every OPEN
/// <see cref="Domain.Trading.PaperTrade"/> through the aggregate-scoped <see cref="IPaperTradeRepository"/> and
/// maps each to the wire <see cref="PaperTradeDto"/> via the module-internal <see cref="PaperTradeDtoMapper"/>, so
/// the REST endpoint returns REAL persisted state instead of an empty stub.
///
/// <para>This is a pure read — the query carries no order field and the returned DTO routes nowhere (§6.3
/// guardrail). The handler ORCHESTRATES only: the repository owns the OPEN filter and the mapper owns the
/// projection.</para>
/// </summary>
public sealed class GetActiveTradesQueryHandler(IPaperTradeRepository trades)
    : IQueryHandler<GetActiveTradesQuery, IReadOnlyList<PaperTradeDto>>
{
    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    public async Task<IReadOnlyList<PaperTradeDto>> HandleAsync(
        GetActiveTradesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var open = await _trades.GetOpenAsync(cancellationToken).ConfigureAwait(false);
        return open.Select(PaperTradeDtoMapper.ToDto).ToList();
    }
}
