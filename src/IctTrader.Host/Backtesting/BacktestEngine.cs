using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IctTrader.Domain.Configuration;
using IctTrader.Domain.Detection;
using IctTrader.Domain.Instruments;
using IctTrader.Domain.Services;
using IctTrader.Domain.Sessions;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using IctTrader.MarketData.Contracts;
using IctTrader.MarketData.Infrastructure.Feeds;
using IctTrader.PaperTrading.Application.Trading;
using IctTrader.PaperTrading.Contracts;
using IctTrader.Performance.Contracts;
using IctTrader.Scanning.Application.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IctTrader.Host.Backtesting;

/// <summary>
/// The on-demand, in-memory backtest engine (plan §15). It REUSES the exact pure §2.5 domain — the per-(symbol,style)
/// <see cref="SymbolScanner"/>, the <see cref="TradeOrchestrator"/>, and the <see cref="PaperAccount"/> — driven
/// SYNCHRONOUSLY over a recorded-history CSV: no message bus, no database, so a run is deterministic, isolated, and
/// fast (seconds). No detection / fill / cost / sizing logic is reimplemented here — the engine only composes the
/// existing components with a per-run risk policy and folds the result. The live no-look-ahead rule is preserved (a
/// setup confirmed on a bar is managed only from the NEXT bar). Advisory/paper only — it reads a CSV, mutates a
/// throwaway in-memory account, and routes nothing (§6.3 guardrail).
/// </summary>
public sealed class BacktestEngine
{
    private readonly ISymbolScannerFactory _scannerFactory;
    private readonly ITradeOrchestratorFactory _orchestratorFactory;
    private readonly IInstrumentRegistry _instruments;
    private readonly RiskOptions _defaultRisk;
    private readonly ConfluenceOptions _defaultConfluence;
    private readonly DailyRiskGuardOptions _dailyGuard;
    private readonly NyClock _nyClock = new(TimeProvider.System);
    private readonly string _dataDirectory;
    private readonly ILogger<BacktestEngine> _logger;

    /// <summary>The §2.5.5 absolute per-trade risk ceiling — a backtest may not size above it (mirrors RiskOptions).</summary>
    private const decimal AbsoluteMaxRiskPercent = 4.5m;

    public BacktestEngine(
        ISymbolScannerFactory scannerFactory,
        ITradeOrchestratorFactory orchestratorFactory,
        IInstrumentRegistry instruments,
        IOptions<RiskOptions> defaultRisk,
        IOptions<ConfluenceOptions> defaultConfluence,
        IOptions<BacktestOptions> backtestOptions,
        IOptions<DailyRiskGuardOptions> dailyGuard,
        ILogger<BacktestEngine>? logger = null)
    {
        _scannerFactory = scannerFactory ?? throw new ArgumentNullException(nameof(scannerFactory));
        _orchestratorFactory = orchestratorFactory ?? throw new ArgumentNullException(nameof(orchestratorFactory));
        _instruments = instruments ?? throw new ArgumentNullException(nameof(instruments));
        _defaultRisk = (defaultRisk ?? throw new ArgumentNullException(nameof(defaultRisk))).Value;
        _defaultConfluence = (defaultConfluence ?? throw new ArgumentNullException(nameof(defaultConfluence))).Value;
        _dailyGuard = (dailyGuard ?? throw new ArgumentNullException(nameof(dailyGuard))).Value;
        var dir = (backtestOptions ?? throw new ArgumentNullException(nameof(backtestOptions))).Value.DataDirectory;
        _dataDirectory = Path.IsPathRooted(dir) ? dir : Path.GetFullPath(dir);
        _logger = logger ?? NullLogger<BacktestEngine>.Instance;
    }

    /// <summary>Runs the backtest described by <paramref name="request"/>, loading the dataset from disk. Throws
    /// <see cref="ArgumentException"/> for an invalid request and <see cref="FileNotFoundException"/> when no dataset
    /// exists for the symbol/timeframe.</summary>
    public BacktestResponse Run(BacktestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (symbol, style, timeframe) = ParseRequest(request);
        return RunCore(request, symbol, style, timeframe, LoadCandles(symbol, timeframe));
    }

    /// <summary>Runs the backtest over a PRE-LOADED candle series — the optimizer's path, so one dataset is read from
    /// disk once and reused across the many parameter combinations that share it (the from/to window is still applied
    /// here, so the same loaded series serves any sub-period).</summary>
    public BacktestResponse Run(BacktestRequest request, IReadOnlyList<Candle> candles)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candles);
        var (symbol, style, timeframe) = ParseRequest(request);
        return RunCore(request, symbol, style, timeframe, candles);
    }

    /// <summary>Loads (and maps to the domain) the full recorded-history series for one symbol/timeframe — exposed so
    /// the optimizer can read a dataset once and reuse it across combinations. Resamples up from M1 if the exact
    /// timeframe was not fetched natively.</summary>
    public IReadOnlyList<Candle> LoadCandles(string symbol, string timeframe)
        => LoadCandles(ParseSymbol(symbol), ParseTimeframe(timeframe));

    /// <summary>The entry timeframe a request resolves to (its explicit timeframe, else the style default) — exposed
    /// so the optimizer can group combinations by the dataset they will read.</summary>
    public Timeframe ResolveTimeframe(BacktestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Timeframe is null ? DefaultTimeframeFor(ParseStyle(request.Style)) : ParseTimeframe(request.Timeframe);
    }

    private (Symbol Symbol, TradeStyle Style, Timeframe Timeframe) ParseRequest(BacktestRequest request)
    {
        var symbol = ParseSymbol(request.Symbol);
        var style = ParseStyle(request.Style);
        var timeframe = request.Timeframe is null ? DefaultTimeframeFor(style) : ParseTimeframe(request.Timeframe);
        ValidateRiskAndBalance(request);
        return (symbol, style, timeframe);
    }

    private BacktestResponse RunCore(
        BacktestRequest request, Symbol symbol, TradeStyle style, Timeframe timeframe, IReadOnlyList<Candle> allCandles)
    {
        var candles = allCandles
            .Where(c => (request.FromUtc is null || c.OpenTimeUtc >= request.FromUtc)
                && (request.ToUtc is null || c.OpenTimeUtc <= request.ToUtc))
            .ToList();

        var perRunRisk = BuildRisk(_defaultRisk, request.RiskPercent);
        var perRunConfluence = BuildConfluence(request.MinRequiredConditions, request.RequiredConditions);

        if (candles.Count == 0)
        {
            var emptyFrom = request.FromUtc ?? DateTimeOffset.UnixEpoch;
            var emptyTo = request.ToUtc ?? emptyFrom;
            return new BacktestResponse(
                symbol.Value, timeframe.ToString(), style.ToString(), emptyFrom, emptyTo,
                request.StartingBalance, request.RiskPercent, request.MinRequiredConditions, request.RequiredConditions,
                request.StartingBalance, 0, 0, 0, EmptySummary(), [], []);
        }

        var profile = _instruments.Resolve(symbol);
        var scanner = _scannerFactory.Create(symbol, timeframe, style, perRunConfluence);
        var orchestrator = _orchestratorFactory.Create(symbol, perRunRisk);
        var account = new PaperAccount(
            Guid.NewGuid(), new Money(request.StartingBalance), perRunRisk.MaxOpenPortfolioRiskPercent);

        var active = new List<ManagedPosition>();
        var closed = new List<PaperTrade>();
        var setupCount = 0;

        foreach (var candle in candles)
        {
            var barCloseUtc = candle.OpenTimeUtc + timeframe.ToTimeSpan();

            // SCAN first (mirrors the live Scanning-before-PaperTrading fan-out): a setup confirmed on THIS bar is
            // created stamped at this bar's open, then excluded from management until the NEXT bar by the eligibility
            // filter below — so there is no same-bar look-ahead (plan §4.1).
            var setup = TryScan(scanner, candle);
            if (setup is not null)
            {
                setupCount++;
                // §2.4/§2.5.5 daily risk guard input: the account's net realized P&L over trades CLOSED earlier on this
                // NY trading day (null when the guard is off → the unguarded path is byte-identical). The 00:00-NY reset
                // is implicit — only trades whose close NY-date matches this bar's NY-date are summed.
                var dayPnl = DayRealizedPnl(closed, candle.OpenTimeUtc);
                TryOpen(orchestrator, setup, account, profile, active, dayPnl);
            }

            // MANAGE every position whose open/arm bar is STRICTLY BEFORE this bar (the no-look-ahead edge).
            foreach (var position in active)
            {
                if (IsEligible(position, candle.OpenTimeUtc))
                {
                    TryAdvance(orchestrator, position, account, candle, barCloseUtc);
                }
            }

            HarvestCompleted(active, closed);
        }

        return BuildResponse(request, symbol, timeframe, style, candles, account, closed, active, setupCount);
    }

    /// <summary>Lists the recorded-history datasets available to backtest (one per <c>&lt;SYMBOL&gt;-&lt;TF&gt;.csv</c>).</summary>
    public IReadOnlyList<BacktestDatasetDto> ListDatasets()
    {
        if (!Directory.Exists(_dataDirectory))
        {
            return [];
        }

        var datasets = new List<BacktestDatasetDto>();
        foreach (var path in Directory.EnumerateFiles(_dataDirectory, "*.csv"))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            var dash = stem.LastIndexOf('-');
            if (dash <= 0)
            {
                continue;
            }

            var symbol = stem[..dash];
            var tf = stem[(dash + 1)..];
            if (!Enum.TryParse<Timeframe>(tf, out _))
            {
                continue; // not a recognised history dataset — skip
            }

            var (count, from, to) = ScanDataset(path);
            if (count > 0)
            {
                datasets.Add(new BacktestDatasetDto(symbol, tf, from, to, count));
            }
        }

        return datasets
            .OrderBy(d => d.Symbol, StringComparer.Ordinal)
            .ThenBy(d => d.FromUtc)
            .ToList();
    }

    private Setup? TryScan(SymbolScanner scanner, Candle candle)
    {
        try
        {
            return scanner.OnCandle(candle);
        }
        catch (Exception ex)
        {
            // A single malformed bar must never abort the whole run (mirrors MarketDataIngestor's per-candle isolation).
            _logger.LogWarning(ex, "Backtest scan failed on a candle at {OpenTimeUtc:o}; skipping it.", candle.OpenTimeUtc);
            return null;
        }
    }

    /// <summary>The account's net realized P&amp;L over trades CLOSED on the same NY trading day as <paramref name="nowUtc"/>
    /// — the §2.4/§2.5.5 daily risk guard input. Returns null when the guard is disabled so the unguarded path skips it
    /// entirely (and stays byte-identical).</summary>
    private Money? DayRealizedPnl(List<PaperTrade> closed, DateTimeOffset nowUtc)
    {
        if (!_dailyGuard.Enabled)
        {
            return null;
        }

        var nyDate = _nyClock.NewYorkDate(nowUtc);
        var sum = 0m;
        foreach (var trade in closed)
        {
            if (trade.ClosedAtUtc is { } closedAt && _nyClock.NewYorkDate(closedAt) == nyDate && trade.NetPnl is { } pnl)
            {
                sum += pnl.Amount;
            }
        }

        return new Money(sum);
    }

    private void TryOpen(
        TradeOrchestrator orchestrator, Setup setup, PaperAccount account, InstrumentProfile profile,
        List<ManagedPosition> active, Money? dayRealizedPnl)
    {
        try
        {
            var position = orchestrator.OnSetupConfirmed(
                setup, account, profile.SymbolSpec, profile.ContractSpec, setup.ConfirmedAtUtc,
                DeterministicSetupId(setup), dayRealizedPnl);
            active.Add(position);
        }
        catch (Exception ex)
        {
            // A refused open (portfolio cap reached, sizing guard such as min-stop / zero-lot) is a legitimate skip,
            // not a run failure — the account is left untouched (the factory's open is atomic).
            _logger.LogDebug(ex, "Backtest setup at {ConfirmedAtUtc:o} was not opened: {Reason}", setup.ConfirmedAtUtc, ex.Message);
        }
    }

    private void TryAdvance(
        TradeOrchestrator orchestrator, ManagedPosition position, PaperAccount account, Candle candle,
        DateTimeOffset barCloseUtc)
    {
        try
        {
            orchestrator.Advance(position, account, candle, barCloseUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backtest advance failed on a candle at {OpenTimeUtc:o}; skipping it.", candle.OpenTimeUtc);
        }
    }

    /// <summary>A position is managed only from the bar AFTER its open/arm bar (plan §4.1 no-look-ahead): a triggered
    /// trade keys on its <see cref="PaperTrade.ManagedFromUtc"/> (the trigger bar's open), a resting limit on its
    /// <see cref="ArmedEntry.ArmedAtUtc"/>.</summary>
    private static bool IsEligible(ManagedPosition position, DateTimeOffset candleOpenUtc) =>
        position.Trade is { } trade
            ? trade.ManagedFromUtc < candleOpenUtc
            : position.Armed is { } armed && armed.ArmedAtUtc < candleOpenUtc;

    private static void HarvestCompleted(List<ManagedPosition> active, List<PaperTrade> closed)
    {
        for (var i = active.Count - 1; i >= 0; i--)
        {
            if (!active[i].IsComplete)
            {
                continue;
            }

            if (active[i].Trade is { Status: TradeStatus.Closed } trade)
            {
                closed.Add(trade);
            }

            active.RemoveAt(i);
        }
    }

    private BacktestResponse BuildResponse(
        BacktestRequest request, Symbol symbol, Timeframe timeframe, TradeStyle style, List<Candle> candles,
        PaperAccount account, List<PaperTrade> closed, List<ManagedPosition> active, int setupCount)
    {
        // Trades = every closed trade plus any still open at the run end, newest-opened first (the live table order).
        var openAtEnd = active.Where(p => p.Trade is { Status: TradeStatus.Open }).Select(p => p.Trade!);
        var trades = closed.Concat(openAtEnd)
            .OrderByDescending(t => t.OpenedAtUtc)
            .Select(PaperTradeDtoMapper.ToDto)
            .ToList();

        // Summary + curves are over CLOSED trades only (R is defined at close). The R-based summary reuses the §5.3
        // PerformanceCalculator verbatim; the account-balance curve folds each close's NET P&L onto the start balance.
        var byClose = closed.OrderBy(t => t.ClosedAtUtc).ToList();
        var closedR = byClose.Select(t => new ClosedTradeR(t.RealizedR!.Value, t.ClosedAtUtc!.Value)).ToList();
        var summary = PerformanceCalculator.Summarize(closedR);

        var equity = new List<BacktestEquityPointDto>(byClose.Count);
        var runningBalance = request.StartingBalance;
        var runningR = 0m;
        foreach (var trade in byClose)
        {
            runningBalance += trade.NetPnl!.Value.Amount;
            runningR += trade.RealizedR!.Value;
            equity.Add(new BacktestEquityPointDto(trade.ClosedAtUtc!.Value, runningBalance, runningR));
        }

        return new BacktestResponse(
            symbol.Value, timeframe.ToString(), style.ToString(),
            candles[0].OpenTimeUtc, candles[^1].OpenTimeUtc,
            request.StartingBalance, request.RiskPercent, request.MinRequiredConditions, request.RequiredConditions,
            account.Equity.Amount, candles.Count, setupCount, trades.Count,
            ToDto(summary), equity, trades);
    }

    // ---- Loading + parsing ----

    private IReadOnlyList<Candle> LoadCandles(Symbol symbol, Timeframe timeframe)
    {
        var nativePath = DatasetPath(symbol.Value, timeframe);
        if (File.Exists(nativePath))
        {
            return CsvCandleSource.Load(nativePath).Select(ToDomain).ToList();
        }

        // Fallback: resample up from the finest native file we have for this symbol (we fetch M1). This lets the lab
        // request a timeframe we did not fetch natively (e.g. M3) without a separate download.
        var basePath = DatasetPath(symbol.Value, Timeframe.M1);
        if (File.Exists(basePath))
        {
            var baseCandles = CsvCandleSource.Load(basePath).Select(ToDomain).ToList();
            return CandleAggregator.Resample(baseCandles, timeframe);
        }

        throw new FileNotFoundException(
            $"No recorded-history dataset for {symbol.Value} {timeframe} in '{_dataDirectory}' (expected " +
            $"'{Path.GetFileName(nativePath)}', and no M1 base to resample from).", nativePath);
    }

    private string DatasetPath(string symbol, Timeframe timeframe) =>
        Path.Combine(_dataDirectory, $"{symbol}-{timeframe}.csv");

    private static Candle ToDomain(CandleDto dto) => new(
        new Symbol(dto.Symbol), Enum.Parse<Timeframe>(dto.Timeframe), dto.OpenTimeUtc,
        dto.Open, dto.High, dto.Low, dto.Close, dto.Volume);

    private static (int Count, DateTimeOffset From, DateTimeOffset To) ScanDataset(string path)
    {
        var count = 0;
        DateTimeOffset from = default, to = default;
        var headerSeen = false;

        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!headerSeen)
            {
                headerSeen = true; // the first non-blank line is the header
                continue;
            }

            var firstComma = line.IndexOf(',');
            var secondComma = firstComma < 0 ? -1 : line.IndexOf(',', firstComma + 1);
            var thirdComma = secondComma < 0 ? -1 : line.IndexOf(',', secondComma + 1);
            if (thirdComma < 0)
            {
                continue;
            }

            var openTimeField = line[(secondComma + 1)..thirdComma];
            if (!DateTimeOffset.TryParse(
                    openTimeField, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var openTime))
            {
                continue;
            }

            if (count == 0)
            {
                from = openTime;
            }

            to = openTime;
            count++;
        }

        return (count, from, to);
    }

    // ---- Per-run risk + helpers ----

    /// <summary>Builds a per-run <see cref="RiskOptions"/> from the chosen base risk %, scaling the §2.5.5 loss ladder
    /// proportionally so its rungs stay below the base, and lifting the caps/hard-max to admit the base. Validated.</summary>
    private static RiskOptions BuildRisk(RiskOptions defaults, decimal baseRiskPercent)
    {
        var ladder = defaults.ResolvedLossLadderPercents
            .Select(rung => Math.Round(rung / defaults.BaseRiskPercent * baseRiskPercent, 6))
            .ToArray();

        var risk = new RiskOptions
        {
            BaseRiskPercent = baseRiskPercent,
            MaxOpenPortfolioRiskPercent = Math.Max(defaults.MaxOpenPortfolioRiskPercent, baseRiskPercent),
            MinStopDistancePips = defaults.MinStopDistancePips,
            LossLadderPercents = ladder,
            ConsecutiveWinsForLowestUnit = defaults.ConsecutiveWinsForLowestUnit,
            DipRecoveryFraction = defaults.DipRecoveryFraction,
            HardMaxRiskPercent = Math.Min(AbsoluteMaxRiskPercent, Math.Max(defaults.HardMaxRiskPercent, baseRiskPercent)),
        };

        var errors = risk.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"The backtest risk policy is invalid: {string.Join("; ", errors)}");
        }

        return risk;
    }

    /// <summary>
    /// Builds the per-run <see cref="ConfluenceOptions"/> when the request relaxes the required gate — either by
    /// COUNT (<paramref name="minRequiredConditions"/>, k-of-n) or by SUBSET
    /// (<paramref name="requiredConditions"/>, which specific concepts to require; the optimizer's feature-subset
    /// search). <c>null</c>/<c>null</c> leaves the scanner on the host default (the strict §2.5 model + any baked
    /// per-instrument override). When a subset is given it is required IN FULL by default (strict-on-subset), and the
    /// non-null count blocks a per-instrument k from overriding the explicit subset gate. Validated.
    /// </summary>
    private ConfluenceOptions? BuildConfluence(int? minRequiredConditions, IReadOnlyList<string>? requiredConditions)
    {
        if (minRequiredConditions is null && requiredConditions is null)
        {
            return null;
        }

        var confluence = _defaultConfluence;
        if (requiredConditions is not null)
        {
            var subset = ParseConditions(requiredConditions);
            // Require the whole subset by default (= subset size); an explicit count still wins and, being non-null,
            // also prevents the per-instrument k (applied later by the scanner) from loosening the explicit subset.
            confluence = confluence
                .WithRequiredConditions(subset)
                .WithMinRequiredConditions(minRequiredConditions ?? subset.Count);
        }
        else
        {
            confluence = confluence.WithMinRequiredConditions(minRequiredConditions);
        }

        var errors = confluence.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"The backtest confluence policy is invalid: {string.Join("; ", errors)}");
        }

        return confluence;
    }

    /// <summary>Parses the required-condition names into <see cref="ConfluenceCondition"/>s (case-insensitive); throws
    /// <see cref="ArgumentException"/> on an empty list or an unknown name.</summary>
    private static IReadOnlyList<ConfluenceCondition> ParseConditions(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            throw new ArgumentException("RequiredConditions must contain at least one condition.");
        }

        var parsed = new List<ConfluenceCondition>(names.Count);
        foreach (var name in names)
        {
            if (!Enum.TryParse<ConfluenceCondition>(name, ignoreCase: true, out var condition))
            {
                throw new ArgumentException($"Unknown confluence condition '{name}'.");
            }

            parsed.Add(condition);
        }

        return parsed;
    }

    private static void ValidateRiskAndBalance(BacktestRequest request)
    {
        if (request.StartingBalance <= 0m)
        {
            throw new ArgumentException($"StartingBalance must be positive but was {request.StartingBalance}.");
        }

        if (request.RiskPercent is <= 0m or > AbsoluteMaxRiskPercent)
        {
            throw new ArgumentException(
                $"RiskPercent must be within (0, {AbsoluteMaxRiskPercent}] (the §2.5.5 hard max) but was {request.RiskPercent}.");
        }
    }

    private static Symbol ParseSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol is required.");
        }

        return new Symbol(symbol.Trim());
    }

    private static TradeStyle ParseStyle(string style) =>
        Enum.TryParse<TradeStyle>(style, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown trade style '{style}'.");

    private static Timeframe ParseTimeframe(string timeframe) =>
        Enum.TryParse<Timeframe>(timeframe, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown timeframe '{timeframe}'.");

    /// <summary>The entry timeframe each style backtests on by default (the §4.7 cascade's entry leg).</summary>
    private static Timeframe DefaultTimeframeFor(TradeStyle style) => style switch
    {
        TradeStyle.Scalp => Timeframe.M1,
        TradeStyle.Intraday => Timeframe.M5,
        TradeStyle.Swing => Timeframe.M15,
        TradeStyle.Position => Timeframe.H4,
        _ => Timeframe.M5,
    };

    /// <summary>A deterministic per-run setup id from the setup's identity (so a re-run yields byte-identical trade
    /// ids). A scanner confirms at most one setup per candle, so symbol|style|tf|confirmedAt is unique within a run.</summary>
    private static Guid DeterministicSetupId(Setup setup)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{setup.Symbol.Value}|{setup.Style}|{setup.Timeframe}|{setup.ConfirmedAtUtc:O}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16).ToArray());
    }

    private static PerformanceSummaryDto ToDto(PerformanceSummary summary) => new(
        summary.TradeCount, summary.WinRate, summary.AverageR, summary.ProfitFactor,
        summary.Expectancy, summary.MaxDrawdownR);

    private static PerformanceSummaryDto EmptySummary() => new(0, 0m, 0m, 0m, 0m, 0m);
}
