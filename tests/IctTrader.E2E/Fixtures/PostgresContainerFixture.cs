using DotNet.Testcontainers.Builders;
using IctTrader.PaperTrading.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace IctTrader.E2E.Fixtures;

/// <summary>
/// Boots a Testcontainers Postgres instance ONCE per E2E run, applies the PaperTrading EF migrations ONCE, and
/// exposes a Respawn-based reset so each Gherkin scenario starts on a clean slate (plan §8.1/§8.2). The container
/// is a single shared resource for the whole run (Reqnroll's <c>[BeforeTestRun]</c>/<c>[AfterTestRun]</c> own its
/// lifecycle), mirroring the IntegrationTests <c>PaperTradingDbFixture</c> so the E2E and integration suites use
/// the SAME proven Testcontainers + migrations + Respawn pattern.
/// </summary>
public sealed class PostgresContainerFixture : IAsyncDisposable
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    /// <summary>The live container's connection string — handed to the real Host's <c>PaperTradingDbContext</c>.</summary>
    public string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The Postgres container has not been started.");

    public async Task StartAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("ict_e2e_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _container.StartAsync();

        // Apply the EF migrations once so the schema (and its FKs) exist before any scenario runs.
        var options = new DbContextOptionsBuilder<PaperTradingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using (var ctx = new PaperTradingDbContext(options))
        {
            await ctx.Database.MigrateAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    /// <summary>Truncates all tables so the next scenario starts clean (Respawn, between scenarios).</summary>
    public async Task ResetAsync()
    {
        if (_respawner is null)
        {
            throw new InvalidOperationException("ResetAsync called before the container was started.");
        }

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await _respawner.ResetAsync(conn);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
