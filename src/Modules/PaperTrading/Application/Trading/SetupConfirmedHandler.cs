using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Repositories;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Contracts;
using IctTrader.SharedKernel.Messaging;
using Microsoft.Extensions.Options;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// The PaperTrading module's setup→trade seam (plan §3.0a/§4.1): it reacts to each <see cref="SetupConfirmed"/> and,
/// after the idempotency short-circuit, branches on the symbol's effective TAKE workflow
/// (<see cref="PaperTradingOptions.EffectiveEntryMode"/>):
/// <list type="bullet">
/// <item><b>Auto</b> → hands the setup to the shared <see cref="SetupTradeOpener"/>, which rehydrates the domain
/// <see cref="Domain.Setups.Setup"/>, sizes + arms/opens it against the demo <see cref="Domain.Trading.PaperAccount"/>
/// (reserving its risk against the §2.5.10 cap), persists, and publishes <see cref="Contracts.PaperTradeOpened"/> on an
/// Immediate open — exactly as before this slice existed;</item>
/// <item><b>Manual</b> → records the setup as a PENDING opportunity in the <see cref="PendingOpportunityStore"/> and
/// RETURNS. Nothing is opened and NO risk is reserved until the operator TAKEs it (the <c>TakeSetupCommandHandler</c>
/// then calls the SAME <see cref="SetupTradeOpener"/>, so Auto and Take are byte-identical in sizing/cap/guardrail).</item>
/// </list>
///
/// <para>The handler ORCHESTRATES only — every decision (sizing, the cap gate, arm-vs-open, the daily risk guard) lives
/// in the pure domain the shared opener drives. Paper-only either way: no broker/order/executor path exists (§6.3).</para>
/// </summary>
public sealed class SetupConfirmedHandler(
    SetupTradeOpener opener,
    PendingOpportunityStore pending,
    IInstrumentRegistry instruments,
    IPaperTradeRepository trades,
    IArmedEntryRepository armedEntries,
    IOptions<PaperTradingOptions> paperTrading)
    : IEventHandler<SetupConfirmed>
{
    private readonly SetupTradeOpener _opener = opener ?? throw new ArgumentNullException(nameof(opener));

    private readonly PendingOpportunityStore _pending = pending ?? throw new ArgumentNullException(nameof(pending));

    private readonly IInstrumentRegistry _instruments =
        instruments ?? throw new ArgumentNullException(nameof(instruments));

    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    private readonly IArmedEntryRepository _armedEntries =
        armedEntries ?? throw new ArgumentNullException(nameof(armedEntries));

    private readonly PaperTradingOptions _paperTrading =
        (paperTrading ?? throw new ArgumentNullException(nameof(paperTrading))).Value;

    public async Task HandleAsync(SetupConfirmed @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Idempotency on the deterministic seam key (SetupDto.Id): a replayed / redelivered / restart-re-streamed
        // SetupConfirmed for the SAME setup must NOT open a second trade (double-reserving the ~5% cap, double-counting
        // performance) NOR re-pend it. The id becomes the opened/armed aggregate id, so an existing trade OR armed entry
        // under it means this setup is already handled — short-circuit BEFORE touching the opener/pending board.
        var setupDto = @event.Setup;
        var setupId = setupDto.Id;
        if (await _trades.GetByIdAsync(setupId, cancellationToken).ConfigureAwait(false) is not null
            || await _armedEntries.GetByIdAsync(setupId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        // Resolve the symbol's effective TAKE workflow: its per-instrument override (live-editable via the runtime
        // settings the registry overlays) else the global default. Manual → pend and wait for the operator; Auto → open.
        var profile = _instruments.Resolve(new Symbol(setupDto.Symbol));
        var entryMode = _paperTrading.EffectiveEntryMode(profile.Overrides);

        if (entryMode == TradeEntryMode.Manual)
        {
            // Semi-auto: record the opportunity for the operator to TAKE. Reserves nothing, persists nothing, opens
            // nothing — just an in-memory advisory board entry. "now" is the setup's confirm time (the candle open),
            // never an ambient clock, so the pending's expiry math is deterministic across replays.
            _pending.Add(new PendingOpportunity(setupDto, profile.InstrumentClass), setupDto.DetectedAtUtc);
            return;
        }

        // Auto: open/arm immediately through the ONE shared simulated-open path.
        await _opener.OpenAsync(setupDto, cancellationToken).ConfigureAwait(false);
    }
}
