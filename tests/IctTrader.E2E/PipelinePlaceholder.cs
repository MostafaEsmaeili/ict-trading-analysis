namespace IctTrader.E2E;

/// <summary>
/// Placeholder anchoring the E2E suite. The Reqnroll Gherkin pipeline (candles → setup → alert →
/// paper trade → fill → performance) lands in WP9 (plan §8.3); skipped (not absent) so the suite is
/// discoverable and green.
/// </summary>
public class PipelinePlaceholder
{
    [Fact(Skip = "WP9 — Reqnroll Gherkin pipeline scenarios land here (plan §8.3).")]
    public void Full_pipeline_detects_setup_and_paper_trades()
    {
    }
}
