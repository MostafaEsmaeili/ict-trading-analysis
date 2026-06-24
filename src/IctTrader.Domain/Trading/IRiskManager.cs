using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Trading;

/// <summary>
/// The adaptive money-management policy (plan §2.4/§2.5.5) — the pure domain service that picks the effective
/// per-trade risk percent from the account's <see cref="RiskState"/> and the configured <see cref="RiskOptions"/>.
/// DECIDE-only and clock-free (mirrors <see cref="PositionSizer"/>): it reads an immutable snapshot and returns a
/// <see cref="RiskPercent"/>; the caller (the factory) feeds it to the sizer. The flat-base-risk behaviour is the
/// special case where no streak and no drawdown are in play.
/// </summary>
public interface IRiskManager
{
    /// <summary>The risk percent to size the next trade with, given the account's current adaptive-risk state.</summary>
    RiskPercent EffectiveRisk(RiskState state, RiskOptions options);
}
