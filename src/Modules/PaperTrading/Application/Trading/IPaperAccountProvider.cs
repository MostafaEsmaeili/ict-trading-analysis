using IctTrader.Domain.Trading;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>Loads the single demo <see cref="PaperAccount"/>, creating and staging it on first use. Both bus
/// handlers resolve the SAME account through this seam so they reserve/settle against one consistent ledger.</summary>
public interface IPaperAccountProvider
{
    /// <summary>Returns the demo account tracked by the current scope's context. If it does not yet exist it is
    /// constructed from config and staged via <c>AddAsync</c> (the caller commits it with the rest of the unit of
    /// work). The returned instance is tracked, so mutations (reserve/settle) persist on the next save.</summary>
    Task<PaperAccount> GetOrCreateAsync(CancellationToken cancellationToken = default);
}
