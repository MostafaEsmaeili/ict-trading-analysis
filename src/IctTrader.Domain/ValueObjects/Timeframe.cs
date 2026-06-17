namespace IctTrader.Domain.ValueObjects;

/// <summary>
/// A chart timeframe across the ICT top-down cascade (plan §4.7: Daily → H1/H4 → M15 → M5/M3 → M1).
/// FROZEN CONTRACT (plan §11.1): names back the per-style TimeframePolicy and the chart's trigger badge.
/// </summary>
public enum Timeframe
{
    M1,
    M3,
    M5,
    M15,
    M30,
    H1,
    H4,
    D1,
    W1,
    MN1,
}
