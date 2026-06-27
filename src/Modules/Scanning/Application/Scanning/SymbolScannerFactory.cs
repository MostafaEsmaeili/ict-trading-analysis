using IctTrader.Domain.Configuration;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Styles;
using IctTrader.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace IctTrader.Scanning.Application.Scanning;

/// <summary>
/// Builds a stateful <see cref="SymbolScanner"/> from the host's validated <c>Ict:*</c> options (each already
/// bound + <c>ValidateOnStart</c>-gated). It snapshots the <see cref="IOptions{T}.Value"/> set once into an
/// immutable <see cref="ScannerOptions"/>, then hands the same snapshot to every scanner it creates — so the
/// detector pipeline reads one consistent, already-validated configuration with zero magic numbers.
/// </summary>
public sealed class SymbolScannerFactory : ISymbolScannerFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly ScannerOptions _options;
    private readonly IInstrumentRegistry _instruments;

    public SymbolScannerFactory(
        TimeProvider timeProvider,
        IInstrumentRegistry instruments,
        IOptions<MarketContextOptions> marketContext,
        IOptions<ConfluenceOptions> confluence,
        IOptions<SetupCandidateOptions> setupCandidate,
        IOptions<SwingOptions> swing,
        IOptions<LiquidityOptions> liquidity,
        IOptions<DisplacementOptions> displacement,
        IOptions<MarketStructureShiftOptions> marketStructureShift,
        IOptions<FvgOptions> fvg,
        IOptions<OrderBlockOptions> orderBlock,
        IOptions<DailyBiasOptions> dailyBias,
        IOptions<PremiumDiscountOptions> premiumDiscount,
        IOptions<OteOptions> ote,
        IOptions<DrawOnLiquidityOptions> drawOnLiquidity,
        IOptions<SdProjectionOptions> sdProjection,
        IOptions<KillzoneEntryOptions> killzoneEntry,
        IOptions<CalendarOptions> calendar,
        IOptions<TradeStyleOptions> tradeStyles,
        IOptions<TargetLadderOptions> targetLadder,
        IOptions<OpenPriceReferenceOptions> openPriceReference,
        IOptions<MacroTimeOptions> macroTime,
        IOptions<CleanPriceActionOptions> cleanPriceAction,
        IOptions<CalendarDriverOptions> calendarDriver)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(instruments);
        _timeProvider = timeProvider;
        _instruments = instruments;
        _options = new ScannerOptions
        {
            MarketContext = marketContext.Value,
            Confluence = confluence.Value,
            SetupCandidate = setupCandidate.Value,
            Swing = swing.Value,
            Liquidity = liquidity.Value,
            Displacement = displacement.Value,
            MarketStructureShift = marketStructureShift.Value,
            Fvg = fvg.Value,
            OrderBlock = orderBlock.Value,
            DailyBias = dailyBias.Value,
            PremiumDiscount = premiumDiscount.Value,
            Ote = ote.Value,
            DrawOnLiquidity = drawOnLiquidity.Value,
            SdProjection = sdProjection.Value,
            KillzoneEntry = killzoneEntry.Value,
            Calendar = calendar.Value,
            TradeStyles = tradeStyles.Value,
            TargetLadder = targetLadder.Value,
            OpenPriceReference = openPriceReference.Value,
            MacroTime = macroTime.Value,
            CleanPriceAction = cleanPriceAction.Value,
            CalendarDriver = calendarDriver.Value,
        };
    }

    public SymbolScanner Create(Symbol symbol, TradeStyle style)
        => new(symbol, style, _timeProvider, _options, _instruments);
}
