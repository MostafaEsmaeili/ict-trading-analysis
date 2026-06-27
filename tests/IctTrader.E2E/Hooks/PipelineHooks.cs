using IctTrader.E2E.Fixtures;
using Reqnroll;
using Reqnroll.BoDi;

namespace IctTrader.E2E.Hooks;

/// <summary>
/// The Reqnroll lifecycle hooks for the E2E gate (plan §8.2): the Testcontainers Postgres boots ONCE per run
/// (<see cref="BeforeTestRun"/>) and is disposed at the end (<see cref="AfterTestRun"/>); Respawn truncates the
/// schema BEFORE every scenario so each starts clean (<see cref="BeforeScenario"/>); and a fresh
/// <see cref="PipelineWorld"/> is registered into Reqnroll's per-scenario object container so the step classes
/// share it by constructor injection. The booted Host factory lives ON the world, disposed with it after the
/// scenario.
/// </summary>
[Binding]
public sealed class PipelineHooks
{
    // The container is a single shared resource for the whole test run (Reqnroll runs [BeforeTestRun] once).
    private static PostgresContainerFixture? _postgres;

    public static PostgresContainerFixture Postgres =>
        _postgres ?? throw new InvalidOperationException("The Postgres container has not been started.");

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        // The Gherkin gate is meaningful only against a real Postgres; fail fast with a clear message when Docker is
        // absent rather than timing out deep inside Testcontainers (the harness guarantees Docker for this gate).
        if (!DockerProbe.IsAvailable())
        {
            throw new InvalidOperationException(
                "Docker is not available; the WP9 E2E gate requires Testcontainers Postgres. Start Docker and re-run, " +
                "or set SKIP_DOCKER_TESTS only when you intend to bypass the container-backed suite.");
        }

        _postgres = new PostgresContainerFixture();
        await _postgres.StartAsync();
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    [BeforeScenario]
    public async Task ResetDatabaseAndRegisterWorld(IObjectContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);

        await Postgres.ResetAsync();
        container.RegisterInstanceAs(new PipelineWorld());
    }

    [AfterScenario]
    public async Task DisposeWorld(PipelineWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        await world.DisposeAsync();
    }
}
