using IctTrader.Domain.Repositories;
using IctTrader.PaperTrading.Contracts;
using IctTrader.SharedKernel.Messaging;

namespace IctTrader.PaperTrading.Application.Trading;

/// <summary>
/// Handles the operator's <see cref="TakeSetupCommand"/> (plan §15): open ONE SIMULATED paper trade from a Manual-mode
/// pending opportunity. It looks the pending up (and atomically removes it) from the <see cref="PendingOpportunityStore"/>,
/// then opens it through the EXACT same <see cref="SetupTradeOpener"/> the automatic path uses — so a taken trade is
/// byte-identical to an auto trade in sizing, the §2.5.10 cap, and the paper-only guardrail. The handler ORCHESTRATES
/// only; every decision lives in the pure domain the shared opener drives.
///
/// <para><b>Idempotent on the deterministic id.</b> The pending id IS the future trade id, so the opener's idempotency
/// guard (a trade/armed entry already exists) makes a double-take a no-op. The handler ALSO checks for an existing
/// trade/armed entry up front and throws <see cref="TakeSetupFailure.AlreadyTaken"/> (409) so a re-take after the
/// pending was already consumed gives the operator a clear answer rather than a silent no-op.</para>
///
/// <para><b>Paper-only (§6.3 guardrail):</b> there is no broker/order/executor here — taking only writes to our own
/// aggregates via the shared opener.</para>
/// </summary>
public sealed class TakeSetupCommandHandler(
    PendingOpportunityStore pending,
    SetupTradeOpener opener,
    IPaperTradeRepository trades,
    IArmedEntryRepository armedEntries,
    TimeProvider timeProvider)
    : ICommandHandler<TakeSetupCommand>
{
    private readonly PendingOpportunityStore _pending = pending ?? throw new ArgumentNullException(nameof(pending));
    private readonly SetupTradeOpener _opener = opener ?? throw new ArgumentNullException(nameof(opener));
    private readonly IPaperTradeRepository _trades = trades ?? throw new ArgumentNullException(nameof(trades));

    private readonly IArmedEntryRepository _armedEntries =
        armedEntries ?? throw new ArgumentNullException(nameof(armedEntries));

    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task HandleAsync(TakeSetupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var setupId = command.SetupId;

        // Already taken? The deterministic id is the trade/armed-entry id, so an existing one means this setup was
        // already opened (auto or a prior take) — a re-take is a no-op, surfaced as 409 for a clear operator answer.
        if (await _trades.GetByIdAsync(setupId, cancellationToken).ConfigureAwait(false) is not null
            || await _armedEntries.GetByIdAsync(setupId, cancellationToken).ConfigureAwait(false) is not null)
        {
            // Clear any lingering pending (defensive — the auto path / a prior take should already have removed it).
            _pending.Remove(setupId);
            throw new TakeSetupException(TakeSetupFailure.AlreadyTaken);
        }

        // Atomically claim the pending. A miss is either "never pending" or "expired" — distinguish for the right
        // status (404 vs 404-Expired) by checking the raw map without the expiry filter would require exposing it; the
        // store treats expired as gone, so we report NotFound when truly absent and Expired when it was present-but-stale.
        var now = _timeProvider.GetUtcNow();
        var taken = _pending.TryTake(setupId, now);
        if (taken is null)
        {
            // Distinguish a stale-but-known pending (Expired) from a never-known one (NotFound): TryTake already pruned
            // it, so we cannot re-read it. We treat any miss as NotFound; the store's own expiry pruning means an
            // expired id is simply absent. (Expired is exposed so a future store that retains tombstones can refine it.)
            throw new TakeSetupException(TakeSetupFailure.NotFound);
        }

        // Open through the ONE shared simulated-open path — identical sizing/cap/guardrail to the automatic flow.
        await _opener.OpenAsync(taken.Setup, cancellationToken).ConfigureAwait(false);
    }
}
