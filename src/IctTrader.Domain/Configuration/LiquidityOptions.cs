namespace IctTrader.Domain.Configuration;

/// <summary>
/// Tunable liquidity-pool and sweep detection (plan §2.5.1 steps 2 &amp; 4). The equal-level tolerance and
/// the minimum sweep penetration are flagged engineering defaults needing WP1/per-symbol calibration.
/// Bound from <c>Ict:Liquidity</c>.
/// </summary>
public sealed class LiquidityOptions
{
    public const string SectionName = "Ict:Liquidity";

    /// <summary>How close two levels must be to count as relative-equal liquidity (engineering default).</summary>
    public decimal EqualLevelTolerancePips { get; init; } = 1.5m;

    /// <summary>Minimum wick penetration beyond a pool to count as a raid; strict (an exact-min wick is not a sweep).</summary>
    public decimal SweepMinPenetrationPips { get; init; } = 0.5m;

    /// <summary>A sweep requires a close back inside the pool; a close BEYOND is a run (HRLR), not a sweep.</summary>
    public bool RequireCloseBackInside { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (EqualLevelTolerancePips < 0m)
        {
            errors.Add($"EqualLevelTolerancePips cannot be negative but was {EqualLevelTolerancePips}.");
        }

        if (SweepMinPenetrationPips < 0m)
        {
            errors.Add($"SweepMinPenetrationPips cannot be negative but was {SweepMinPenetrationPips}.");
        }

        return errors;
    }
}
