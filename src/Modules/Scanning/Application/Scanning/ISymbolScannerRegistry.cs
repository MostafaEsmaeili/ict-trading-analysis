using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>Holds the live per-(symbol, style) <see cref="SymbolScanner"/> state across candles. A SINGLETON —
/// each scanner is single-symbol mutable working memory that must persist between bus dispatches.</summary>
public interface ISymbolScannerRegistry
{
    /// <summary>Returns the scanner for this (symbol, style), creating it on first use.</summary>
    SymbolScanner GetOrCreate(Symbol symbol, TradeStyle style);
}
