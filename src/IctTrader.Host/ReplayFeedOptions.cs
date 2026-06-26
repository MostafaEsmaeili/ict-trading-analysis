namespace IctTrader.Host;

/// <summary>
/// The replay market-data feed's configuration (bound from <c>Ict:MarketData:Replay</c>) — no magic strings/paths. The
/// replay feed is the only feed wired in the runnable backend (WP7 slice 2e): it streams a deterministic candle fixture
/// from disk so the scan→paper-trade chain can be exercised end-to-end without any live broker connection (the
/// NON-NEGOTIABLE guardrail — every feed is read-only by shape). It is OFF by default so a bare Host stays idle until an
/// operator opts in with a fixture path.
/// </summary>
public sealed class ReplayFeedOptions
{
    public const string SectionName = "Ict:MarketData:Replay";

    /// <summary>When false (the default) the hosted scanner stays idle and ingests nothing.</summary>
    public bool Enabled { get; init; }

    /// <summary>Absolute or relative path to the CSV candle fixture the replay feed streams. Required when enabled.</summary>
    public string? FixturePath { get; init; }
}
