using IctTrader.Domain.Configuration;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// A resolved snapshot of every validated <c>Ict:*</c> Options POCO a <see cref="SymbolScanner"/> needs to
/// build the pinned detector pipeline + the FSM + the priced-setup factory. The factory composes this from the
/// host's <c>IOptions&lt;T&gt;.Value</c> set (each already bound + <c>ValidateOnStart</c>-gated by
/// <c>IctOptionsRegistration</c>), so the scanner stays free of DI and magic numbers — it only USES the
/// already-validated configuration. The same instance is shared by every (symbol, style) scanner; the
/// detectors treat it as read-only.
/// </summary>
public sealed record ScannerOptions
{
    public required MarketContextOptions MarketContext { get; init; }

    public required ConfluenceOptions Confluence { get; init; }

    public required SetupCandidateOptions SetupCandidate { get; init; }

    public required SwingOptions Swing { get; init; }

    public required LiquidityOptions Liquidity { get; init; }

    public required DisplacementOptions Displacement { get; init; }

    public required MarketStructureShiftOptions MarketStructureShift { get; init; }

    public required FvgOptions Fvg { get; init; }

    public required OrderBlockOptions OrderBlock { get; init; }

    public required DailyBiasOptions DailyBias { get; init; }

    public required PremiumDiscountOptions PremiumDiscount { get; init; }

    public required OteOptions Ote { get; init; }

    public required DrawOnLiquidityOptions DrawOnLiquidity { get; init; }

    public required SdProjectionOptions SdProjection { get; init; }

    public required KillzoneEntryOptions KillzoneEntry { get; init; }

    public required CalendarOptions Calendar { get; init; }

    public required TradeStyleOptions TradeStyles { get; init; }

    public required TargetLadderOptions TargetLadder { get; init; }
}
