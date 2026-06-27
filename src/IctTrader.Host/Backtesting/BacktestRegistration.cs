using IctTrader.Host.Backtesting;

namespace IctTrader.Host;

/// <summary>
/// Registers the on-demand backtest engine (plan §15) in the composition root. The engine is a stateless singleton —
/// each run builds its own fresh scanner, orchestrator, and throwaway in-memory account — so it is safe to share and
/// to call concurrently (the optimizer fans out runs). It depends only on the already-registered scan-loop services
/// (the scanner + orchestrator factories, the instrument registry, the risk options), so it is wired AFTER
/// <c>AddScanLoop</c>. Pure analysis surface — it reads CSVs and routes nothing near a broker (§6.3 guardrail).
/// </summary>
public static class BacktestRegistration
{
    public static IServiceCollection AddBacktesting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<BacktestOptions>().Bind(configuration.GetSection(BacktestOptions.SectionName));
        services.AddSingleton<BacktestEngine>();
        return services;
    }
}
