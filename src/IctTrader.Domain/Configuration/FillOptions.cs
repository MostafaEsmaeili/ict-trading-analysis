using IctTrader.Domain.Trading;

namespace IctTrader.Domain.Configuration;

/// <summary>
/// The intrabar fill-resolution policy (plan §5.2, bound from <c>Ict:Execution:Fills</c>) — no magic numbers.
/// This slice models the exit leg (stop / runner) of an already-open trade; the realistic entry-touch
/// (Pending→Open), partial scale-outs, breakeven arming, time-exit, and the §5.4 cost model (spread / commission /
/// slippage / swap, weekend gap-through) are deferred follow-ons that will extend this section.
/// </summary>
public sealed class FillOptions
{
    public const string SectionName = "Ict:Execution:Fills";

    /// <summary>
    /// How a bar that touches both the stop and the runner is resolved. Default
    /// <see cref="IntrabarFillAssumption.StopFirst"/> — the conservative worst-case that never overstates
    /// strategy performance (§5.2/§5.4).
    /// </summary>
    public IntrabarFillAssumption StopVsTarget { get; init; } = IntrabarFillAssumption.StopFirst;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!Enum.IsDefined(StopVsTarget))
        {
            errors.Add($"StopVsTarget must be a defined intrabar fill assumption but was {StopVsTarget}.");
        }

        return errors;
    }
}
