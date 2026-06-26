using FluentAssertions;
using IctTrader.Domain.Services;

namespace IctTrader.UnitTests.Services;

/// <summary>
/// Locks the pure §5.3 <see cref="PerformanceCalculator"/>. Every metric is <b>R-based</b> — the money P&amp;L is not
/// on the <c>PaperTradeClosed</c> wire, only <c>RealizedR</c> — so the calculator folds a stream of closed-trade R
/// outcomes into the summary + the cumulative-R equity curve. The trichotomy (R&gt;0 win / R==0 scratch / R&lt;0 loss)
/// matches the already-shipped §2.4/§2.5.5 gross-outcome classifier the §2.5.9 breakeven-trail feeds (it books ~0R).
/// </summary>
public class PerformanceCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2024, 7, 1, 7, 0, 0, TimeSpan.Zero);

    private static ClosedTradeR Closed(decimal r, int minute) =>
        new(r, T0.AddMinutes(minute));

    [Fact]
    public void Worked_example_folds_the_mixed_stream_into_the_summary()
    {
        // +2R, -1R, +3R, -1R: 2 wins, 2 losses, ΣR = 3.
        var trades = new[] { Closed(2m, 0), Closed(-1m, 5), Closed(3m, 10), Closed(-1m, 15) };

        var summary = PerformanceCalculator.Summarize(trades);

        summary.TradeCount.Should().Be(4);
        summary.WinRate.Should().Be(0.5m);          // 2 / 4
        summary.AverageR.Should().Be(0.75m);         // 3 / 4
        summary.ProfitFactor.Should().Be(2.5m);      // ΣwinR 5 / |ΣlossR| 2
        summary.Expectancy.Should().Be(0.75m);       // 0.5*2.5 - 0.5*1.0 == AverageR
        summary.MaxDrawdownR.Should().Be(1m);        // peak 4, trough 3 -> 1R drop (absolute, R units)
    }

    [Fact]
    public void Equity_curve_is_cumulative_R_ordered_by_close_time_from_the_baseline()
    {
        var trades = new[] { Closed(2m, 0), Closed(-1m, 5), Closed(3m, 10), Closed(-1m, 15) };

        var curve = PerformanceCalculator.EquityCurve(trades);

        curve.Select(p => p.Equity).Should().Equal(2m, 1m, 4m, 3m);     // running ΣR from 0
        curve.Select(p => p.AtUtc).Should().Equal(
            T0, T0.AddMinutes(5), T0.AddMinutes(10), T0.AddMinutes(15));
    }

    [Fact]
    public void Equity_curve_starts_from_an_explicit_baseline_when_supplied()
    {
        var trades = new[] { Closed(2m, 0), Closed(-1m, 5) };

        var curve = PerformanceCalculator.EquityCurve(trades, startingEquity: 100m);

        curve.Select(p => p.Equity).Should().Equal(102m, 101m);
    }

    [Fact]
    public void Equity_curve_orders_out_of_order_closes_by_time()
    {
        // Fed out of close-time order; the curve must sort so cumulative R is chronological.
        var trades = new[] { Closed(3m, 10), Closed(2m, 0), Closed(-1m, 5) };

        var curve = PerformanceCalculator.EquityCurve(trades);

        curve.Select(p => p.AtUtc).Should().Equal(T0, T0.AddMinutes(5), T0.AddMinutes(10));
        curve.Select(p => p.Equity).Should().Equal(2m, 1m, 4m);
    }

    [Fact]
    public void Empty_input_yields_all_zeros_and_an_empty_curve()
    {
        var summary = PerformanceCalculator.Summarize([]);

        summary.TradeCount.Should().Be(0);
        summary.WinRate.Should().Be(0m);
        summary.AverageR.Should().Be(0m);
        summary.ProfitFactor.Should().Be(0m);        // documented "n/a — no trades" sentinel
        summary.Expectancy.Should().Be(0m);
        summary.MaxDrawdownR.Should().Be(0m);
        PerformanceCalculator.EquityCurve([]).Should().BeEmpty();
    }

    [Fact]
    public void All_wins_no_losses_returns_the_documented_undefined_profit_factor_sentinel()
    {
        // No losing trade -> |ΣlossR| == 0 -> profit factor is +infinity. The frozen DTO field is a plain decimal, so
        // we surface a large positive sentinel (NOT 0m, which would read as worst-case for the best-case record).
        var trades = new[] { Closed(2m, 0), Closed(3m, 5) };

        var summary = PerformanceCalculator.Summarize(trades);

        summary.WinRate.Should().Be(1m);
        summary.ProfitFactor.Should().Be(PerformanceCalculator.UndefinedProfitFactor);
        summary.ProfitFactor.Should().BeGreaterThan(0m, "an unbroken win streak is the best case, never 0");
    }

    [Fact]
    public void A_scratch_counts_in_the_total_but_is_neither_a_win_nor_a_loss()
    {
        // +2R win, 0R scratch (a §2.5.9 breakeven trail), -1R loss. WinRate 1/3, LossRate 1/3, ΣR = 1.
        var trades = new[] { Closed(2m, 0), Closed(0m, 5), Closed(-1m, 10) };

        var summary = PerformanceCalculator.Summarize(trades);

        summary.TradeCount.Should().Be(3);
        summary.WinRate.Should().BeApproximately(1m / 3m, 1e-9m);
        summary.AverageR.Should().BeApproximately(1m / 3m, 1e-9m);
        // Expectancy == AverageR: 1/3*2 - 1/3*1 = 1/3 (the scratch dilutes via the total denominator).
        summary.Expectancy.Should().BeApproximately(1m / 3m, 1e-9m);
        summary.ProfitFactor.Should().Be(2m);        // win 2 / |loss| 1
    }

    [Fact]
    public void Max_drawdown_is_the_largest_peak_to_trough_drop_in_R()
    {
        // Curve: 5, 3, 8, 2. Peaks: 5,5,8,8. Drops: 0,2,0,6 -> max 6R (the 8 -> 2 leg).
        var trades = new[] { Closed(5m, 0), Closed(-2m, 5), Closed(5m, 10), Closed(-6m, 15) };

        var summary = PerformanceCalculator.Summarize(trades);

        summary.MaxDrawdownR.Should().Be(6m);
    }

    [Fact]
    public void All_losses_has_zero_profit_factor_and_negative_expectancy()
    {
        var trades = new[] { Closed(-1m, 0), Closed(-1m, 5) };

        var summary = PerformanceCalculator.Summarize(trades);

        summary.WinRate.Should().Be(0m);
        summary.ProfitFactor.Should().Be(0m);        // ΣwinR 0 / |ΣlossR| 2 -> a true 0, not the sentinel
        summary.Expectancy.Should().Be(-1m);         // 0*0 - 1*1
        summary.MaxDrawdownR.Should().Be(2m);
    }
}
