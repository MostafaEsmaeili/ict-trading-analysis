using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>Holds the live per-(symbol, timeframe, style) <see cref="SymbolScanner"/> state across candles. A
/// SINGLETON — each scanner is single-symbol, single-timeframe mutable working memory that must persist between
/// bus dispatches.</summary>
public interface ISymbolScannerRegistry
{
    /// <summary>Returns the scanner for this (symbol, timeframe, style), creating it on first use. The timeframe
    /// is part of the key so a multi-granularity feed never mixes timeframes into one scanner's FSM.</summary>
    SymbolScanner GetOrCreate(Symbol symbol, Timeframe timeframe, TradeStyle style);
}
