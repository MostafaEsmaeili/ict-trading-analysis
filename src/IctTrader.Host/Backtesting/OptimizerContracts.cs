namespace IctTrader.Host.Backtesting;

/// <summary>
/// A parameter-sweep request (plan §15): backtest the cartesian product of symbols × styles × timeframes ×
/// risk percentages over one period and rank the combinations, so an operator can find the optimum settings for
/// each asset in each timeframe in each way of trading — and see WHERE the defensive model actually produces edge.
/// <see cref="Timeframes"/> is optional: when empty, each style runs on its default entry timeframe (Scalp→M1,
/// Intraday→M5, Swing→M15, Position→H4); when given, every style is run on every listed timeframe.
/// </summary>
public sealed record OptimizeRequest(
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Styles,
    IReadOnlyList<decimal> RiskPercents,
    decimal StartingBalance,
    IReadOnlyList<string>? Timeframes = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Objective = null,
    int TopN = 25,
    IReadOnlyList<int>? MinRequiredConditions = null,
    // Feature-subset search over WHICH concepts to require: explicit candidate subsets…
    IReadOnlyList<IReadOnlyList<string>>? RequiredConditionSets = null,
    // …or auto-generate them by dropping up to this many of the (non-MSS) default required conditions to optional.
    int? LeaveOutUpTo = null,
    // ADDITIVE (plan §16): which setup models to sweep (SetupModel member names); null/empty = [Ict2022]. Adding a
    // model multiplies the grid — the MaxCombinations cap still applies, so narrow the other axes for a 2-model sweep.
    IReadOnlyList<string>? Models = null);

/// <summary>One ranked combination's headline result — the parameters plus the key R-based metrics, ending balance,
/// and the objective score it was ranked by.</summary>
public sealed record OptimizerResultDto(
    string Symbol,
    string Timeframe,
    string Style,
    decimal RiskPercent,
    int? MinRequiredConditions,
    IReadOnlyList<string>? RequiredConditions,
    int TradeCount,
    decimal WinRate,
    decimal AverageR,
    decimal ProfitFactor,
    decimal Expectancy,
    decimal MaxDrawdownR,
    decimal EndingBalance,
    decimal Score,
    // ADDITIVE (plan §16): the setup model this combination ran, appended LAST (frozen-wire safe) — the leaderboard's
    // Model column, so the top row literally names the winning setup.
    string Model = "Ict2022");

/// <summary>The optimizer leaderboard: how many combinations ran, the objective they were ranked by, and the top N.</summary>
public sealed record OptimizeResponse(
    int CombinationCount,
    string Objective,
    IReadOnlyList<OptimizerResultDto> Results);
