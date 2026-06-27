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
/// newly-completed candles — and streams them as <see cref="CandleDto"/>s in chronological order.
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
    /// Streams completed candles in chronological order: a one-shot historical backfill across all configured
    /// instruments (merged by open time), then — only when <see cref="OandaFeedOptions.LiveStreaming"/> is true —
    /// an indefinite poll that yields each newly-completed candle once. With live streaming off this is a finite
    /// historical stream (the backtest path) that completes after the backfill.
    /// </summary>
    public async IAsyncEnumerable<CandleDto> StreamCandlesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // The most-recent open time already yielded per OANDA instrument — the live-poll dedupe watermark. Keyed
        // by the OANDA instrument (e.g. EUR_USD), the same key both fetch loops iterate, so a multi-instrument
        // run cannot cross-contaminate watermarks.
        var lastYieldedByInstrument = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        var backfill = await FetchBackfillAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (instrument, candle) in backfill)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Watermark(lastYieldedByInstrument, instrument, candle);
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

            IReadOnlyList<(string Instrument, CandleDto Candle)> fresh;
            try
            {
                fresh = await FetchLiveTailAsync(lastYieldedByInstrument, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                // A transient feed error (429/5xx/network blip) must NOT tear down a long-running live stream —
                // log it and retry on the next poll tick. (The backfill path above stays fail-fast.)
                _logger.LogWarning(ex, "OANDA live poll failed transiently; retrying after {PollSeconds}s.", _options.PollSeconds);
                continue;
            }

            foreach (var (instrument, candle) in fresh)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Watermark(lastYieldedByInstrument, instrument, candle);
                yield return candle;
            }
        }
    }

    /// <summary>Backfills <see cref="OandaFeedOptions.HistoryCount"/> candles per instrument, merged chronologically.</summary>
    private async Task<IReadOnlyList<(string Instrument, CandleDto Candle)>> FetchBackfillAsync(
        CancellationToken cancellationToken)
    {
        var merged = new List<(string Instrument, CandleDto Candle)>();
        foreach (var instrument in _options.ResolvedInstruments)
        {
            var candles = await FetchCandlesAsync(instrument, _options.HistoryCount, cancellationToken)
                .ConfigureAwait(false);
            foreach (var candle in candles)
            {
                merged.Add((instrument, candle));
            }
        }

        return SortChronologically(merged);
    }

    /// <summary>Polls a small tail per instrument and keeps only candles strictly after the per-instrument watermark.</summary>
    private async Task<IReadOnlyList<(string Instrument, CandleDto Candle)>> FetchLiveTailAsync(
        IReadOnlyDictionary<string, DateTimeOffset> lastYieldedByInstrument,
        CancellationToken cancellationToken)
    {
        var fresh = new List<(string Instrument, CandleDto Candle)>();
        foreach (var instrument in _options.ResolvedInstruments)
        {
            var hasWatermark = lastYieldedByInstrument.TryGetValue(instrument, out var lastOpen);

            // Bound the tail by elapsed time (`from=<watermark>`), not a fixed count, so a slow poll never skips
            // candles that completed and scrolled past — gap-free. `from` is inclusive, so the watermark candle
            // returns and is dropped by the strictly-after filter. The very first poll (no watermark yet) takes a
            // small count tail.
            var candles = hasWatermark
                ? await FetchCandlesAsync(BuildCandlesUriFrom(instrument, lastOpen), cancellationToken).ConfigureAwait(false)
                : await FetchCandlesAsync(BuildCandlesUriCount(instrument, FirstPollCandleCount), cancellationToken).ConfigureAwait(false);

            foreach (var candle in candles)
            {
                if (!hasWatermark || candle.OpenTimeUtc > lastOpen)
                {
                    fresh.Add((instrument, candle));
                }
            }
        }

        return SortChronologically(fresh);
    }

    private async Task<CandleDto[]> FetchCandlesAsync(string instrument, int count, CancellationToken cancellationToken)
        => await FetchCandlesAsync(BuildCandlesUriCount(instrument, count), cancellationToken).ConfigureAwait(false);

    private async Task<CandleDto[]> FetchCandlesAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return OandaCandleParser.Parse(json);
    }

    /// <summary>Builds the read-only candles GET for the latest <paramref name="count"/> candles (granularity + mid).</summary>
    private Uri BuildCandlesUriCount(string instrument, int count)
        => BuildCandlesUri(instrument, $"&count={count.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Builds the read-only candles GET for all candles since <paramref name="fromUtc"/> (inclusive).</summary>
    private Uri BuildCandlesUriFrom(string instrument, DateTimeOffset fromUtc)
    {
        var from = fromUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        return BuildCandlesUri(instrument, $"&from={Uri.EscapeDataString(from)}");
    }

    private Uri BuildCandlesUri(string instrument, string selector)
    {
        var path = string.Format(CultureInfo.InvariantCulture, CandlesPathFormat, Uri.EscapeDataString(instrument));
        var query = $"?granularity={Uri.EscapeDataString(_options.Granularity)}{selector}&price={MidPricingComponent}";
        return new Uri(path + query, UriKind.Relative);
    }

    private static void Watermark(
        IDictionary<string, DateTimeOffset> watermarks,
        string instrument,
        CandleDto candle)
    {
        if (!watermarks.TryGetValue(instrument, out var existing) || candle.OpenTimeUtc > existing)
        {
            watermarks[instrument] = candle.OpenTimeUtc;
        }
    }

    /// <summary>Stable-sorts by open time so the merged multi-instrument stream is chronological and reproducible.</summary>
    private static IReadOnlyList<(string Instrument, CandleDto Candle)> SortChronologically(
        List<(string Instrument, CandleDto Candle)> candles)
        => candles.OrderBy(entry => entry.Candle.OpenTimeUtc).ToList();
}
