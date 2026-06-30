using IctTrader.Domain.Repositories;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Answers <see cref="GetPaperTradeQuery"/> — reads a single paper trade by id (open or closed) and projects it to the
/// wire <see cref="PaperTradeDto"/>, or null when absent. The Host uses it to echo the trade a
/// <see cref="TakeSetupCommand"/> just opened (the opened trade carries the deterministic setup id). Pure read — the
/// handler ORCHESTRATES only (the repository owns the lookup, the mapper owns the projection); the DTO routes nowhere
/// near an order path (§6.3 guardrail).
/// </summary>
public sealed class GetPaperTradeQueryHandler(IPaperTradeRepository trades)
    : IQueryHandler<GetPaperTradeQuery, PaperTradeDto?>
{
    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    public async Task<PaperTradeDto?> HandleAsync(
        GetPaperTradeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trade = await _trades.GetByIdAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return trade is null ? null : PaperTradeDtoMapper.ToDto(trade);
    }
}
