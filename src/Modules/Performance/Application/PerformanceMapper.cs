using IctTrader.Domain.Services;
using IctTrader.Performance.Contracts;

namespace IctTrader.Performance.Application;

/// <summary>
/// The module boundary between the pure DOMAIN performance records and the frozen wire DTOs (plan §3.0a) — so the
/// Domain stays free of the Contracts assembly. Pure projection, no business logic.
///
/// <para><b>R-based metrics:</b> the money P&amp;L is not on the <c>PaperTradeClosed</c> wire (only <c>RealizedR</c>),
/// so every metric is R-based. The frozen <see cref="PerformanceSummaryDto.ProfitFactor"/> is gross structural R
/// (ΣwinR / |ΣlossR|), with <see cref="PerformanceCalculator.UndefinedProfitFactor"/> standing in for the
/// all-wins/no-losses infinity. <see cref="PerformanceSummaryDto.MaxDrawdown"/> is the absolute peak-to-trough drop
/// in R UNITS (the §5.3 fractional form is undefined on a baseline-0 cumulative-R curve), and
/// <see cref="EquityPointDto.Equity"/> is the running sum of R.</para>
/// </summary>
internal static class PerformanceMapper
{
    public static PerformanceSummaryDto ToDto(PerformanceSummary summary) =>
        new(
            summary.TradeCount,
            summary.WinRate,
            summary.AverageR,
            summary.ProfitFactor,
            summary.Expectancy,
            summary.MaxDrawdownR);

    public static EquityPointDto ToDto(EquityPoint point) =>
        new(point.AtUtc, point.Equity);
}
