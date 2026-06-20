namespace IctTrader.Domain.Configuration;

/// <summary>
/// The broker-realism cost policy (plan §5.4, bound from <c>Ict:Execution</c>) — no magic numbers. This slice
/// binds only the two deterministic, always-present FX costs: the round-trip <see cref="Spread"/> and the
/// <see cref="Commission"/>. The §5.4 sub-sections that need market/clock context — the session-stepped spread
/// (Asian/news widening), slippage tiers, swap/rollover, weekend gap, partial fills/latency — are NOT bound here;
/// they land with their own follow-on slices so <c>ValidateOnStart</c> stays honest about what is actually wired.
/// </summary>
public sealed class ExecutionCostOptions
{
    public const string SectionName = "Ict:Execution";

    public SpreadOptions Spread { get; init; } = new();

    public CommissionOptions Commission { get; init; } = new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Spread.Validate());
        errors.AddRange(Commission.Validate());
        return errors;
    }
}

/// <summary>
/// The dealing spread (plan §5.4). This slice models a FLAT spread charged on BOTH legs of the round trip — which
/// is faithful for the §2.5 killzone-gated entries (the §5.4 base and peak spreads are equal during the
/// London/NY active hours we trade, and the news minute is already vetoed by the <c>CalendarClear</c> condition).
/// The session step-function (Asian/news widening) is a follow-on slice and will add its model selector then.
/// </summary>
public sealed class SpreadOptions
{
    /// <summary>The dealing spread in pips, paid once per leg crossing (§5.4 default 0.7 for FX majors).</summary>
    public decimal BasePips { get; init; } = 0.7m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (BasePips < 0m)
        {
            errors.Add($"Spread.BasePips cannot be negative but was {BasePips}.");
        }

        return errors;
    }
}

/// <summary>The dealing commission (plan §5.4) — a round-turn (both legs) charge per lot, in account currency.</summary>
public sealed class CommissionOptions
{
    /// <summary>The round-trip commission per lot in account currency (§5.4 default 6.0 USD/lot ECN).</summary>
    public decimal PerLotRoundTripUsd { get; init; } = 6.0m;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PerLotRoundTripUsd < 0m)
        {
            errors.Add($"Commission.PerLotRoundTripUsd cannot be negative but was {PerLotRoundTripUsd}.");
        }

        return errors;
    }
}
