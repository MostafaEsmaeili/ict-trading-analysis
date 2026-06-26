using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>Holds one <see cref="TradeOrchestrator"/> per symbol across bus dispatches. A SINGLETON — but the
/// orchestrators are STATELESS (pure DECIDE/APPLY; the position state lives in the database, not the orchestrator),
/// so caching one per symbol is purely to avoid rebuilding the per-symbol object graph on every candle.</summary>
public interface ITradeOrchestratorRegistry
{
    /// <summary>Returns the orchestrator for this symbol, creating it on first use.</summary>
    TradeOrchestrator GetOrCreate(Symbol symbol);
}
