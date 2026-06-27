using System.Collections.Concurrent;
using IctTrader.Domain.Configuration;
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
public sealed class TradeOrchestratorRegistry(ITradeOrchestratorFactory factory, IRuntimeSettings settings)
    : ITradeOrchestratorRegistry
{
    private readonly ITradeOrchestratorFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IRuntimeSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    private readonly ConcurrentDictionary<string, TradeOrchestrator> _orchestrators = new();
    private int _builtRevision = -1;

    public TradeOrchestrator GetOrCreate(Symbol symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        // Live-apply seam (plan §15): a settings change ticks the revision, so rebuild the per-symbol orchestrators
        // with the new options. The orchestrators are stateless (the DB holds position state), so this is a cheap
        // graph rebuild with no state loss.
        var revision = _settings.Revision;
        if (_builtRevision != revision)
        {
            _orchestrators.Clear();
            _builtRevision = revision;
        }

        return _orchestrators.GetOrAdd(symbol.Value, _ => _factory.Create(symbol));
    }
}
