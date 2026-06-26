using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.MarketData.Application.Chart;

/// <summary>
/// The MarketData module's chart read-side (plan §9.1): it answers the <see cref="GetChartCandlesQuery"/> the Host
/// REST surface (<c>GET /api/chart/{symbol}</c>) routes over the bus, returning the most-recent candles in
/// CHRONOLOGICAL order from the singleton <see cref="ChartCandleStore"/>. Pure read — the handler ORCHESTRATES only
/// (the store owns the bounded window).
/// </summary>
public sealed class GetChartCandlesQueryHandler(ChartCandleStore store)
    : IQueryHandler<GetChartCandlesQuery, IReadOnlyList<CandleDto>>
{
    private readonly ChartCandleStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<IReadOnlyList<CandleDto>> HandleAsync(
        GetChartCandlesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(_store.Recent(query.Symbol, query.Timeframe, query.Max));
    }
}
