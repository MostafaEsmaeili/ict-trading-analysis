namespace IctTrader.Domain.Detection;

/// <summary>
/// Stable string keys for <see cref="DetectorResult.Evidence"/> (plan §4.6 — no magic strings). Evidence
/// is persisted as JSONB and read by the chart overlays (plan §9.1), so the keys are a contract: define
/// each once here, never inline. Values are primitive/decimal/string/bool/enum-name for deterministic,
/// sorted-key serialization.
/// </summary>
public static class EvidenceKeys
{
    // Lifecycle
    public const string Invalidated = "invalidated";
    public const string VoidedArrayId = "voidedArrayId";
    public const string Reason = "reason";

    // Common
    public const string Timeframe = "timeframe";
    public const string Direction = "direction";

    // Sessions
    public const string Killzone = "killzone";
    public const string LunchBlocked = "lunchBlocked";
    public const string NoNewEntry = "noNewEntry";

    // Fair value gap
    public const string GapTopPrice = "gapTopPrice";
    public const string GapBottomPrice = "gapBottomPrice";
    public const string InCorrectHalf = "inCorrectHalf";
    public const string Stacked = "stacked";
    public const string TouchCount = "touchCount";

    // Order block
    public const string OpeningPrice = "openingPrice";
    public const string MeanThreshold = "meanThreshold";

    // Liquidity / sweep
    public const string PoolLevel = "poolLevel";
    public const string SweptLevel = "sweptLevel";
    public const string IsJudas = "isJudas";

    // Structure
    public const string SwingPrice = "swingPrice";
    public const string BrokenSwingPrice = "brokenSwingPrice";
    public const string DisplacementPips = "displacementPips";

    // Bias / PD / OTE
    public const string EquilibriumPrice = "equilibriumPrice";
    public const string PositionPercent = "positionPercent";
    public const string OteSweetSpot = "oteSweetSpot";
    public const string RewardRatio = "rewardRatio";
}
