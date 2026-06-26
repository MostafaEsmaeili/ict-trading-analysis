using System.Collections.Concurrent;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// The SINGLETON cache of stateful <see cref="SymbolScanner"/>s, one per (symbol, style). The scan FSM lives in
/// the scanner's <see cref="MarketContext"/>, so the instance MUST survive across bus dispatches — hence the
/// singleton lifetime and the get-or-create lookup. The bus dispatches a module's event handlers sequentially,
/// but the lookup is still guarded by a <see cref="ConcurrentDictionary{TKey,TValue}"/> so a future concurrent
/// caller cannot race a second scanner into the same slot.
///
/// <para>NOTE: a <see cref="SymbolScanner"/> is single-symbol mutable working memory — candles for ONE
/// (symbol, style) must arrive in chronological order; the registry never shares an instance across keys.</para>
/// </summary>
public sealed class SymbolScannerRegistry(ISymbolScannerFactory factory) : ISymbolScannerRegistry
{
    private readonly ISymbolScannerFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ConcurrentDictionary<ScannerKey, SymbolScanner> _scanners = new();

    public SymbolScanner GetOrCreate(Symbol symbol, TradeStyle style)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var key = new ScannerKey(symbol.Value, style);
        return _scanners.GetOrAdd(key, _ => _factory.Create(symbol, style));
    }

    private readonly record struct ScannerKey(string Symbol, TradeStyle Style);
}
