using IctTrader.Domain.ValueObjects;

namespace IctTrader.Domain.Detection;

/// <summary>
/// A pure, deterministic ICT detector (plan §4.2/§3.0). <see cref="Detect"/> reads the
/// <see cref="MarketContext"/> and the current candle and returns a <see cref="DetectorResult"/> — it
/// performs no I/O, never reads an ambient clock, and is TOTAL (returns <see cref="DetectorResult.NoMatch"/>
/// rather than throwing on a small window or absent pattern). Structural detectors may mutate ONLY their
/// own registry on the context; confluence detectors mutate nothing.
/// </summary>
public interface ISetupDetector
{
    /// <summary>The confluence this detector emits (its slot in the scorer); null for non-scoring feeders.</summary>
    ConfluenceCondition? Condition { get; }

    DetectorResult Detect(MarketContext context, Candle current);
}
