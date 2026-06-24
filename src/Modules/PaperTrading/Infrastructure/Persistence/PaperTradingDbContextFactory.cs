using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace IctTrader.PaperTrading.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for <see cref="PaperTradingDbContext"/> (plan §7).
/// <para>
/// The <c>dotnet ef</c> tooling discovers this factory when adding or updating migrations without a
/// running host. It resolves the connection string from the <c>PAPERTRADING_CONNECTION_STRING</c>
/// environment variable first (for CI), then <c>appsettings.Development.json</c> under the startup
/// project (<c>src/IctTrader.Host</c>). It deliberately has NO hard-coded credential fallback — if
/// neither is configured it throws, so no database credential is ever committed (plan §6.3 / §14).
/// </para>
/// <para>
/// Usage: <c>dotnet ef migrations add &lt;Name&gt; --project src/Modules/PaperTrading/Infrastructure
/// --startup-project src/IctTrader.Host</c>
/// </para>
/// </summary>
public sealed class PaperTradingDbContextFactory : IDesignTimeDbContextFactory<PaperTradingDbContext>
{
    private const string ConnectionStringEnvVar = "PAPERTRADING_CONNECTION_STRING";

    private const string AppSettingsKey = "ConnectionStrings:PaperTrading";

    public PaperTradingDbContext CreateDbContext(string[] args)
    {
        // Resolve the connection string: the environment variable (CI) overrides appsettings. There is NO
        // hard-coded credential fallback — fail fast rather than bake a default into the repo, so no database
        // credential is ever committed (plan §6.3 / §14 "never commit secrets").
        var connectionString =
            NullIfBlank(Environment.GetEnvironmentVariable(ConnectionStringEnvVar))
            ?? NullIfBlank(ReadFromAppSettings())
            ?? throw new InvalidOperationException(
                $"No PaperTrading connection string is configured. Set the '{ConnectionStringEnvVar}' environment " +
                $"variable or '{AppSettingsKey}' in src/IctTrader.Host/appsettings.Development.json before running " +
                "design-time EF tooling. Never commit real database credentials.");

        var optionsBuilder = new DbContextOptionsBuilder<PaperTradingDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(PaperTradingDbContextFactory).Assembly.FullName));

        return new PaperTradingDbContext(optionsBuilder.Options);
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
                    return value;     // an empty Development value falls through to appsettings.json, not an early return
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
