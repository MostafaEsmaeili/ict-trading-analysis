using IctTrader.Domain.Configuration;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>Creates a fresh stateful <see cref="SymbolScanner"/> for one (symbol, timeframe, style). The registry
/// calls this on a cache miss; production wiring resolves it as a singleton (the resolved options are immutable).</summary>
public interface ISymbolScannerFactory
{
    /// <summary>
    /// Builds the scanner for <paramref name="symbol"/>/<paramref name="timeframe"/>/<paramref name="style"/>. The
    /// <paramref name="timeframe"/> is the granularity this cell scans (a multi-granularity feed routes each style
    /// to its canonical entry TF), and it stamps the confirmed <c>Setup.Timeframe</c> per cell.
    /// <paramref name="confluence"/> overrides the host's configured <see cref="ConfluenceOptions"/> for this
    /// scanner (the on-demand backtest passes a per-run confluence policy — e.g. a relaxed k-of-n required gate to
    /// sweep); <c>null</c> uses the host default (the strict §2.5 model).
    /// </summary>
    SymbolScanner Create(Symbol symbol, Timeframe timeframe, TradeStyle style, ConfluenceOptions? confluence = null);
}
