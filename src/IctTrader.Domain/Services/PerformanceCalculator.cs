namespace IctTrader.Domain.Services;

/// <summary>
/// One closed paper-trade's outcome expressed in R (the §5.2 frozen-1R multiple) and the bar-close time it settled.
/// R is the only per-closed-trade signal on the <c>PaperTradeClosed</c> wire — the money P&amp;L is not carried — so
/// every §5.3 metric the <see cref="PerformanceCalculator"/> derives is R-based.
/// </summary>
public readonly record struct ClosedTradeR(decimal R, DateTimeOffset ClosedAtUtc);

/// <summary>
/// A point on the cumulative-R equity curve (plan §5.3): the running sum of R at the moment a trade closed. With the
/// default zero baseline the curve is in pure R units (the module maps this to the wire <c>EquityPointDto</c>).
/// </summary>
public readonly record struct EquityPoint(DateTimeOffset AtUtc, decimal Equity);

/// <summary>
/// The R-based §5.3 performance summary (a pure DOMAIN record — the module maps it to the Contracts DTO, so the
/// Domain stays dependency-free). All fields are R-based because the money P&amp;L is not on the wire.
/// <para><see cref="ProfitFactor"/> is gross structural R (ΣwinR / |ΣlossR|): when there are losses it is the real
/// ratio; with no trades it is <c>0</c> (read as "n/a"); with trades but NO losses it is
/// <see cref="PerformanceCalculator.UndefinedProfitFactor"/> (the mathematically-infinite all-wins case — NOT 0,
/// which would read as the worst case for the best record).</para>
/// <para><see cref="MaxDrawdownR"/> is the largest peak-to-trough drop on the cumulative-R curve, expressed as a
/// POSITIVE decimal in R UNITS. The §5.3 fractional form <c>(peak−E)/peak</c> is undefined on a baseline-0
/// cumulative-R curve (the early peak is 0/negative), so the always-valid absolute-R drop is the canonical output.</para>
/// </summary>
public readonly record struct PerformanceSummary(
    int TradeCount,
    decimal WinRate,
    decimal AverageR,
    decimal ProfitFactor,
    decimal Expectancy,
    decimal MaxDrawdownR);

/// <summary>
/// The pure §5.3 performance domain service. It folds a list of closed-trade R outcomes into the
/// <see cref="PerformanceSummary"/> and the cumulative-R <see cref="EquityCurve(IReadOnlyCollection{ClosedTradeR}, decimal)"/>.
/// <para>Trichotomy: R&gt;0 is a win, R==0 is a SCRATCH (e.g. a §2.5.9 breakeven trail that books ~0R — excluded from
/// wins, included in the total so it dilutes the rate honestly), R&lt;0 is a loss. This matches the already-shipped
/// §2.4/§2.5.5 gross-outcome classifier so the analytics and the adaptive-risk feeder never disagree on what a
/// breakeven is. The R values are exact decimals (R = GrossPnl / RiskBudget), so <c>== 0m</c> is safe — no tolerance.</para>
/// <para>Read-only analytics (plan §6.3 guardrail): no order path, no clock, no I/O — the close time arrives on each
/// <see cref="ClosedTradeR"/>, never from <c>DateTime.Now</c>.</para>
/// </summary>
public static class PerformanceCalculator
{
    /// <summary>The cumulative-R curve's default baseline: pure R units (the running sum starts at 0).</summary>
    public const decimal DefaultStartingEquity = 0m;

    /// <summary>
    /// The sentinel surfaced for <see cref="PerformanceSummary.ProfitFactor"/> in the all-wins / no-losses case, where
    /// the ratio is mathematically infinite. A large positive value (NOT 0) so a dashboard reads it as "best case /
    /// undefined — no losing trades", never as a worst-case zero. The frozen DTO field is a plain decimal, so the
    /// state can't be expressed as null; this named constant documents the meaning instead of a magic number.
    /// </summary>
    public const decimal UndefinedProfitFactor = 999_999m;

    /// <summary>
    /// Computes the R-based §5.3 summary over the closed trades. Empty input yields all zeros. Pure and deterministic.
    /// </summary>
    public static PerformanceSummary Summarize(IReadOnlyCollection<ClosedTradeR> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);

        var total = trades.Count;
        if (total == 0)
        {
            return new PerformanceSummary(0, 0m, 0m, 0m, 0m, 0m);
        }

        var wins = 0;
        var losses = 0;
        var sumR = 0m;
        var sumWinR = 0m;
        var sumLossMagnitudeR = 0m;
        foreach (var trade in trades)
        {
            sumR += trade.R;
            if (trade.R > 0m)
            {
                wins++;
                sumWinR += trade.R;
            }
            else if (trade.R < 0m)
            {
                losses++;
                sumLossMagnitudeR += -trade.R;
            }
            // R == 0m is a scratch: counted in `total`, in neither the win nor loss aggregate.
        }

        var winRate = (decimal)wins / total;
        var lossRate = (decimal)losses / total;
        var averageR = sumR / total;

        // ProfitFactor = ΣwinR / |ΣlossR|. No losses -> infinite: the sentinel if any wins exist, else a true 0 (no trades' edge).
        var profitFactor = losses > 0
            ? sumWinR / sumLossMagnitudeR
            : (wins > 0 ? UndefinedProfitFactor : 0m);

        // Expectancy = WinRate*AvgWinR - LossRate*AvgLossR. Zero-winner/zero-loser averages collapse cleanly to 0.
        var averageWinR = wins > 0 ? sumWinR / wins : 0m;
        var averageLossR = losses > 0 ? sumLossMagnitudeR / losses : 0m;
        var expectancy = (winRate * averageWinR) - (lossRate * averageLossR);

        var maxDrawdownR = MaxDrawdownR(trades);

        return new PerformanceSummary(total, winRate, averageR, profitFactor, expectancy, maxDrawdownR);
    }

    /// <summary>
    /// The cumulative-R equity curve (plan §5.3 <c>E_k = E_{k-1} + R_k</c>), ordered by close time, from
    /// <paramref name="startingEquity"/> (default <see cref="DefaultStartingEquity"/> -> pure R units). Pure.
    /// </summary>
    public static IReadOnlyList<EquityPoint> EquityCurve(
        IReadOnlyCollection<ClosedTradeR> trades, decimal startingEquity = DefaultStartingEquity)
    {
        ArgumentNullException.ThrowIfNull(trades);

        var points = new List<EquityPoint>(trades.Count);
        var running = startingEquity;
        foreach (var trade in trades.OrderBy(t => t.ClosedAtUtc))
        {
            running += trade.R;
            points.Add(new EquityPoint(trade.ClosedAtUtc, running));
        }

        return points;
    }

    /// <summary>
    /// The largest peak-to-trough drop on the cumulative-R curve, as a POSITIVE decimal in R units (§5.3, absolute
    /// form — see <see cref="PerformanceSummary.MaxDrawdownR"/> for why the fractional form is undefined here).
    /// Computed over the same chronological cumulative-R series the equity curve exposes.
    /// </summary>
    private static decimal MaxDrawdownR(IReadOnlyCollection<ClosedTradeR> trades)
    {
        var running = DefaultStartingEquity;
        var peak = DefaultStartingEquity;
        var maxDrawdown = 0m;
        foreach (var trade in trades.OrderBy(t => t.ClosedAtUtc))
        {
            running += trade.R;
            if (running > peak)
            {
                peak = running;
            }

            var drawdown = peak - running;
            if (drawdown > maxDrawdown)
            {
                maxDrawdown = drawdown;
            }
        }

        return maxDrawdown;
    }
}
