namespace IctTrader.IntegrationTests;

/// <summary>
/// Placeholder anchoring the integration suite. Real per-slice round-trips against a Testcontainers
/// Postgres land in WP2 (plan §8.1); skipped (not absent) so the suite is discoverable and green.
/// </summary>
public class PersistencePlaceholder
{
    [Fact(Skip = "WP2 — Testcontainers Postgres round-trip tests land here (plan §8.1).")]
    public void Persistence_round_trips_against_postgres()
    {
    }
}
