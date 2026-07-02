using FluentAssertions;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection.Detectors;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Scanning.Application.Scanning;
using IctTrader.Scanning.Application.Scanning.Models;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.UnitTests.Scanning;

/// <summary>
/// Locks the multi-model catalog seam (plan §16). The GOLDEN test pins the Ict2022 definition's pipeline
/// type-for-type, in order, to the pre-catalog hardcoded recipe — moving the model into the catalog must be
/// byte-identical (same detectors, same pinned order), or the §2.5 scan behavior silently drifts.
/// </summary>
public sealed class SetupModelCatalogTests
{
    [Fact]
    public void Ict2022_pipeline_is_the_exact_pinned_precatalog_recipe()
    {
        var pipeline = Ict2022ModelDefinition.Definition.BuildPipeline(
            DefaultOptions(), new NyClock(new FakeTimeProvider()));

        pipeline.Select(d => d.GetType()).Should().Equal(
            typeof(SwingPointDetector),
            typeof(LiquidityPoolDetector),
            typeof(DealingRangeContextDetector),
            typeof(LiquiditySweepDetector),
            typeof(DisplacementDetector),
            typeof(MarketStructureShiftDetector),
            typeof(FairValueGapDetector),
            typeof(OrderBlockDetector),
            typeof(DailyBiasDetector),
            typeof(PremiumDiscountGateDetector),
            typeof(OteFibDetector),
            typeof(DrawOnLiquidityDetector),
            typeof(KillzoneEntryDetector),
            typeof(CalendarGateDetector),
            typeof(OpenPriceReferenceDetector),
            typeof(MacroTimeDetector),
            typeof(CleanPriceActionDetector),
            typeof(CalendarDriverDetector));
    }

    [Fact]
    public void Ict2022_preset_is_the_identity()
    {
        var options = DefaultOptions();

        Ict2022ModelDefinition.Definition.ApplyPreset(options).Should().BeSameAs(
            options, "the global Ict:* defaults ARE the 2022 model's mined parameters — no deltas to overlay");
    }

    [Fact]
    public void Resolving_an_unregistered_model_fails_fast()
    {
        var resolve = () => SetupModelCatalog.Default.Resolve(SetupModel.Ict2024);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*Ict2024*no registered definition*", because:
                "a selected model without a shipped pipeline must fail loudly, never silently scan the wrong recipe");
    }

    [Fact]
    public void Duplicate_model_registration_is_rejected()
    {
        var build = () => new SetupModelCatalog(
            [Ict2022ModelDefinition.Definition, Ict2022ModelDefinition.Definition]);

        build.Should().Throw<ArgumentException>().WithMessage("*registered more than once*");
    }

    [Fact]
    public void Resolved_active_models_default_to_the_canonical_model_and_dedupe()
    {
        new MarketContextOptions().ResolvedActiveModels.Should().Equal(SetupModel.Ict2022);

        new MarketContextOptions { ActiveModels = [SetupModel.Ict2022, SetupModel.Ict2022] }
            .ResolvedActiveModels.Should().Equal(SetupModel.Ict2022);

        new MarketContextOptions { ActiveModels = [SetupModel.Ict2024] }
            .ResolvedActiveModels.Should().Equal(SetupModel.Ict2024);
    }

    private static ScannerOptions DefaultOptions() => new()
    {
        MarketContext = new MarketContextOptions(),
        Confluence = new ConfluenceOptions(),
        SetupCandidate = new SetupCandidateOptions(),
        Swing = new SwingOptions(),
        Liquidity = new LiquidityOptions(),
        Displacement = new DisplacementOptions(),
        MarketStructureShift = new MarketStructureShiftOptions(),
        Fvg = new FvgOptions(),
        OrderBlock = new OrderBlockOptions(),
        DailyBias = new DailyBiasOptions(),
        PremiumDiscount = new PremiumDiscountOptions(),
        Ote = new OteOptions(),
        DrawOnLiquidity = new DrawOnLiquidityOptions(),
        SdProjection = new SdProjectionOptions(),
        KillzoneEntry = new KillzoneEntryOptions(),
        SilverBullet = new SilverBulletOptions(),
        Calendar = new CalendarOptions(),
        TradeStyles = new TradeStyleOptions(),
        TargetLadder = new TargetLadderOptions(),
        OpenPriceReference = new OpenPriceReferenceOptions(),
        MacroTime = new MacroTimeOptions(),
        CleanPriceAction = new CleanPriceActionOptions(),
        CalendarDriver = new CalendarDriverOptions(),
    };
}
