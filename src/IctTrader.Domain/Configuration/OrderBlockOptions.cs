namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable order-block detection (plan §2.5.1 step 6). A valid OB REQUIRES a linked FVG; the key level is
/// the block's opening price and the mean-threshold (default 50%) is the partial/entry reference. Bound
/// from <c>Ict:Detection:OrderBlock</c>.
/// </summary>
public sealed class OrderBlockOptions
{
    public const string SectionName = "Ict:Detection:OrderBlock";

    /// <summary>An order block is invalid without a linked FVG in the same leg/direction.</summary>
    public bool RequireFvg { get; init; } = true;

    /// <summary>
    /// Require the linked FVG to be on the order block's OWN timeframe — a §2.5.7-deferred approximation of true
    /// displacement-leg membership (§2.5.1 step 6 scans the entry FVG 15m→1m, so a higher-TF OB may legitimately
    /// link to a finer-TF FVG). Defaults TRUE to suppress stale cross-timeframe false confluence in the single-
    /// entry-timeframe WP1 slice; the confluence FSM replaces it with bar-window leg linkage (WP3).
    /// </summary>
    public bool RequireSameTimeframeFvg { get; init; } = true;

    public decimal MeanThresholdPercent { get; init; } = 0.50m;

    public decimal EntryOffsetPipsFx { get; init; } = 3m;

    public bool RequireInCorrectHalf { get; init; } = true;

    /// <summary>
    /// Caps the consecutive opposite-close OB cluster at the last N candles counted back from the
    /// displacement (OB-9a). Default 3 per the core-model register; Ep9 shows a real 4-candle cluster, so
    /// this is a CAP not a hard rule and is raisable. A longer run keeps only the last N (the anchor becomes
    /// the Nth-from-the-displacement candle); 1 reduces the OB to the single bar immediately before the
    /// displacement (the legacy behaviour). PROVENANCE: the numeric default 3 is INVENTED/operator-tunable —
    /// the transcripts state no cap (set 4 to admit the Ep9 example whole); the consecutive-run RULE itself
    /// is Mentorship-verbatim (Ep3/Ep9), only this number is not.
    /// </summary>
    public int MaxClusterCandles { get; init; } = 3;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MeanThresholdPercent is < 0m or > 1m)
        {
            errors.Add($"MeanThresholdPercent must be within [0, 1] but was {MeanThresholdPercent}.");
        }

        if (EntryOffsetPipsFx < 0m)
        {
            errors.Add($"EntryOffsetPipsFx cannot be negative but was {EntryOffsetPipsFx}.");
        }

        if (MaxClusterCandles < 1)
        {
            errors.Add($"MaxClusterCandles must be >= 1 but was {MaxClusterCandles}.");
        }

        return errors;
    }
}
