using System.Globalization;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.Scanning.Application.Signals;
using IctTrader.Scanning.Contracts;
using Microsoft.Extensions.Options;

namespace IctTrader.Host;

/// <summary>
/// The Host-resident adapter that backs the Scanning <see cref="ISignalTakeStateProvider"/> port with PaperTrading
/// state — the seam that lets the signals feed show each signal's semi-auto TAKE state (plan §15) WITHOUT Scanning
/// referencing PaperTrading internals (the Host references both, so the cross-module enrichment lives here). It derives:
/// <list type="bullet">
/// <item><b>EntryMode</b> — the symbol's effective Auto/Manual workflow (the per-instrument override the
/// <see cref="IInstrumentRegistry"/> overlays, else <see cref="PaperTradingOptions.DefaultEntryMode"/>);</item>
/// <item><b>IsTaken</b> — whether a paper trade OR armed entry already exists under the signal's deterministic id;</item>
/// <item><b>BlockReason / ExpiresAtUtc</b> — null/expiry from the <see cref="PendingOpportunityStore"/> + the mode.</item>
/// </list>
/// Read-only/advisory (§6.3): it reports DISPLAY state only and opens nothing (a TAKE goes through the PaperTrading
/// <c>TakeSetupCommand</c>). It is a SINGLETON consumed by the singleton ranking service, so the scoped trade/armed
/// repositories are resolved through a short-lived scope per call (the signals feed is small + read on demand).
/// </summary>
public sealed class PaperTradingSignalTakeStateProvider : ISignalTakeStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInstrumentRegistry _instruments;
    private readonly PendingOpportunityStore _pending;
    private readonly PaperTradingOptions _paperTrading;

    public PaperTradingSignalTakeStateProvider(
        IServiceScopeFactory scopeFactory,
        IInstrumentRegistry instruments,
        PendingOpportunityStore pending,
        IOptions<PaperTradingOptions> paperTrading)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(pending);
        _scopeFactory = scopeFactory;
        _instruments = instruments;
        _pending = pending;
        _paperTrading = (paperTrading ?? throw new ArgumentNullException(nameof(paperTrading))).Value;
    }

    public SignalTakeState DescribeFor(SetupDto setup, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(setup);

        var profile = _instruments.Resolve(new Symbol(setup.Symbol));
        var entryMode = _paperTrading.EffectiveEntryMode(profile.Overrides);
        var entryModeName = entryMode.ToString();

        // Already acted on? The deterministic id is the trade/armed-entry id, so an existing one means the signal was
        // taken (or auto-opened). A short scope reads the scoped repositories from the singleton.
        var isTaken = TradeOrArmedExists(setup.Id);
        if (isTaken)
        {
            return new SignalTakeState(entryModeName, IsTaken: true, BlockReason: nameof(TakeSetupFailure.AlreadyTaken), ExpiresAtUtc: null);
        }

        // Auto symbols are not takeable via the manual button (they open automatically) — flag the reason so the UI
        // disables Take, without an expiry.
        if (entryMode == TradeEntryMode.Auto)
        {
            return new SignalTakeState(entryModeName, IsTaken: false, BlockReason: entryModeName, ExpiresAtUtc: null);
        }

        // Manual: takeable only while a live (non-expired) pending exists for it; otherwise it has expired. The store's
        // public AgeExpiryFor returns the age-based expiry instant when pending, else null (the killzone-end expiry is
        // dynamic, so the age window is the stable wire countdown; killzone-end pruning still removes it server-side).
        var expiry = _pending.AgeExpiryFor(setup.Id, nowUtc);
        if (expiry is null)
        {
            return new SignalTakeState(entryModeName, IsTaken: false, BlockReason: nameof(TakeSetupFailure.Expired), ExpiresAtUtc: null);
        }

        return new SignalTakeState(
            entryModeName,
            IsTaken: false,
            BlockReason: null,
            ExpiresAtUtc: expiry.Value.ToString("O", CultureInfo.InvariantCulture));
    }

    private bool TradeOrArmedExists(Guid id)
    {
        using var scope = _scopeFactory.CreateScope();
        var trades = scope.ServiceProvider.GetRequiredService<IPaperTradeRepository>();
        var armed = scope.ServiceProvider.GetRequiredService<IArmedEntryRepository>();

        // GetByIdAsync is awaited synchronously here: the provider is consumed by a synchronous ranking projection and
        // the lookups are a single keyed read each (small, indexed). GetAwaiter().GetResult() avoids changing the
        // port's signature to async for two trivial existence checks.
        var trade = trades.GetByIdAsync(id).GetAwaiter().GetResult();
        if (trade is not null)
        {
            return true;
        }

        return armed.GetByIdAsync(id).GetAwaiter().GetResult() is not null;
    }
}
