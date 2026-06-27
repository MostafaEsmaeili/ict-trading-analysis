using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IctTrader.Host.Backtesting;

/// <summary>
/// Sweeps the <see cref="BacktestEngine"/> across a grid of symbols × styles × timeframes × risk percentages and
/// ranks the combinations by a chosen objective (plan §15) — the tool for finding the optimum settings per asset /
/// timeframe / style, and for surfacing where the selective §2.5 model produces a real edge. Each combination is an
/// independent in-memory engine run; runs that share a dataset read it from disk ONCE (grouped + cached) and then
/// fan out concurrently. Advisory only — it reuses the read-only backtest engine and routes nothing (§6.3).
/// </summary>
public sealed class BacktestOptimizer
{
    /// <summary>A hard ceiling on the grid size so a careless request cannot launch an unbounded sweep.</summary>
    public const int MaxCombinations = 600;

    private readonly BacktestEngine _engine;
    private readonly ILogger<BacktestOptimizer> _logger;

    public BacktestOptimizer(BacktestEngine engine, ILogger<BacktestOptimizer>? logger = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? NullLogger<BacktestOptimizer>.Instance;
    }

    public async Task<OptimizeResponse> OptimizeAsync(OptimizeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var objective = ParseObjective(request.Objective);
        var combos = BuildCombinations(request);

        if (combos.Count > MaxCombinations)
        {
            throw new ArgumentException(
                $"The sweep has {combos.Count} combinations, above the {MaxCombinations} ceiling. Narrow the symbols, " +
                "styles, timeframes or risk percentages.");
        }

        // Group by the dataset each combo reads (symbol + resolved timeframe) so a 38 MB CSV is loaded ONCE and reused
        // across the risk/style combos that share it, instead of re-read per run.
        var results = new List<OptimizerResultDto>();
        foreach (var group in combos.GroupBy(c => (c.Symbol, Timeframe: _engine.ResolveTimeframe(c))))
        {
            IReadOnlyList<Candle> candles;
            try
            {
                candles = _engine.LoadCandles(group.Key.Symbol, group.Key.Timeframe.ToString());
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "Optimizer skipping {Symbol} {Timeframe}: no dataset.", group.Key.Symbol, group.Key.Timeframe);
                continue; // no dataset for this symbol/timeframe — skip the whole group, don't fail the sweep
            }

            // Each run builds its own scanner/orchestrator/account and only READS the shared candle list, so the
            // combos in a group fan out concurrently.
            var groupResults = await Task.WhenAll(
                group.Select(combo => Task.Run(() => RunOne(combo, candles, objective), cancellationToken)))
                .ConfigureAwait(false);

            results.AddRange(groupResults.Where(r => r is not null)!);
        }

        var ranked = results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.TradeCount)
            .Take(request.TopN <= 0 ? results.Count : request.TopN)
            .ToList();

        return new OptimizeResponse(combos.Count, objective.ToString(), ranked);
    }

    private OptimizerResultDto? RunOne(BacktestRequest combo, IReadOnlyList<Candle> candles, OptimizeObjective objective)
    {
        try
        {
            var result = _engine.Run(combo, candles);
            var summary = result.Summary;
            return new OptimizerResultDto(
                result.Symbol, result.Timeframe, result.Style, combo.RiskPercent,
                summary.TradeCount, summary.WinRate, summary.AverageR, summary.ProfitFactor,
                summary.Expectancy, summary.MaxDrawdown, result.EndingBalance,
                Score(objective, summary, result.EndingBalance));
        }
        catch (ArgumentException ex)
        {
            _logger.LogDebug(ex, "Optimizer combo {Symbol} {Style} risk {Risk} was rejected: {Reason}", combo.Symbol, combo.Style, combo.RiskPercent, ex.Message);
            return null;
        }
    }

    private IReadOnlyList<BacktestRequest> BuildCombinations(OptimizeRequest request)
    {
        // null timeframe → "use the style's default entry timeframe" (resolved inside the engine).
        var timeframes = request.Timeframes is { Count: > 0 }
            ? request.Timeframes.Select(tf => (string?)tf).ToArray()
            : [null];

        var seen = new HashSet<(string, string, string, decimal)>();
        var combos = new List<BacktestRequest>();
        foreach (var symbol in request.Symbols)
        {
            foreach (var style in request.Styles)
            {
                foreach (var timeframe in timeframes)
                {
                    foreach (var risk in request.RiskPercents)
                    {
                        var combo = new BacktestRequest(
                            symbol, style, request.StartingBalance, risk, timeframe, request.FromUtc, request.ToUtc);
                        // Dedup on the RESOLVED timeframe so an explicit tf equal to a style default isn't run twice.
                        var key = (symbol, _engine.ResolveTimeframe(combo).ToString(), style, risk);
                        if (seen.Add(key))
                        {
                            combos.Add(combo);
                        }
                    }
                }
            }
        }

        return combos;
    }

    private static decimal Score(
        OptimizeObjective objective, IctTrader.Performance.Contracts.PerformanceSummaryDto summary, decimal endingBalance)
        => objective switch
        {
            OptimizeObjective.Expectancy => summary.Expectancy,
            OptimizeObjective.ProfitFactor => summary.ProfitFactor,
            OptimizeObjective.AverageR => summary.AverageR,
            OptimizeObjective.EndingBalance => endingBalance,
            _ => summary.Expectancy,
        };

    private static OptimizeObjective ParseObjective(string? objective) =>
        Enum.TryParse<OptimizeObjective>(objective, ignoreCase: true, out var parsed) ? parsed : OptimizeObjective.Expectancy;

    private static void Validate(OptimizeRequest request)
    {
        if (request.Symbols is not { Count: > 0 })
        {
            throw new ArgumentException("At least one symbol is required.");
        }

        if (request.Styles is not { Count: > 0 })
        {
            throw new ArgumentException("At least one style is required.");
        }

        if (request.RiskPercents is not { Count: > 0 })
        {
            throw new ArgumentException("At least one risk percent is required.");
        }

        if (request.StartingBalance <= 0m)
        {
            throw new ArgumentException($"StartingBalance must be positive but was {request.StartingBalance}.");
        }
    }

    private enum OptimizeObjective
    {
        Expectancy,
        ProfitFactor,
        AverageR,
        EndingBalance,
    }
}
