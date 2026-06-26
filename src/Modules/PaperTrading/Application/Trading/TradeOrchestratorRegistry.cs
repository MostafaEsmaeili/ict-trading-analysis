using System.Collections.Concurrent;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The SINGLETON cache of one <see cref="TradeOrchestrator"/> per symbol. Unlike the Scanning module's scanner
/// registry — whose scanners hold the mutable scan FSM and MUST survive across dispatches — the orchestrators here
/// are STATELESS (the DB is the position state, plan §4.1 / the slice's DB-as-state decision), so this cache only
/// avoids rebuilding the per-symbol object graph (a per-symbol <see cref="SymbolSpec"/>) on every candle. The
/// lookup is guarded by a <see cref="ConcurrentDictionary{TKey,TValue}"/> so a future concurrent caller cannot
/// race a second orchestrator into the same slot.
/// </summary>
public sealed class TradeOrchestratorRegistry(ITradeOrchestratorFactory factory) : ITradeOrchestratorRegistry
{
    private readonly ITradeOrchestratorFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));

    private readonly ConcurrentDictionary<string, TradeOrchestrator> _orchestrators = new();

    public TradeOrchestrator GetOrCreate(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return _orchestrators.GetOrAdd(symbol.Value, _ => _factory.Create(symbol));
    }
}
