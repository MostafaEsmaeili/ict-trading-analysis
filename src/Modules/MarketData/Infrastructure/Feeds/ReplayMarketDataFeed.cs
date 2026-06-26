using System.Runtime.CompilerServices;
using IctTrader.MarketData.Application.Abstractions;
using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// A read-only feed that replays a fixed candle set in chronological order (plan §6.1 — tests/backtest). It
/// drives a <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider"/>-style deterministic run: the
/// same candles always produce the same setups, so a replay reproduces a live run bit-for-bit. The candles
/// are sorted by open time on construction (a stable sort, so same-timestamp candles across symbols keep
/// their supplied order), making the chronological-delivery contract structural rather than caller-trusted.
/// </summary>
public sealed class ReplayMarketDataFeed : IMarketDataFeed
{
    /// <summary>The provider name used for status and feed selection.</summary>
    public const string ProviderName = "Replay";

    private readonly IReadOnlyList<CandleDto> _candles;

    public ReplayMarketDataFeed(IEnumerable<CandleDto> candles)
    {
        ArgumentNullException.ThrowIfNull(candles);
        _candles = candles.OrderBy(candle => candle.OpenTimeUtc).ToList();
    }

    public string Provider => ProviderName;

    public bool IsReadOnly => true;

    public async IAsyncEnumerable<CandleDto> StreamCandlesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var candle in _candles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return candle;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
