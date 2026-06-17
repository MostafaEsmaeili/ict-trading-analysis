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

    public decimal MeanThresholdPercent { get; init; } = 0.50m;

    public decimal EntryOffsetPipsFx { get; init; } = 3m;

    public bool RequireInCorrectHalf { get; init; } = true;

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

        return errors;
    }
}
