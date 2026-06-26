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

    // FVG-SEM-3 validity exclusions (§2.5.10) — flag-only diagnostic evidence attached to every FVG match,
    // independent of FvgOptions.ApplyValidityExclusions (so the dashboard always has the read). TRUE = excluded.
    public const string ExcludedNoSweep = "excludedNoSweep";
    public const string ExcludedAsianRange = "excludedAsianRange";
    public const string ExcludedCounterBias = "excludedCounterBias";
    public const string ExcludedNoChoch = "excludedNoChoch";
    public const string ExcludedOverlappingWicks = "excludedOverlappingWicks";
    public const string AnyValidityExclusion = "anyValidityExclusion";

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
    public const string DisplacementLegBars = "displacementLegBars";

    // Bias / PD / OTE
    public const string EquilibriumPrice = "equilibriumPrice";
    public const string PositionPercent = "positionPercent";
    public const string OteSweetSpot = "oteSweetSpot";
    public const string RewardRatio = "rewardRatio";

    // Calendar
    public const string CalendarDate = "calendarDate";

    // Draw on liquidity
    public const string EntryPrice = "entryPrice";
    public const string StopPrice = "stopPrice";
    public const string TargetPrice = "targetPrice";

    // Standard-deviation projection targets (TGR-1/2) — the −1/−1.5/−2 SD tier prices, present only when enabled.
    public const string SdTargetPrices = "sdTargetPrices";
}
