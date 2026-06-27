using System.Collections.Concurrent;
using IctTrader.Domain.Configuration;
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
public sealed class SymbolScannerRegistry(ISymbolScannerFactory factory, IRuntimeSettings settings)
    : ISymbolScannerRegistry
{
    private readonly ISymbolScannerFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly IRuntimeSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ConcurrentDictionary<ScannerKey, SymbolScanner> _scanners = new();
    private int _builtRevision = -1;

    public SymbolScanner GetOrCreate(Symbol symbol, TradeStyle style)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        // Live-apply seam (plan §15): when the operator changes settings the revision ticks, so drop the cached
        // scanners and rebuild them with the new options on the next candle. The scan FSM/context warm-state is
        // intentionally reset — a live settings change re-warms the scanner (the cost of applying without a restart).
        // The bus dispatches a symbol's candles sequentially, so this evict-then-rebuild is not contended.
        var revision = _settings.Revision;
        if (_builtRevision != revision)
        {
            _scanners.Clear();
            _builtRevision = revision;
        }

        var key = new ScannerKey(symbol.Value, style);
        return _scanners.GetOrAdd(key, _ => _factory.Create(symbol, style));
    }

    private readonly record struct ScannerKey(string Symbol, TradeStyle Style);
}
