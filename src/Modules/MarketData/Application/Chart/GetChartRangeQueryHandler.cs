using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.MarketData.Application.Chart;

/// <summary>
/// Answers a <see cref="GetChartRangeQuery"/> from the persisted <c>candles</c> table (plan §7 / §9.1):
/// returns OHLC bars for <c>[From, To]</c> in CHRONOLOGICAL (oldest→newest) order, capped at
/// <see cref="GetChartRangeQuery.MaxRangeCandles"/>.
/// <para>
/// This handler is the "historical depth" counterpart to <see cref="GetChartCandlesQueryHandler"/> (which
/// reads the in-memory ring buffer).  The <c>GET /api/chart/{symbol}?from=&amp;to=</c> endpoint chooses
/// between them: a range request goes here; a plain recent-candles request goes to the ring buffer.
/// </para>
/// <para>
/// Pure read — routes nowhere near an order path (plan §6.3 guardrail).
/// </para>
/// </summary>
public sealed class GetChartRangeQueryHandler(
    ICandleRepository repository,
    CandlePersistenceConfiguration config)
    : IQueryHandler<GetChartRangeQuery, IReadOnlyList<CandleDto>>
{
    private readonly ICandleRepository _repository =
        repository ?? throw new ArgumentNullException(nameof(repository));

    private readonly CandlePersistenceConfiguration _config =
        config ?? throw new ArgumentNullException(nameof(config));

    public async Task<IReadOnlyList<CandleDto>> HandleAsync(
        GetChartRangeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Enum.TryParse<Timeframe>(query.Timeframe, ignoreCase: false, out var tf))
        {
            // Unknown timeframe → empty result (the chart endpoint surfaces its empty-series state).
            return [];
        }

        var symbol = new Symbol(query.Symbol);
        var candles = await _repository.GetRangeAsync(
                symbol, tf, query.From, query.To, _config.MaxRangeCandles, cancellationToken)
            .ConfigureAwait(false);

        return candles.Select(ToDto).ToList();
    }

    private static CandleDto ToDto(Candle c) =>
        new(c.Symbol.Value, c.Timeframe.ToString(), c.OpenTimeUtc, c.Open, c.High, c.Low, c.Close, c.Volume);
}

/// <summary>
/// A lightweight singleton that carries the max-range-candles cap from <c>CandlePersistenceOptions</c>
/// into the query handler without pulling the full <c>IOptions&lt;T&gt;</c> infrastructure into the
/// Application layer (which must stay EF-free and options-POCO-free to respect the module boundary).
/// Populated by the Host DI wiring when <see cref="MarketDataPersistenceOptions"/> is resolved.
/// </summary>
public sealed class CandlePersistenceConfiguration(int maxRangeCandles)
{
    public int MaxRangeCandles { get; } =
        maxRangeCandles > 0
            ? maxRangeCandles
            : throw new ArgumentOutOfRangeException(nameof(maxRangeCandles), "MaxRangeCandles must be > 0.");
}
