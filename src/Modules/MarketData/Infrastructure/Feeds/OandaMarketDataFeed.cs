using System.Globalization;
using System.Runtime.CompilerServices;
using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// A <b>read-only</b> OANDA-practice market-data feed (plan §6). It reads completed candles from the OANDA v20
/// REST API — first a historical backfill, then (when <see cref="OandaFeedOptions.LiveStreaming"/>) a poll for
/// newly-completed candles — and streams them as <see cref="CandleDto"/>s in chronological order. It fans out
/// over every configured instrument × granularity (<see cref="OandaFeedOptions.ResolvedInstruments"/> ×
/// <see cref="OandaFeedOptions.ResolvedGranularities"/>), so downstream scanners receive live candles on every
/// timeframe at once — each <see cref="CandleDto"/> carries its own <c>Timeframe</c>, so per-TF routing is correct.
/// <para>
/// <b>Read-only is structural, not a flag (the NON-NEGOTIABLE guardrail):</b> this feed implements ONLY
/// <see cref="IMarketDataFeed"/> (which has no write/order method) and issues ONLY HTTP <c>GET</c>s against the
/// candles endpoint. There is no order/trade/buy/sell path anywhere — it is impossible to route an order through
/// this feed. The default base URL is the OANDA <i>practice</i> host, which has no real capital. The API token
/// is injected via configuration (never committed) and carried on the <c>Authorization: Bearer</c> header of the
/// supplied <see cref="HttpClient"/>.
/// </para>
/// </summary>
public sealed class OandaMarketDataFeed : IMarketDataFeed
{
    /// <summary>The provider name used for status and feed selection.</summary>
    public const string ProviderName = "Oanda";

    /// <summary>The feed's read-only status (always true — it is structurally a reader, plan §6.3).</summary>
    public const bool IsReadOnly = true;

    // The OANDA v20 candles endpoint + query — an external API contract, so these literals are unavoidable.
    private const string CandlesPathFormat = "/v3/instruments/{0}/candles";
    private const string MidPricingComponent = "M";   // ?price=M → mid-price candles (no bid/ask routing surface)

    // The first live poll (before any watermark exists) fetches this small tail; every later poll fetches by
    // `from=<watermark>` so it is bounded by elapsed time, never a fixed count — the stream stays gap-free even
    // if a poll falls behind. (PollSeconds should still be <= the granularity period for timely delivery.)
    private const int FirstPollCandleCount = 2;

    private readonly HttpClient _httpClient;
    private readonly OandaFeedOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OandaMarketDataFeed> _logger;

    public OandaMarketDataFeed(
        HttpClient httpClient,
        OandaFeedOptions options,
        TimeProvider timeProvider,
        ILogger<OandaMarketDataFeed>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<OandaMarketDataFeed>.Instance;
    }

    public string Provider => ProviderName;

    /// <summary>
    /// Streams completed candles in chronological order: a one-shot historical backfill across every configured
    /// instrument × granularity (merged by open time), then — only when <see cref="OandaFeedOptions.LiveStreaming"/>
    /// is true — an indefinite poll that yields each newly-completed candle once. With live streaming off this is a
    /// finite historical stream (the backtest path) that completes after the backfill. The downstream ingestor
    /// publishes these candles sequentially in this chronological order — the concurrency below is only in the HTTP
    /// fetch, never in the yield order.
    /// </summary>
    public async IAsyncEnumerable<CandleDto> StreamCandlesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The most-recent open time already yielded per (instrument, granularity) — the live-poll dedupe watermark.
        // Keyed by the composite series (e.g. EUR_USD/M5), the same key every fetch loop iterates, so neither a
        // multi-instrument NOR a multi-granularity run can cross-contaminate watermarks (M1 and M5 of the same pair
        // advance independently).
        var lastYieldedBySeries = new Dictionary<FeedSeries, DateTimeOffset>();

        var backfill = await FetchBackfillAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (series, candle) in backfill)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Watermark(lastYieldedBySeries, series, candle);
            yield return candle;
        }

        if (!_options.LiveStreaming)
        {
            yield break;   // historical-only: a finite, reproducible backtest stream
        }

        var pollDelay = TimeSpan.FromSeconds(_options.PollSeconds);
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(pollDelay, _timeProvider, cancellationToken).ConfigureAwait(false);

            IReadOnlyList<(FeedSeries Series, CandleDto Candle)> fresh;
            try
            {
                fresh = await FetchLiveTailAsync(lastYieldedBySeries, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                ex is HttpRequestException
                    or System.Text.Json.JsonException
                    or FormatException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A transient feed error must NOT tear down a long-running live stream — log it and retry on the
                // next poll tick. The set is: an HTTP blip (429/5xx/network); an HttpClient request TIMEOUT
                // (TaskCanceledException, an OperationCanceledException whose token is NOT our cancellationToken —
                // hence the !IsCancellationRequested guard); a truncated/partial body (JsonException); or one
                // malformed candle (FormatException). A genuine cooperative shutdown
                // (cancellationToken.IsCancellationRequested) is EXCLUDED above and propagates, so a real stop
                // still tears the loop down. (The backfill path has its own bounded transient retry; see
                // FetchBackfillCandlesAsync — a backtest still fails fast after the retries are exhausted.)
                _logger.LogWarning(ex, "OANDA live poll failed transiently; retrying after {PollSeconds}s.", _options.PollSeconds);
                continue;
            }

            foreach (var (series, candle) in fresh)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Watermark(lastYieldedBySeries, series, candle);
                yield return candle;
            }
        }
    }

    /// <summary>
    /// Backfills <see cref="OandaFeedOptions.HistoryCount"/> candles per (instrument, granularity), fanned out
    /// concurrently (bounded by <see cref="OandaFeedOptions.MaxConcurrentFetchesPerPoll"/>) and merged
    /// chronologically. Each series' fetch retries <i>transient</i> failures
    /// (<see cref="OandaFeedOptions.BackfillMaxAttempts"/>) so a flaky-egress blip during startup cannot leave the
    /// feed permanently dead. When live-streaming, a series that is still unreachable after its retries is logged and
    /// SKIPPED (the remaining series stream live — one bad instrument/timeframe must not blank the whole chart); a
    /// finite BACKTEST keeps fail-fast so a data gap can never silently corrupt a reproducible run.
    /// </summary>
    private async Task<IReadOnlyList<(FeedSeries Series, CandleDto Candle)>> FetchBackfillAsync(
        CancellationToken cancellationToken)
    {
        var perSeries = await FanOutAsync(
            series => FetchBackfillCandlesAsync(series, cancellationToken),
            isBackfill: true,
            cancellationToken).ConfigureAwait(false);

        return SortChronologically(Flatten(perSeries));
    }

    /// <summary>
    /// Fetches one series' backfill, retrying ONLY transient failures up to
    /// <see cref="OandaFeedOptions.BackfillMaxAttempts"/> with a fixed <see cref="OandaFeedOptions.BackfillRetryDelaySeconds"/>
    /// pause (driven by the injected <see cref="TimeProvider"/> so tests stay clock-free). A non-transient error
    /// (auth/4xx) throws on the first attempt — a bad token fails fast, not after N retries.
    /// </summary>
    private async Task<CandleDto[]> FetchBackfillCandlesAsync(FeedSeries series, CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(_options.BackfillRetryDelaySeconds);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await FetchCandlesAsync(
                        BuildCandlesUriCount(series, _options.HistoryCount), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt < _options.BackfillMaxAttempts && IsTransientFetchFailure(ex, cancellationToken))
            {
                _logger.LogWarning(
                    ex,
                    "OANDA backfill for {Instrument}/{Granularity} failed transiently on attempt {Attempt}/{MaxAttempts}; " +
                    "retrying in {DelaySeconds}s.",
                    series.Instrument,
                    series.Granularity,
                    attempt,
                    _options.BackfillMaxAttempts,
                    _options.BackfillRetryDelaySeconds);
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// True for a <i>transient</i> fetch failure that is worth retrying: a transport-level error (connection
    /// refused/reset, DNS) which surfaces as an <see cref="HttpRequestException"/> with no <c>StatusCode</c>; an
    /// HTTP 408/429/5xx; or an <see cref="HttpClient"/> request timeout (an <see cref="OperationCanceledException"/>
    /// whose token is NOT our <paramref name="cancellationToken"/>). A genuine cooperative cancellation and any
    /// auth/4xx (a bad token, an unknown instrument) are NOT transient — the caller fails fast.
    /// </summary>
    private static bool IsTransientFetchFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (ex is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        if (ex is HttpRequestException http)
        {
            if (http.StatusCode is null)
            {
                return true;   // a transport/connection error (refused, reset) carries no HTTP status
            }

            var status = (int)http.StatusCode.Value;
            return status is 408 or 429 || status >= 500;
        }

        return false;
    }

    /// <summary>
    /// Polls a small tail per (instrument, granularity), fanned out concurrently, and keeps only candles strictly
    /// after the per-series watermark. A live poll never fails fast — its caller swallows transient errors — so the
    /// fan-out here lets a transient error on one series surface to that caller (which skips the whole tick) without
    /// the per-series skip the backfill uses.
    /// </summary>
    private async Task<IReadOnlyList<(FeedSeries Series, CandleDto Candle)>> FetchLiveTailAsync(
        IReadOnlyDictionary<FeedSeries, DateTimeOffset> lastYieldedBySeries,
        CancellationToken cancellationToken)
    {
        var perSeries = await FanOutAsync(
            series => FetchLiveTailForSeriesAsync(series, lastYieldedBySeries, cancellationToken),
            isBackfill: false,
            cancellationToken).ConfigureAwait(false);

        return SortChronologically(Flatten(perSeries));
    }

    /// <summary>Polls one series' tail and returns only the candles strictly after its watermark.</summary>
    private async Task<CandleDto[]> FetchLiveTailForSeriesAsync(
        FeedSeries series,
        IReadOnlyDictionary<FeedSeries, DateTimeOffset> lastYieldedBySeries,
        CancellationToken cancellationToken)
    {
        var hasWatermark = lastYieldedBySeries.TryGetValue(series, out var lastOpen);

        // Bound the tail by elapsed time (`from=<watermark>`), not a fixed count, so a slow poll never skips candles
        // that completed and scrolled past — gap-free. `from` is inclusive, so the watermark candle returns and is
        // dropped by the strictly-after filter. The very first poll (no watermark yet) takes a small count tail.
        var candles = hasWatermark
            ? await FetchCandlesAsync(BuildCandlesUriFrom(series, lastOpen), cancellationToken).ConfigureAwait(false)
            : await FetchCandlesAsync(BuildCandlesUriCount(series, FirstPollCandleCount), cancellationToken).ConfigureAwait(false);

        if (!hasWatermark)
        {
            return candles;
        }

        return [.. candles.Where(candle => candle.OpenTimeUtc > lastOpen)];
    }

    /// <summary>
    /// Runs the supplied per-series fetch over every instrument × granularity CONCURRENTLY, bounded by a
    /// <see cref="SemaphoreSlim"/>(<see cref="OandaFeedOptions.MaxConcurrentFetchesPerPoll"/>) so the burst against
    /// the OANDA host stays capped however many series there are. The merged candles are re-sorted chronologically by
    /// the caller, so the parallelism never affects yield order. When <paramref name="isBackfill"/> AND live-streaming,
    /// a series unreachable after its transient retries is logged + SKIPPED (returns no candles); otherwise — a finite
    /// backtest backfill, or any live poll — a transient failure propagates (the backtest fails fast; the live poll's
    /// caller swallows it and retries the whole tick next poll).
    /// </summary>
    private async Task<IReadOnlyList<(FeedSeries Series, CandleDto[] Candles)>> FanOutAsync(
        Func<FeedSeries, Task<CandleDto[]>> fetch,
        bool isBackfill,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(_options.MaxConcurrentFetchesPerPoll);

        var tasks = new List<Task<(FeedSeries Series, CandleDto[] Candles)>>();
        foreach (var instrument in _options.ResolvedInstruments)
        {
            foreach (var granularity in _options.ResolvedGranularities)
            {
                var series = new FeedSeries(instrument, granularity);
                tasks.Add(FetchOneSeriesAsync(series, fetch, gate, isBackfill, cancellationToken));
            }
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task<(FeedSeries Series, CandleDto[] Candles)> FetchOneSeriesAsync(
        FeedSeries series,
        Func<FeedSeries, Task<CandleDto[]>> fetch,
        SemaphoreSlim gate,
        bool isBackfill,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (series, await fetch(series).ConfigureAwait(false));
        }
        catch (Exception ex) when (isBackfill && _options.LiveStreaming && IsTransientFetchFailure(ex, cancellationToken))
        {
            // A LIVE backfill must stream the healthy series rather than die because one is unreachable after its
            // retries. A finite backtest does NOT reach here (isBackfill && !LiveStreaming) so it fails fast.
            _logger.LogWarning(
                ex,
                "OANDA backfill skipped {Instrument}/{Granularity} after {MaxAttempts} transient failures; the " +
                "remaining series stream live and the poll will retry it.",
                series.Instrument,
                series.Granularity,
                _options.BackfillMaxAttempts);
            return (series, []);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CandleDto[]> FetchCandlesAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return OandaCandleParser.Parse(json);
    }

    /// <summary>Builds the read-only candles GET for the latest <paramref name="count"/> candles (granularity + mid).</summary>
    private Uri BuildCandlesUriCount(FeedSeries series, int count)
        => BuildCandlesUri(series, $"&count={count.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Builds the read-only candles GET for all candles since <paramref name="fromUtc"/> (inclusive).</summary>
    private Uri BuildCandlesUriFrom(FeedSeries series, DateTimeOffset fromUtc)
    {
        var from = fromUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        return BuildCandlesUri(series, $"&from={Uri.EscapeDataString(from)}");
    }

    private static Uri BuildCandlesUri(FeedSeries series, string selector)
    {
        var path = string.Format(CultureInfo.InvariantCulture, CandlesPathFormat, Uri.EscapeDataString(series.Instrument));
        var query = $"?granularity={Uri.EscapeDataString(series.Granularity)}{selector}&price={MidPricingComponent}";
        return new Uri(path + query, UriKind.Relative);
    }

    private static void Watermark(
        IDictionary<FeedSeries, DateTimeOffset> watermarks,
        FeedSeries series,
        CandleDto candle)
    {
        if (!watermarks.TryGetValue(series, out var existing) || candle.OpenTimeUtc > existing)
        {
            watermarks[series] = candle.OpenTimeUtc;
        }
    }

    /// <summary>Flattens the per-series fetch results into (series, candle) pairs (order is re-sorted by the caller).</summary>
    private static List<(FeedSeries Series, CandleDto Candle)> Flatten(
        IReadOnlyList<(FeedSeries Series, CandleDto[] Candles)> perSeries)
    {
        var merged = new List<(FeedSeries Series, CandleDto Candle)>();
        foreach (var (series, candles) in perSeries)
        {
            foreach (var candle in candles)
            {
                merged.Add((series, candle));
            }
        }

        return merged;
    }

    /// <summary>Stable-sorts by open time so the merged multi-series stream is chronological and reproducible.</summary>
    private static IReadOnlyList<(FeedSeries Series, CandleDto Candle)> SortChronologically(
        List<(FeedSeries Series, CandleDto Candle)> candles)
        => candles.OrderBy(entry => entry.Candle.OpenTimeUtc).ToList();

    /// <summary>
    /// The composite live-poll/dedupe key — one OANDA instrument at one granularity (e.g. EUR_USD/M5). Keying the
    /// watermark by the (instrument, granularity) pair lets the same pair's timeframes advance independently, so a
    /// multi-granularity run never cross-contaminates watermarks.
    /// </summary>
    private readonly record struct FeedSeries(string Instrument, string Granularity);
}
