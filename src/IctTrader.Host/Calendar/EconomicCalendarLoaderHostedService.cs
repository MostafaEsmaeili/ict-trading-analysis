using IctTrader.Domain.Configuration;
using IctTrader.Domain.Sessions;
using Microsoft.Extensions.Options;

namespace IctTrader.Host.Calendar;

/// <summary>
/// Loads the economic calendar into the shared <see cref="IEconomicCalendarStore"/> at startup and on a refresh
/// interval, from the configured <see cref="IEconomicCalendarSource"/> (plan §2.5.8/§15). Each refresh fetches the
/// NY-date window around "today" (lookback covers a just-passed FOMC's post-day blackout; lookahead covers upcoming
/// NFP weeks) and replaces the store's set — bumping its revision so every per-symbol scanner re-loads it into its
/// MarketContext and the §2.5.2 gate starts withholding <c>CalendarClear</c> on blacked-out days.
///
/// <para>Resilient: a fetch failure is logged and retried next interval — the store keeps its last good set (or stays
/// unloaded, so the gate stays fail-open). The loader only READS events; there is no order path (§6.3 guardrail).</para>
/// </summary>
internal sealed class EconomicCalendarLoaderHostedService(
    IEconomicCalendarSource source,
    IEconomicCalendarStore store,
    TimeProvider timeProvider,
    IOptions<CalendarFeedOptions> options,
    ILogger<EconomicCalendarLoaderHostedService> logger) : BackgroundService
{
    private readonly CalendarFeedOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nyClock = new NyClock(timeProvider);
        var period = TimeSpan.FromHours(_options.RefreshHours);
        using var timer = new PeriodicTimer(period);

        logger.LogInformation(
            "Economic-calendar loader starting on provider {Provider} (refresh every {Hours}h).",
            source.Provider,
            _options.RefreshHours);

        do
        {
            await RefreshAsync(nyClock, stoppingToken).ConfigureAwait(false);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    private async Task RefreshAsync(NyClock nyClock, CancellationToken ct)
    {
        var today = nyClock.NewYorkDate(nyClock.UtcNow);
        var from = today.AddDays(-_options.LookbackDays);
        var to = today.AddDays(_options.LookaheadDays);

        try
        {
            var events = await source.FetchAsync(from, to, ct).ConfigureAwait(false);
            store.Load(events);
            logger.LogInformation(
                "Economic calendar loaded: {Count} event(s) for {From}..{To} from {Provider}.",
                events.Count,
                from,
                to,
                source.Provider);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutdown — propagate
        }
        catch (Exception ex)
        {
            // Keep the last good set (or stay unloaded → fail-open). Surface the failure; retry next tick.
            logger.LogError(
                ex,
                "Economic-calendar refresh from {Provider} failed; keeping the previous calendar.",
                source.Provider);
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
