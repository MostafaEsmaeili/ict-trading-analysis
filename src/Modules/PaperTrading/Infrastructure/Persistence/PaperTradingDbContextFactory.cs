using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IctTrader.PaperTrading.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="PaperTradingDbContext"/> (plan §7).
/// <para>
/// The <c>dotnet ef</c> tooling discovers this factory when adding or updating migrations without a
/// running host.  It reads the connection string from <c>appsettings.Development.json</c> under the
/// startup project (<c>src/IctTrader.Host</c>), falling back to the
/// <c>PAPERTRADING_CONNECTION_STRING</c> environment variable for CI.
/// </para>
/// <para>
/// Usage: <c>dotnet ef migrations add &lt;Name&gt; --project src/Modules/PaperTrading/Infrastructure
/// --startup-project src/IctTrader.Host</c>
/// </para>
/// </summary>
public sealed class PaperTradingDbContextFactory : IDesignTimeDbContextFactory<PaperTradingDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=ict_paper_trading_dev;Username=postgres;Password=postgres";

    private const string ConnectionStringEnvVar = "PAPERTRADING_CONNECTION_STRING";

    private const string AppSettingsKey = "ConnectionStrings:PaperTrading";

    public PaperTradingDbContext CreateDbContext(string[] args)
    {
        // Resolve the connection string: environment variable overrides appsettings; fallback to localhost.
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvVar)
            ?? ReadFromAppSettings()
            ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<PaperTradingDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(PaperTradingDbContextFactory).Assembly.FullName));

        return new PaperTradingDbContext(optionsBuilder.Options);
    }

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
                return config[AppSettingsKey];
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
