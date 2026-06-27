using IctTrader.Domain.Configuration;
using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection.Detectors;

/// <summary>
/// Emits the OPTIONAL <see cref="ConfluenceCondition.OpenPriceReference"/> confluence (0.50, plan §2.5.2 step 4 /
/// §2.5.8). The §2.5 model wants the confirming price on the bias-correct side of the day's reference open: a BEARISH
/// setup forms when price was driven into PREMIUM (the Judas rally ABOVE the open) before reversing down, a BULLISH
/// one when price is in DISCOUNT (BELOW the open). This reads the SAME reference open the Judas-sweep detector uses —
/// <see cref="MarketContext.ReferenceOpen(bool)"/> (the FX midnight open, or, when enabled, the dual midnight/08:30
/// macro reference) — so the two can never disagree.
///
/// <para>It is a confluence (scoring-only), NOT a RequiredCondition, so its absence never blocks a setup; it only
/// promotes a complete setup toward Grade A. It emits the bias direction, so the FSM counts it only when it agrees
/// with the MSS-locked direction (a price on the wrong side of the open simply does not contribute).</para>
/// </summary>
public sealed class OpenPriceReferenceDetector : ISetupDetector
{
    private readonly OpenPriceReferenceOptions _options;

    public OpenPriceReferenceDetector(OpenPriceReferenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public ConfluenceCondition? Condition => ConfluenceCondition.OpenPriceReference;

    public DetectorResult Detect(MarketContext context, Candle current)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.Enabled || context.Bias is not { } bias)
        {
            return DetectorResult.NoMatch; // disabled, or neutral bias -> nothing to corroborate
        }

        var premium = bias == Direction.Bearish;
        if (context.ReferenceOpen(premium) is not { } referenceOpen)
        {
            return DetectorResult.NoMatch; // no midnight/macro open captured yet
        }

        // BEARISH wants the confirming price strictly ABOVE the open (premium / Judas rally); BULLISH strictly BELOW
        // (discount). Exactly AT the open is neither, so it does not corroborate (mirrors the strict equilibrium rule).
        var price = current.Close;
        var agreesWithBias = bias == Direction.Bearish ? price > referenceOpen : price < referenceOpen;
        if (!agreesWithBias)
        {
            return DetectorResult.NoMatch;
        }

        var evidence = new Dictionary<string, object>
        {
            [EvidenceKeys.Direction] = bias.ToString(),
            [EvidenceKeys.ReferenceOpenPrice] = referenceOpen,
        };

        return DetectorResult.Match(
            bias, referenceOpen, ReasonFragments.OpenPriceReference(bias, referenceOpen), evidence);
    }
}
