using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IctTrader.Host;
using IctTrader.Host.Backtesting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IctTrader.IntegrationTests;

/// <summary>
/// Locks the on-demand backtest API (plan §15). It boots the REAL Host (so the engine resolves against the real
/// scanner + orchestrator factories DI) pointed at a temp dataset directory — no Postgres is needed because the
/// engine is in-memory and the replay ingestion is left off — writes a small EURUSD M5 CSV, and proves the datasets
/// endpoint lists it and the run endpoint replays it deterministically (same request twice → byte-identical result).
/// </summary>
public sealed class BacktestEndpointTests : IDisposable
{
    private readonly string _dataDir;

    public BacktestEndpointTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "ict-backtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(Path.Combine(_dataDir, "EURUSD-M5.csv"), BuildCsv(30), Encoding.UTF8);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    [Fact]
    public async Task The_datasets_endpoint_lists_the_written_history()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var datasets = await client.GetFromJsonAsync<IReadOnlyList<BacktestDatasetDto>>("/api/backtest/datasets");

        datasets.Should().NotBeNull();
        var eurusd = datasets!.Single(d => d.Symbol == "EURUSD" && d.Timeframe == "M5");
        eurusd.CandleCount.Should().Be(30);
    }

    [Fact]
    public async Task A_backtest_runs_deterministically_over_the_dataset()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new BacktestRequest(
            Symbol: "EURUSD", Style: "Intraday", StartingBalance: 10_000m, RiskPercent: 1.0m, Timeframe: "M5");

        var first = await client.PostAsJsonAsync("/api/backtest", request);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstResult = await first.Content.ReadFromJsonAsync<BacktestResponse>();

        firstResult.Should().NotBeNull();
        firstResult!.CandlesProcessed.Should().Be(30);
        firstResult.Symbol.Should().Be("EURUSD");
        firstResult.Timeframe.Should().Be("M5");
        // 30 flat warmup candles can never confirm a setup, so the run is a clean no-trade baseline.
        firstResult.TradeCount.Should().Be(0);
        firstResult.EndingBalance.Should().Be(10_000m);

        // Determinism: the same request replays byte-identically (the engine is pure + in-memory).
        var second = await client.PostAsJsonAsync("/api/backtest", request);
        var secondResult = await second.Content.ReadFromJsonAsync<BacktestResponse>();
        JsonSerializer.Serialize(secondResult).Should().Be(JsonSerializer.Serialize(firstResult));
    }

    [Fact]
    public async Task A_missing_dataset_returns_404()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new BacktestRequest(
            Symbol: "ZZZZZZ", Style: "Intraday", StartingBalance: 10_000m, RiskPercent: 1.0m, Timeframe: "M5");

        var response = await client.PostAsJsonAsync("/api/backtest", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_out_of_range_risk_returns_400()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var request = new BacktestRequest(
            Symbol: "EURUSD", Style: "Intraday", StartingBalance: 10_000m, RiskPercent: 99m, Timeframe: "M5");

        var response = await client.PostAsJsonAsync("/api/backtest", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_optimize_endpoint_ranks_the_grid_combinations()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        // 1 symbol × 2 styles × 1 timeframe × 2 risk% = 4 combinations, all on the one EURUSD-M5 dataset we wrote.
        var request = new OptimizeRequest(
            Symbols: ["EURUSD"],
            Styles: ["Intraday", "Scalp"],
            RiskPercents: [0.5m, 1.0m],
            StartingBalance: 10_000m,
            Timeframes: ["M5"]);

        var response = await client.PostAsJsonAsync("/api/backtest/optimize", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<OptimizeResponse>();
        result.Should().NotBeNull();
        result!.CombinationCount.Should().Be(4);
        result.Results.Should().HaveCount(4);
        result.Results.Should().OnlyContain(r => r.Symbol == "EURUSD" && r.Timeframe == "M5");
    }

    private WebApplicationFactory<Program> CreateFactory() => new BacktestFactory(_dataDir);

    /// <summary>Boots the Host with the backtest data dir pointed at the temp fixture and ingestion left OFF (Replay
    /// disabled), so no Postgres connection is required — the in-memory backtest never touches the database.</summary>
    private sealed class BacktestFactory(string dataDir) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Ict:Backtest:DataDirectory", dataDir);
            builder.UseSetting("Ict:MarketData:Provider", "Replay");
            builder.UseSetting($"{ReplayFeedOptions.SectionName}:Enabled", "false");
        }
    }

    private static string BuildCsv(int candleCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,Timeframe,OpenTimeUtc,Open,High,Low,Close,Volume");
        var open = new DateTimeOffset(2024, 7, 1, 2, 0, 0, TimeSpan.Zero); // London Open killzone
        for (var i = 0; i < candleCount; i++)
        {
            var time = open.AddMinutes(5 * i).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            // Flat, valid OHLC (high >= open/close, low <= open/close) — no displacement, so no setup ever confirms.
            sb.AppendLine(CultureInfo.InvariantCulture, $"EURUSD,M5,{time},1.08000,1.08050,1.07950,1.08010,100");
        }

        return sb.ToString();
    }
}
