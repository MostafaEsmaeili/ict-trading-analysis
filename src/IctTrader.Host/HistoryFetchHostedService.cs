using System.Globalization;
using IctTrader.MarketData.Infrastructure.Feeds;

namespace IctTrader.Host;

/// <summary>
/// A one-shot, <b>read-only</b> history-export tool (issue #100). When <c>Ict:MarketData:Oanda:FetchHistory</c> is
/// true it runs INSTEAD of the normal scan-loop ingestion: for each configured OANDA instrument it fetches up to
/// <see cref="OandaFeedOptions.HistoryMaxCandles"/> completed candles via <see cref="OandaHistoryFetcher"/> (which
/// paginates the OANDA practice candles endpoint backward), writes them to
/// <c>&lt;HistoryOutputDirectory&gt;/&lt;symbol&gt;-&lt;granularity&gt;.csv</c> in the <see cref="CsvCandleSource"/>
/// replay format via <see cref="CandleCsvWriter"/>, logs the count + path, and then requests app shutdown — so the
/// process exits after the fetch.
/// <para>
/// <b>Read-only is structural (the NON-NEGOTIABLE guardrail):</b> the fetcher issues only HTTP <c>GET</c>s and this
/// service writes ONLY a local CSV file. There is no order/trade/broker path anywhere; the default base URL is the
/// OANDA <i>practice</i> host. It is registered ONLY when <c>FetchHistory</c> is true, and in that mode the normal
/// <see cref="MarketDataIngestionHostedService"/> is NOT registered, so no scan-loop ingestion runs.
/// </para>
/// </summary>
internal sealed class HistoryFetchHostedService(
    OandaHistoryFetcher fetcher,
    OandaFeedOptions options,
    IHostApplicationLifetime lifetime,
    ILogger<HistoryFetchHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            logger.LogInformation(
                "OANDA history fetch starting: {InstrumentCount} instrument(s) at {Granularity}, up to {MaxCandles} candles each, into '{OutputDirectory}'.",
                options.ResolvedInstruments.Count, options.Granularity, options.HistoryMaxCandles, options.HistoryOutputDirectory);

            foreach (var instrument in options.ResolvedInstruments)
            {
                stoppingToken.ThrowIfCancellationRequested();
                await FetchAndWriteAsync(instrument, stoppingToken).ConfigureAwait(false);
            }

            logger.LogInformation("OANDA history fetch complete; requesting shutdown.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // A normal shutdown during the fetch — not an error.
            logger.LogInformation("OANDA history fetch cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            // Top-level boundary catch: a fetch failure (bad token, unreachable practice host, a bad output path)
            // must surface clearly and still stop the one-shot tool rather than hang the process.
            logger.LogError(ex, "OANDA history fetch failed.");
        }
        finally
        {
            // One-shot tool: whatever happened, exit the process so it doesn't sit idle as a server.
            lifetime.StopApplication();
        }
    }

    private async Task FetchAndWriteAsync(string instrument, CancellationToken ct)
    {
        var candles = await fetcher
            .FetchAsync(instrument, options.Granularity, options.HistoryMaxCandles, ct)
            .ConfigureAwait(false);

        if (candles.Count == 0)
        {
            logger.LogWarning("OANDA history fetch for {Instrument} returned no candles; skipping CSV write.", instrument);
            return;
        }

        // The fetcher normalises the symbol to the dashboard form (EUR_USD → EURUSD) on the CandleDto, so the file
        // name uses that symbol + the granularity — exactly what the Replay feed's CsvCandleSource reads back.
        var symbol = candles[0].Symbol;
        var fileName = string.Format(
            CultureInfo.InvariantCulture, "{0}-{1}.csv", symbol, options.Granularity);
        var path = Path.Combine(options.HistoryOutputDirectory, fileName);

        await CandleCsvWriter.WriteAsync(candles, path, ct).ConfigureAwait(false);

        logger.LogInformation(
            "OANDA history fetch wrote {Count} candles for {Instrument} ({Symbol} {Granularity}) to '{Path}'.",
            candles.Count, instrument, symbol, options.Granularity, Path.GetFullPath(path));
    }
}
