namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// Configuration for the <see cref="OandaMarketDataFeed"/> (plan §6 — the OANDA-practice market-data reader),
/// bound from <c>Ict:MarketData:Oanda</c> — no magic numbers. This is a <b>read-only</b> market-data feed:
/// the options carry NO order/account/trade settings because the feed has no order path at all (the guardrail
/// is structural). <see cref="BaseUrl"/> defaults to the OANDA <b>PRACTICE</b> host, which is broker-accurate
/// AND structurally non-live — the practice environment cannot touch real capital — and <see cref="Token"/> is
/// supplied via config/environment, never committed.
/// </summary>
public sealed class OandaFeedOptions
{
    public const string SectionName = "Ict:MarketData:Oanda";

    /// <summary>The fewest backfill candles an operator may request.</summary>
    private const int MinHistoryCount = 1;

    /// <summary>The OANDA v20 ceiling on candles returned by a single request.</summary>
    private const int MaxHistoryCount = 5000;

    /// <summary>The fastest live-poll cadence (one second) — the feed polls for newly-completed candles, not ticks.</summary>
    private const int MinPollSeconds = 1;

    /// <summary>The fewest backfill attempts (1 = the historical no-retry behaviour).</summary>
    private const int MinBackfillMaxAttempts = 1;

    /// <summary>A sane ceiling on backfill attempts so a config typo can't wedge startup in a long retry storm.</summary>
    private const int MaxBackfillMaxAttempts = 10;

    /// <summary>The fewest seconds between backfill retries (0 = retry immediately — used by the unit tests).</summary>
    private const int MinBackfillRetryDelaySeconds = 0;

    /// <summary>A ceiling on the inter-retry delay so a typo can't stall the first candle for minutes.</summary>
    private const int MaxBackfillRetryDelaySeconds = 60;

    /// <summary>The fewest concurrent per-(instrument,granularity) fetches a poll/backfill may run (1 = serial).</summary>
    private const int MinMaxConcurrentFetchesPerPoll = 1;

    /// <summary>
    /// A ceiling on the per-poll fetch concurrency so a config typo can't open an unbounded burst of GETs at the
    /// OANDA host (each instrument×granularity is one GET). 24 is generous — e.g. 6 instruments × 4 granularities.
    /// </summary>
    private const int MaxMaxConcurrentFetchesPerPoll = 24;

    /// <summary>The default per-poll fetch concurrency — enough to fan a handful of instruments × granularities out at once.</summary>
    private const int DefaultMaxConcurrentFetchesPerPoll = 6;

    /// <summary>The fewest candles a history fetch (<see cref="FetchHistory"/>) may request.</summary>
    private const int MinHistoryMaxCandles = 1;

    /// <summary>
    /// The most candles a one-shot history fetch (<see cref="FetchHistory"/>) may request per instrument. The
    /// backward-paginating fetcher accumulates every page in memory before writing the CSV, so an unbounded
    /// budget (e.g. a config typo of extra zeroes) could exhaust memory. One million candles is ~9.5 years of M5
    /// bars per instrument — far beyond any realistic backtest window — yet still a hard, defensible ceiling.
    /// </summary>
    private const int MaxHistoryMaxCandles = 1_000_000;

    /// <summary>The default local directory the one-shot history fetch writes its backtest CSVs into.</summary>
    private const string DefaultHistoryOutputDirectory = "data";

    /// <summary>The default candle budget for a one-shot history fetch (paginated backward, OANDA caps a page at 5000).</summary>
    private const int DefaultHistoryMaxCandles = 5000;

    /// <summary>
    /// The OANDA granularities that map to a scanner <c>Timeframe</c> enum member (so a fetched candle's timeframe
    /// string parses downstream). The intraday set maps 1:1; OANDA's daily <c>D</c> maps to <c>D1</c> (and weekly
    /// <c>W</c> to <c>W1</c>) via <see cref="OandaCandleParser"/>'s granularity normalisation. OANDA's
    /// <c>M2/M4/M10/MN</c> are excluded — no scanner timeframe — so a typo fails fast at startup, not at the first fetch.
    /// </summary>
    private static readonly string[] SupportedGranularities = ["M1", "M5", "M15", "M30", "H1", "H4", "D", "W"];

    /// <summary>
    /// The OANDA REST base URL. Defaults to the <b>practice</b> (fxPractice) host: broker-accurate and
    /// structurally non-live (the practice environment has no real capital). The live host is intentionally
    /// NOT the default and the system never routes an order regardless.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api-fxpractice.oanda.com";

    /// <summary>
    /// The OANDA practice API token, supplied via configuration/environment (never committed). Required — a
    /// blank token fails <see cref="Validate"/> so a mis-configured host fails fast at startup.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// The instruments to read, in OANDA's underscore form (e.g. <c>EUR_USD</c>). Defaults to EMPTY so the .NET
    /// config binder REPLACES rather than APPENDS to a pre-populated initializer (see MarketContextOptions.cs for the
    /// documented rationale) — a non-empty default would silently still stream <c>EUR_USD</c> even when the operator
    /// selects only <c>GBP_USD</c>, double-feeding a per-symbol scanner. Consume <see cref="ResolvedInstruments"/>.
    /// </summary>
    public IReadOnlyList<string> Instruments { get; init; } = [];

    /// <summary>The OANDA default when no instrument is configured (the §2.5 reference major).</summary>
    private static readonly IReadOnlyList<string> DefaultInstruments = ["EUR_USD"];

    /// <summary>
    /// The instruments actually read — the configured set de-duplicated, or the <c>EUR_USD</c> default when none is
    /// configured. Consume this, never the raw <see cref="Instruments"/>.
    /// </summary>
    public IReadOnlyList<string> ResolvedInstruments =>
        Instruments.Count == 0 ? DefaultInstruments : Instruments.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The single OANDA granularity (candle timeframe) for the one-shot <see cref="FetchHistory"/> CSV exporter (it
    /// writes one timeframe per run, <c>&lt;symbol&gt;-&lt;granularity&gt;.csv</c>) AND the fallback for the live/backtest
    /// stream when <see cref="Granularities"/> is empty (so existing single-granularity config stays byte-identical).
    /// Defaults to <c>M5</c>.
    /// </summary>
    public string Granularity { get; init; } = "M5";

    /// <summary>
    /// The OANDA granularities the live/backtest stream reads CONCURRENTLY per instrument (e.g.
    /// <c>["M1", "M5", "M15", "H1"]</c>) — so downstream scanners get live candles on every timeframe at once. Each
    /// candle carries its own <c>Timeframe</c> (the parser maps OANDA <c>D</c>→<c>D1</c>/<c>W</c>→<c>W1</c>), so per-TF
    /// routing is already correct. Defaults to EMPTY so the .NET config binder REPLACES rather than APPENDS to a
    /// pre-populated initializer (the documented binder convention — see <see cref="Instruments"/>); when empty,
    /// <see cref="ResolvedGranularities"/> falls back to the single <see cref="Granularity"/> so a one-granularity
    /// configuration (and the <see cref="FetchHistory"/> path, which always uses the single one) stays byte-identical.
    /// Consume <see cref="ResolvedGranularities"/>, never this raw list.
    /// </summary>
    public IReadOnlyList<string> Granularities { get; init; } = [];

    /// <summary>
    /// The granularities the stream actually reads: the configured <see cref="Granularities"/> de-duplicated when
    /// non-empty, else a single-element list of <see cref="Granularity"/> (the byte-identical single-granularity
    /// fallback). Consume this, never the raw <see cref="Granularities"/>. (The one-shot <see cref="FetchHistory"/>
    /// exporter deliberately ignores this and uses the single <see cref="Granularity"/> — one timeframe per CSV run.)
    /// </summary>
    public IReadOnlyList<string> ResolvedGranularities =>
        Granularities.Count == 0 ? [Granularity] : Granularities.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// The maximum number of per-(instrument, granularity) candle GETs the stream runs concurrently in each backfill
    /// and each live poll (bounded by a <see cref="System.Threading.SemaphoreSlim"/>). Fanning out over instruments ×
    /// granularities multiplies the request count, so this caps the burst against the OANDA host while still keeping a
    /// multi-timeframe feed timely. The merged result is always re-sorted chronologically before yielding, so the
    /// concurrency never affects ordering. Defaults to 6.
    /// </summary>
    public int MaxConcurrentFetchesPerPoll { get; init; } = DefaultMaxConcurrentFetchesPerPoll;

    /// <summary>The number of completed candles to backfill per instrument (OANDA caps a request at 5000).</summary>
    public int HistoryCount { get; init; } = 500;

    /// <summary>The live-poll cadence in seconds — how often the feed checks for a newly-completed candle.</summary>
    public int PollSeconds { get; init; } = 60;

    /// <summary>
    /// When <c>false</c> (default), the feed is historical-only — a finite backtest stream that ends after the
    /// backfill. When <c>true</c>, after the backfill it polls (<see cref="PollSeconds"/>) for newly-completed
    /// candles until cancelled.
    /// </summary>
    public bool LiveStreaming { get; init; }

    /// <summary>
    /// The total number of attempts (including the first) the <b>backfill</b> makes for each instrument's candle
    /// fetch before giving up, retrying only <i>transient</i> failures — a refused/reset connection, a request
    /// timeout, HTTP 408/429, or a 5xx. Auth/4xx errors (a bad token) are NOT transient and fail fast on the first
    /// attempt regardless. Defaults to 5: with intermittently-refusing egress, a single transient blip during
    /// startup backfill would otherwise leave the live feed permanently dead (the live <i>poll</i> already
    /// self-heals; the backfill did not). Backfilling many instruments multiplies the blip risk, so the retry is
    /// what makes a multi-instrument live feed reliable.
    /// </summary>
    public int BackfillMaxAttempts { get; init; } = 5;

    /// <summary>The seconds to wait between transient backfill retries (see <see cref="BackfillMaxAttempts"/>).</summary>
    public int BackfillRetryDelaySeconds { get; init; } = 2;

    // ---- One-shot history-fetch mode (issue #100 — the read-only backtest CSV exporter) ----

    /// <summary>
    /// When <c>true</c>, the Host runs as a one-shot, <b>read-only</b> history fetcher instead of the normal scan
    /// loop: for each configured instrument it fetches up to <see cref="HistoryMaxCandles"/> candles via
    /// <c>OandaHistoryFetcher</c>, writes a backtest CSV under <see cref="HistoryOutputDirectory"/>, then stops the
    /// app. It writes ONLY local CSV files (no order path — the guardrail is structural). Defaults to <c>false</c>.
    /// </summary>
    public bool FetchHistory { get; init; }

    /// <summary>The local directory the one-shot history fetch writes <c>&lt;symbol&gt;-&lt;granularity&gt;.csv</c> into.</summary>
    public string HistoryOutputDirectory { get; init; } = DefaultHistoryOutputDirectory;

    /// <summary>
    /// The number of completed candles to fetch per instrument in history-fetch mode. Unlike
    /// <see cref="HistoryCount"/> (a single 5000-capped request) this paginates backward, so it may exceed 5000.
    /// </summary>
    public int HistoryMaxCandles { get; init; } = DefaultHistoryMaxCandles;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            errors.Add("BaseUrl is required and must be non-blank.");
        }

        if (string.IsNullOrWhiteSpace(Token))
        {
            errors.Add("Token is required and must be non-blank (supply it via config/environment).");
        }

        // An empty configured list is VALID — it means "use the EUR_USD default" (applied by ResolvedInstruments).
        // A configured list with a blank entry is still a fail-fast misconfig.
        if (Instruments.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("Instruments must not contain a blank entry.");
        }

        if (string.IsNullOrWhiteSpace(Granularity) || !SupportedGranularities.Contains(Granularity, StringComparer.Ordinal))
        {
            errors.Add(
                $"Granularity must be one of [{string.Join(", ", SupportedGranularities)}] (OANDA granularities that " +
                $"map to a scanner timeframe) but was '{Granularity}'.");
        }

        // Validate the RESOLVED granularity set (the configured Granularities, or the single Granularity fallback) —
        // every entry must map to a scanner timeframe, so a typo in the multi-granularity list fails fast at startup
        // rather than at the first fetch. An empty configured list is VALID — it falls back to Granularity above.
        foreach (var granularity in ResolvedGranularities)
        {
            if (string.IsNullOrWhiteSpace(granularity) || !SupportedGranularities.Contains(granularity, StringComparer.Ordinal))
            {
                errors.Add(
                    $"Granularities entry '{granularity}' must be one of [{string.Join(", ", SupportedGranularities)}] " +
                    "(OANDA granularities that map to a scanner timeframe).");
            }
        }

        if (MaxConcurrentFetchesPerPoll is < MinMaxConcurrentFetchesPerPoll or > MaxMaxConcurrentFetchesPerPoll)
        {
            errors.Add(
                $"MaxConcurrentFetchesPerPoll must be within [{MinMaxConcurrentFetchesPerPoll}, " +
                $"{MaxMaxConcurrentFetchesPerPoll}] but was {MaxConcurrentFetchesPerPoll}.");
        }

        if (HistoryCount is < MinHistoryCount or > MaxHistoryCount)
        {
            errors.Add($"HistoryCount must be within [{MinHistoryCount}, {MaxHistoryCount}] but was {HistoryCount}.");
        }

        if (PollSeconds < MinPollSeconds)
        {
            errors.Add($"PollSeconds must be at least {MinPollSeconds} but was {PollSeconds}.");
        }

        if (BackfillMaxAttempts is < MinBackfillMaxAttempts or > MaxBackfillMaxAttempts)
        {
            errors.Add(
                $"BackfillMaxAttempts must be within [{MinBackfillMaxAttempts}, {MaxBackfillMaxAttempts}] but was " +
                $"{BackfillMaxAttempts}.");
        }

        if (BackfillRetryDelaySeconds is < MinBackfillRetryDelaySeconds or > MaxBackfillRetryDelaySeconds)
        {
            errors.Add(
                $"BackfillRetryDelaySeconds must be within [{MinBackfillRetryDelaySeconds}, " +
                $"{MaxBackfillRetryDelaySeconds}] but was {BackfillRetryDelaySeconds}.");
        }

        // The history-fetch knobs only constrain the run when fetch mode is on (so a normal scan-loop host need not
        // set them), but a blank output dir or non-positive budget WITH FetchHistory=true is a fail-fast misconfig.
        if (FetchHistory)
        {
            if (string.IsNullOrWhiteSpace(HistoryOutputDirectory))
            {
                errors.Add("HistoryOutputDirectory is required and must be non-blank when FetchHistory is true.");
            }

            if (HistoryMaxCandles is < MinHistoryMaxCandles or > MaxHistoryMaxCandles)
            {
                errors.Add(
                    $"HistoryMaxCandles must be within [{MinHistoryMaxCandles}, {MaxHistoryMaxCandles}] but was " +
                    $"{HistoryMaxCandles}.");
            }
        }

        return errors;
    }
}
