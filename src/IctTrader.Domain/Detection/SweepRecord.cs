using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// A record of the most recent liquidity sweep (plan §2.5.1 step 4). The MSS detector consumes it to enforce
/// the sweep-must-precede-MSS rule (§2.5.10) within a configured bar window. <see cref="Direction"/> is the
/// trade direction the sweep enables (sweeping buy-side liquidity enables a bearish trade).
/// </summary>
public readonly record struct SweepRecord(Direction Direction, decimal Level, DateTimeOffset AtUtc, long BarIndex);
