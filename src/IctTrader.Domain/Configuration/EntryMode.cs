namespace IctTrader.Domain.Configuration;

/// <summary>
/// How a confirmed advisory setup becomes a paper trade (plan §2.5.1 step 7 / §5.1). <see cref="Armed"/> is the
/// ICT-faithful default: the setup rests as a LIMIT and only opens if price RETRACES into the entry (the orchestrator
/// drives it per candle). <see cref="Immediate"/> is the §5.1 immediate-open path (open at the plan entry on
/// confirmation) — kept as a what-if escape hatch and for the existing immediate-open tests. The module orchestrator
/// branches on this; the per-candle <c>EntryManager</c> only ever drives an already-armed entry.
/// </summary>
public enum EntryMode
{
    /// <summary>Rest as a limit and open only on the §2.5.1-step-7 retrace touch (the faithful default).</summary>
    Armed,

    /// <summary>Open immediately at the plan entry on confirmation (the §5.1 path / a what-if).</summary>
    Immediate,
}
