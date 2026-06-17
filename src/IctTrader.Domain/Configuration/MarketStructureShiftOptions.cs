namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable market-structure-shift detection (plan §2.5.1 step 5, §2.5.10). Bound from
/// <c>Ict:MarketStructureShift</c>. The sweep-to-MSS bar window is a flagged engineering default.
/// </summary>
public sealed class MarketStructureShiftOptions
{
    public const string SectionName = "Ict:MarketStructureShift";

    /// <summary>Minimum distance the close must travel beyond the broken swing (a weak/wick break is rejected).</summary>
    public decimal CloseBeyondMinPips { get; init; } = 1.0m;

    /// <summary>How many bars a sweep may precede the MSS — sweep MUST precede the shift (§2.5.10).</summary>
    public int SweepToMssMaxBars { get; init; } = 5;

    public bool RequirePrecedentSweep { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (CloseBeyondMinPips < 0m)
        {
            errors.Add($"CloseBeyondMinPips cannot be negative but was {CloseBeyondMinPips}.");
        }

        if (SweepToMssMaxBars < 1)
        {
            errors.Add($"SweepToMssMaxBars must be at least 1 but was {SweepToMssMaxBars}.");
        }

        return errors;
    }
}
