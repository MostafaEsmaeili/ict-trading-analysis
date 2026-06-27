using IctTrader.Domain.Instruments;

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

    /// <summary>
    /// Returns a copy with the instrument-class scalar overrides applied where present
    /// (<see cref="EqualLevelTolerancePips"/>). A <see cref="InstrumentOptionOverrides.None"/> / FX bundle leaves
    /// every field unchanged (byte-identical). <see cref="SweepMinPenetrationPips"/> (0.5 ≈ 2 ICT ticks) is DERIVED
    /// from the "penetrate by a tick or two" rule and stays at its global value for the index too.
    /// </summary>
    public LiquidityOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return new LiquidityOptions
        {
            EqualLevelTolerancePips = overrides.EqualLevelTolerancePips ?? EqualLevelTolerancePips,
            SweepMinPenetrationPips = SweepMinPenetrationPips,
            RequireCloseBackInside = RequireCloseBackInside,
        };
    }

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
