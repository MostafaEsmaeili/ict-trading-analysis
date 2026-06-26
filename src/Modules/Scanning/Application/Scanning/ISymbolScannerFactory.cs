using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>Creates a fresh stateful <see cref="SymbolScanner"/> for one (symbol, style). The registry calls
/// this on a cache miss; production wiring resolves it as a singleton (the resolved options are immutable).</summary>
public interface ISymbolScannerFactory
{
    SymbolScanner Create(Symbol symbol, TradeStyle style);
}
