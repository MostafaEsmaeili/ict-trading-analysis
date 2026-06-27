using IctTrader.Domain.Repositories;
using IctTrader.Domain.Trading;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The PaperTrading module's full trades read-side (plan §4.1/§5.3): it answers <see cref="GetTradesQuery"/> the
/// Host REST surface routes over the bus, so the dashboard's trades table can show EVERY trade — open and closed —
/// with its status, close reason, gross/net P&amp;L, costs and R. It selects the source set by the requested
/// <see cref="TradeStatus"/> (a SQL-pushed filter via the repository) and optionally narrows by symbol, then maps
/// each aggregate to the wire <see cref="PaperTradeDto"/> via <see cref="PaperTradeDtoMapper"/>.
///
/// <para>Pure read — the query carries no order field and the returned DTOs route nowhere (§6.3 guardrail). The
/// handler ORCHESTRATES only: the repository owns the status filter, the mapper owns the projection.</para>
/// </summary>
public sealed class GetTradesQueryHandler(IPaperTradeRepository trades)
    : IQueryHandler<GetTradesQuery, IReadOnlyList<PaperTradeDto>>
{
    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    public async Task<IReadOnlyList<PaperTradeDto>> HandleAsync(
        GetTradesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = query.Status?.Trim();

        // The status filter is pushed to SQL via the repository's dedicated methods; an unrecognised/blank status
        // means "all". string.Equals is null-safe, so a null status falls through to the full ledger.
        IReadOnlyList<PaperTrade> source;
        if (string.Equals(status, nameof(TradeStatus.Open), StringComparison.OrdinalIgnoreCase))
        {
            source = await _trades.GetOpenAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (string.Equals(status, nameof(TradeStatus.Closed), StringComparison.OrdinalIgnoreCase))
        {
            source = await _trades.GetClosedAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            source = await _trades.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }

        var symbol = query.Symbol?.Trim();
        var filtered = string.IsNullOrEmpty(symbol)
            ? source
            : source.Where(t => string.Equals(t.Symbol.Value, symbol, StringComparison.OrdinalIgnoreCase));

        return filtered.Select(PaperTradeDtoMapper.ToDto).ToList();
    }
}
