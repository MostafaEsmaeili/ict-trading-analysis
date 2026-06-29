namespace IctTrader.PaperTrading.Application;

/// <summary>
/// Policy for the in-memory <see cref="Trading.PendingOpportunityStore"/> (bound from <c>Ict:PaperTrading:Pending</c>)
/// — the bounded set of Manual-mode confirmed setups awaiting an operator TAKE (plan §15). No magic numbers.
/// </summary>
public sealed class PendingOpportunityOptions
{
    public const string SectionName = "Ict:PaperTrading:Pending";

    /// <summary>An upper bound so a misconfigured value can't request an unbounded pending board.</summary>
    private const int AbsoluteMaxPending = 500;

    /// <summary>
    /// How long (minutes) after its detection a pending opportunity stays takeable before it ages out as stale. A §2.5
    /// intraday setup is fleeting, so an old untaken opportunity should not linger. INVENTED/operator-tunable — it
    /// mirrors the §2.5.1-step-9 spirit (don't act on a stale setup) but the transcripts give no take-window number;
    /// kept GENEROUS so killzone-end normally expires it first. Must be positive. Default 240 (4 hours).
    /// </summary>
    public int MaxPendingMinutes { get; init; } = 240;

    /// <summary>The maximum number of pending opportunities retained (the board cap). Once full, adding evicts the
    /// OLDEST by detection time so the board stays the latest opportunities, not an audit log. Default 50.</summary>
    public int MaxPending { get; init; } = 50;

    /// <summary>When true (default), a pending also expires once its candle's killzone-entry window has ended (the
    /// §2.5.1-step-3 entry window closed — window over / lunch / index cutoff) — don't chase a stale setup past its
    /// session. Turn off to expire on age alone (e.g. for a backtest with no live session math).</summary>
    public bool ExpireOnKillzoneEnd { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxPendingMinutes < 1)
        {
            errors.Add($"MaxPendingMinutes must be at least 1 but was {MaxPendingMinutes}.");
        }

        if (MaxPending is < 1 or > AbsoluteMaxPending)
        {
            errors.Add($"MaxPending must be within [1, {AbsoluteMaxPending}] but was {MaxPending}.");
        }

        return errors;
    }
}
