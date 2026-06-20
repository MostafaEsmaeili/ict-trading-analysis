namespace IctTrader.Domain.Trading;

/// <summary>
/// How the simulator resolves a single bar that straddles BOTH the stop and the runner target (plan §5.2). At
/// bar granularity the true intrabar path is unknowable, so the default is the conservative, senior-trader-honest
/// assumption — the stop fills first, for longs AND shorts — which never overstates strategy edge. The raw
/// Open→Low→High→Close path would invert for shorts (filling the target first, optimistically), so the worst-case
/// assumption deliberately overrides it. A future tick/sub-bar replay (the §5.2 gold-standard path) removes the
/// ambiguity entirely and can opt back into a literal-path resolution.
/// </summary>
public enum IntrabarFillAssumption
{
    /// <summary>WorstCase (default): a straddling bar fills the stop, regardless of trade direction.</summary>
    StopFirst,

    /// <summary>Optimistic: a straddling bar fills the runner target first — for what-if analysis only.</summary>
    TargetFirst,
}
