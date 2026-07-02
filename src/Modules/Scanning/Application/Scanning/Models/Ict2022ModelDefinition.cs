using IctTrader.Domain.Detection;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Setups;

namespace IctTrader.Scanning.Application.Scanning.Models;

/// <summary>
/// THE canonical ICT 2022 Mentorship intraday FVG model (plan §2.5) as a catalog definition. The pipeline array
/// below is the pre-catalog <see cref="SymbolScanner"/> recipe moved VERBATIM (same detectors, same pinned
/// order, same option wiring), and the preset is the identity — so composing a scanner through this definition
/// is byte-identical to the original hardcoded path (locked by the golden pipeline-equality test).
/// </summary>
public static class Ict2022ModelDefinition
{
    public static SetupModelDefinition Definition { get; } = new()
    {
        Id = SetupModel.Ict2022,
        DisplayName = "ICT 2022 Mentorship",
        // The global Ict:* defaults ARE the 2022 model's mined parameters — no deltas to overlay.
        ApplyPreset = static options => options,
        // The PINNED canonical order (ScanSessionTests): SwingPointDetector before the MSS, and the
        // displacement feeder before the MSS, so the breach-vs-MSS race is deterministic (spec §5 item 19).
        // The four OPTIONAL §2.5.3 confluence emitters (OpenPriceReference → MacroTime → CleanPriceAction →
        // CalendarDriver) run AFTER the structural + RequiredCondition detectors — they read the bias / reference
        // open / displacement leg / calendar those already populated, and contribute ONLY to the score (never a
        // RequiredCondition), so they promote a complete setup toward Grade A without changing Σ(applicable).
        BuildPipeline = static (resolvedOptions, nyClock) => new ISetupDetector[]
        {
            new SwingPointDetector(resolvedOptions.Swing),
            new LiquidityPoolDetector(resolvedOptions.Liquidity),
            new DealingRangeContextDetector(resolvedOptions.PremiumDiscount),
            new LiquiditySweepDetector(resolvedOptions.Liquidity),
            new DisplacementDetector(resolvedOptions.Displacement),
            new MarketStructureShiftDetector(resolvedOptions.MarketStructureShift),
            new FairValueGapDetector(resolvedOptions.Fvg),
            new OrderBlockDetector(resolvedOptions.OrderBlock),
            new DailyBiasDetector(resolvedOptions.DailyBias),
            new PremiumDiscountGateDetector(resolvedOptions.PremiumDiscount),
            new OteFibDetector(resolvedOptions.Ote, resolvedOptions.Fvg),
            new DrawOnLiquidityDetector(
                resolvedOptions.DrawOnLiquidity,
                resolvedOptions.Ote,
                resolvedOptions.TradeStyles,
                resolvedOptions.Fvg,
                resolvedOptions.SdProjection),
            new KillzoneEntryDetector(resolvedOptions.KillzoneEntry, resolvedOptions.SilverBullet),
            new CalendarGateDetector(resolvedOptions.Calendar),
            new OpenPriceReferenceDetector(resolvedOptions.OpenPriceReference),
            new MacroTimeDetector(nyClock, resolvedOptions.MacroTime),
            new CleanPriceActionDetector(resolvedOptions.CleanPriceAction),
            new CalendarDriverDetector(resolvedOptions.CalendarDriver, resolvedOptions.Calendar),
        },
    };
}
