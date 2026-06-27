using IctTrader.Scanning.Contracts;

namespace IctTrader.E2E.Fixtures;

/// <summary>
/// Per-scenario state shared between the step-definition classes (Reqnroll resolves ONE instance per scenario via
/// its object container, so every step sees the same world). Holds the booted Host factory, the symbol under
/// analysis, and the confirmed setup so the assertion steps can read what the action steps produced — the
/// scenario's "given/when" write here and its "then" read from here.
/// </summary>
public sealed class PipelineWorld : IAsyncDisposable
{
    /// <summary>The real Host, booted over the Testcontainers Postgres for THIS scenario.</summary>
    public CustomWebApplicationFactory? Factory { get; set; }

    /// <summary>The symbol the scenario is analysing (set by the Background).</summary>
    public string? Symbol { get; set; }

    /// <summary>The advisory setup the scenario confirmed onto the bus (null until the "when" step runs).</summary>
    public SetupDto? ConfirmedSetup { get; set; }

    /// <summary>The id of the paper trade that opened from the setup, captured so a later step can reload it once
    /// it is no longer in the open set (the repository keys on the trade id, which is not the setup id).</summary>
    public Guid OpenedTradeId { get; set; }

    public CustomWebApplicationFactory RequireFactory() =>
        Factory ?? throw new InvalidOperationException("The Host has not been booted for this scenario.");

    public async ValueTask DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }
}
