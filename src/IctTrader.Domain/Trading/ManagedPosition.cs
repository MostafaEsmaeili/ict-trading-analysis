namespace IctTrader.Domain.Trading;

/// <summary>
/// One confirmed setup's in-flight position as the <see cref="TradeOrchestrator"/> drives it through its lifecycle
/// (plan §3.4): a resting <see cref="ArmedEntry"/> (Armed mode) that triggers into a <see cref="PaperTrade"/>, or a
/// trade opened immediately (Immediate mode), then managed to close. It is a thin holder over the two aggregates — it
/// owns NO business rules (those stay in <see cref="ArmedEntry"/> / <see cref="PaperTrade"/> / <see cref="PaperAccount"/>);
/// it only records which aggregate currently represents the position so the orchestrator and the module handler can
/// read state and collect domain events. The aggregates' own <see cref="ArmedEntryStatus"/> / <see cref="TradeStatus"/>
/// remain the single source of truth — this holder never duplicates them.
/// </summary>
public sealed class ManagedPosition
{
    private ManagedPosition(ArmedEntry? armed, PaperTrade? trade)
    {
        Armed = armed;
        Trade = trade;
    }

    /// <summary>A position that begins as a resting entry limit (Armed mode — plan §2.5.1 step 7).</summary>
    public static ManagedPosition Resting(ArmedEntry armed)
    {
        ArgumentNullException.ThrowIfNull(armed);
        return new ManagedPosition(armed, null);
    }

    /// <summary>A position that begins as an already-open trade (Immediate mode — the §5.1 immediate-open path).</summary>
    public static ManagedPosition Live(PaperTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return new ManagedPosition(null, trade);
    }

    /// <summary>The resting limit, if this position armed. After it triggers or is cancelled, the reference is kept so
    /// its terminal event can be read — inspect its <see cref="ArmedEntry.Status"/>, not the reference, for the state.</summary>
    public ArmedEntry? Armed { get; private set; }

    /// <summary>The trade, once the entry triggered or opened immediately. Kept after close so its terminal event can be
    /// read — inspect its <see cref="PaperTrade.Status"/> for the state.</summary>
    public PaperTrade? Trade { get; private set; }

    /// <summary>True once the lifecycle is finished and nothing remains to manage: the trade closed, or the limit was
    /// cancelled unfilled (and never produced a trade). The module handler stops advancing the position once this holds.</summary>
    public bool IsComplete =>
        Trade is { Status: TradeStatus.Closed }
        || (Trade is null && Armed is { Status: ArmedEntryStatus.Cancelled });

    /// <summary>True while a resting limit is still waiting for its retrace this candle.</summary>
    internal bool HasRestingEntry => Armed is { Status: ArmedEntryStatus.Armed };

    /// <summary>True while a trade is open and still manageable this candle.</summary>
    internal bool HasOpenTrade => Trade is { Status: TradeStatus.Open };

    /// <summary>Records the trade the resting limit triggered into (a key re-label — the trade carries the armed id).</summary>
    internal void AttachTrade(PaperTrade trade) => Trade = trade;
}
