using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IctTrader.MarketData.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="MarketDataDbContext"/> (plan §7).
/// <para>
/// The <c>dotnet ef</c> tooling discovers this factory when adding or updating migrations without a running
/// host.  It resolves the connection string from <c>PAPERTRADING_CONNECTION_STRING</c> (the shared env-var
/// for the single shared Postgres instance) first (for CI), then <c>appsettings.Development.json</c> /
/// <c>appsettings.json</c> under the startup project (<c>src/IctTrader.Host</c>).  NO hard-coded credential
/// fallback — if neither is configured it throws, so no database credential is ever committed (plan §6.3 /
/// §14).
/// </para>
/// <para>
/// Usage: <c>dotnet ef migrations add AddCandles --project src/Modules/MarketData/Infrastructure
/// --startup-project src/IctTrader.Host</c>
/// </para>
/// </summary>
public sealed class MarketDataDbContextFactory : IDesignTimeDbContextFactory<MarketDataDbContext>
{
    // Reuse the same env-var as PaperTrading — one shared Postgres, two separate contexts.
    private const string ConnectionStringEnvVar = "PAPERTRADING_CONNECTION_STRING";

    private const string AppSettingsKey = "ConnectionStrings:PaperTrading";

    public MarketDataDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            NullIfBlank(Environment.GetEnvironmentVariable(ConnectionStringEnvVar))
            ?? NullIfBlank(ReadFromAppSettings())
            ?? throw new InvalidOperationException(
                $"No MarketData (candle) connection string is configured. Set the '{ConnectionStringEnvVar}' " +
                $"environment variable or '{AppSettingsKey}' in src/IctTrader.Host/appsettings.Development.json " +
                "before running design-time EF tooling. Never commit real database credentials.");

        var optionsBuilder = new DbContextOptionsBuilder<MarketDataDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(MarketDataDbContextFactory).Assembly.FullName));

        return new MarketDataDbContext(optionsBuilder.Options);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? ReadFromAppSettings()
    {
        // Walk up from the assembly directory to find the Host appsettings (design-time layout).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "appsettings.Development.json");
            if (File.Exists(candidate))
            {
                var config = new ConfigurationBuilder()
                    .AddJsonFile(candidate, optional: true)
                    .Build();
                var value = config[AppSettingsKey];
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            candidate = Path.Combine(dir.FullName, "appsettings.json");
            if (File.Exists(candidate))
            {
                var config = new ConfigurationBuilder()
                    .AddJsonFile(candidate, optional: true)
                    .Build();
                var value = config[AppSettingsKey];
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
