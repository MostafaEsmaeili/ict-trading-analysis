using IctTrader.Host;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace IctTrader.E2E.Fixtures;

/// <summary>
/// Hosts the REAL <c>Program</c> for the WP9 E2E gate (plan §8.2), pointed at the Testcontainers Postgres and
/// configured so the SCENARIO pumps the pipeline rather than a background scanner:
/// <list type="bullet">
/// <item>the PaperTrading connection string is overridden to the shared container;</item>
/// <item>the replay feed is pinned OFF, so the auto-ingestion <c>BackgroundService</c> stays idle — the scenario
/// drives candles by publishing <c>CandleIngested</c> on the bus directly (deterministic, no wall-clock racing);</item>
/// <item>the entry mode is Immediate, so a confirmed advisory setup opens a paper trade directly (the §2.5
/// limit-arming retrace is exercised by the unit/integration pyramid; the E2E proves the OPEN→manage→close→perform
/// pipeline through the real Host);</item>
/// <item><see cref="TimeProvider"/> is replaced with a <see cref="FakeTimeProvider"/> anchored to the fixture's
/// New-York-anchored instant, so any clock-reading handler (the alert close-time fallback) is deterministic.</item>
/// </list>
/// The market-data feed itself is structurally read-only and left unwired (Replay disabled registers no feed), so
/// there is no live-order path anywhere — the NON-NEGOTIABLE guardrail holds (plan §6.3).
/// </summary>
public sealed class CustomWebApplicationFactory(string connectionString, DateTimeOffset clockAnchorUtc)
    : WebApplicationFactory<Program>
{
    private readonly FakeTimeProvider _timeProvider = new(clockAnchorUtc);

    /// <summary>The test clock the Host resolves — advanced by the scenario where a deterministic "now" matters.</summary>
    public FakeTimeProvider Clock => _timeProvider;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("ConnectionStrings:PaperTrading", connectionString);

        // Keep the background ingestion idle — the scenario is the only candle source (deterministic pumping).
        builder.UseSetting($"{ReplayFeedOptions.SectionName}:Enabled", "false");

        // Immediate so a confirmed setup opens a trade directly; the candle stream then manages it to a close.
        builder.UseSetting("Ict:Execution:Entry:Mode", "Immediate");

        builder.ConfigureTestServices(services =>
        {
            // Swap the system clock for a controllable fake (the Host registers TimeProvider.System as a singleton).
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_timeProvider);
        });
    }
}
