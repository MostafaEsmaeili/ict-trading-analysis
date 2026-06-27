using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;

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

    // The four OPTIONAL §2.5.3 confluence emitters (the Grade-A enablers) — scoring-only, instrument-agnostic.
    public required OpenPriceReferenceOptions OpenPriceReference { get; init; }

    public required MacroTimeOptions MacroTime { get; init; }

    public required CleanPriceActionOptions CleanPriceAction { get; init; }

    public required CalendarDriverOptions CalendarDriver { get; init; }

    /// <summary>
    /// Returns a copy with the per-instrument-class overrides applied to the geometry/reference options the index
    /// re-defaults (<see cref="MarketContext"/>, <see cref="Liquidity"/>, <see cref="Fvg"/>,
    /// <see cref="DrawOnLiquidity"/>). An <see cref="InstrumentOptionOverrides.None"/> / FX bundle leaves every
    /// POCO at its global value, so the FX scanner pipeline is byte-identical. Detector/confluence/style/ladder
    /// options are instrument-agnostic and pass through unchanged.
    /// </summary>
    public ScannerOptions WithInstrumentOverrides(InstrumentOptionOverrides overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        return this with
        {
            MarketContext = MarketContext.WithInstrumentOverrides(overrides),
            Liquidity = Liquidity.WithInstrumentOverrides(overrides),
            Fvg = Fvg.WithInstrumentOverrides(overrides),
            DrawOnLiquidity = DrawOnLiquidity.WithInstrumentOverrides(overrides),
            // The per-instrument k-of-n relaxation (e.g. NAS100 → 6-of-8); FX None leaves it strict (byte-identical).
            Confluence = Confluence.WithInstrumentOverrides(overrides),
        };
    }
}
