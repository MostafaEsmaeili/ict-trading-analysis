using IctTrader.PaperTrading.Application;
using IctTrader.PaperTrading.Infrastructure;
using IctTrader.PaperTrading.Infrastructure.Persistence;
using IctTrader.Scanning.Application;
using Microsoft.EntityFrameworkCore;

namespace IctTrader.Host;

/// <summary>
/// Composes the runnable scan loop (WP7 slice 2e): the PaperTrading EF <c>DbContext</c> on Npgsql, the aggregate-scoped
/// persistence, the Scanning + PaperTrading modules, and a read-only replay market-data feed driven by a background
/// hosted service. This is the seam that finally turns the pure, ICT-gated domain into a running backend — candles flow
/// in through the feed, the bus dispatches them to the module handlers, and confirmed advisory setups become paper
/// trades persisted to Postgres.
///
/// <para>The feed is the ONLY market-data source wired here and it is structurally read-only (a candle stream — no order
/// or broker surface anywhere), preserving the NON-NEGOTIABLE no-live-trading guardrail.</para>
/// </summary>
public static class ScanLoopRegistration
{
    public static IServiceCollection AddScanLoop(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Fail fast: a running replay feed persists paper trades, so a missing connection string would crash mid-loop
        // on the first DB write rather than at startup. Refuse to start when replay is on without a database.
        var replayEnabled = configuration.GetSection(ReplayFeedOptions.SectionName).GetValue<bool>(nameof(ReplayFeedOptions.Enabled));
        if (replayEnabled && string.IsNullOrWhiteSpace(configuration.GetConnectionString("PaperTrading")))
        {
            throw new InvalidOperationException(
                $"{ReplayFeedOptions.SectionName}:Enabled is true but ConnectionStrings:PaperTrading is not set. The " +
                "scan loop persists paper trades and needs a database — set the connection string or disable replay.");
        }

        // The PaperTrading write-model context on Npgsql. A null/empty connection string is fine at registration when
        // replay is OFF — the context is lazy and only opens a connection when a handler reads or writes (so a bare
        // Host boots even before a database is provisioned). The migrations assembly mirrors the design-time factory
        // so `dotnet ef` and the runtime resolve the same migrations.
        services.AddDbContext<PaperTradingDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("PaperTrading"),
                npgsql => npgsql.MigrationsAssembly(typeof(PaperTradingDbContext).Assembly.FullName)));

        services.AddPaperTradingPersistence();
        services.AddScanningModule();
        services.AddPaperTradingModule();

        // The replay feed config. ValidateOnStart fails fast on a self-contradictory setup (Enabled with no fixture
        // path) rather than running a silently idle scanner. The fixture is LOADED inside the hosted service (not a
        // DI factory), so a bad/missing path is caught + logged there rather than crashing host startup.
        services.AddOptions<ReplayFeedOptions>()
            .Bind(configuration.GetSection(ReplayFeedOptions.SectionName))
            .Validate(
                options => !options.Enabled || !string.IsNullOrWhiteSpace(options.FixturePath),
                $"{ReplayFeedOptions.SectionName}: FixturePath is required when the replay feed is Enabled.")
            .ValidateOnStart();

        services.AddHostedService<ReplayScannerHostedService>();

        return services;
    }
}
