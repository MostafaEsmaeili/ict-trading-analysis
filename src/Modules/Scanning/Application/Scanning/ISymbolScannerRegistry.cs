using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>Holds the live per-(symbol, timeframe, style, model) <see cref="SymbolScanner"/> state across candles.
/// A SINGLETON — each scanner is single-symbol, single-timeframe mutable working memory that must persist between
/// bus dispatches.</summary>
public interface ISymbolScannerRegistry
{
    /// <summary>Returns the scanner for this (symbol, timeframe, style, model), creating it on first use. The
    /// timeframe is part of the key so a multi-granularity feed never mixes timeframes into one scanner's FSM; the
    /// model is part of the key so two active setup models hold independent FSM state on the same cell (plan §16).</summary>
    SymbolScanner GetOrCreate(Symbol symbol, Timeframe timeframe, TradeStyle style, SetupModel model = SetupModel.Ict2022);
}
