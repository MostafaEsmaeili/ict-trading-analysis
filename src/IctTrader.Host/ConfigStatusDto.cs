namespace IctTrader.Host;

/// <summary>
/// A read-only projection of the RESOLVED <c>Ict:*</c> options the operator is running under, for the dashboard's
/// live-config panel ("we're trading EURUSD Intraday, risk 1%, spread 0.7"). It is owned by the Host because it
/// spans several modules' options + the market-data provider selector (the Host is the composition root, allowed to
/// read everything); it carries no order field and routes nowhere near a broker path (§6.3 guardrail).
/// </summary>
public sealed record ConfigStatusDto(
    string Provider,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> ActiveStyles,
    IReadOnlyList<string> ActiveKillzones,
    decimal BaseRiskPercent,
    decimal MaxOpenPortfolioRiskPercent,
    decimal SpreadBasePips,
    decimal CommissionPerLotRoundTripUsd,
    decimal StartingEquity);

/// <summary>
/// Resolves the symbol(s) the running feed scans for the <see cref="ConfigStatusDto"/>. This reads the
/// provider-specific config directly (rather than the conditionally-registered feed options) so it is valid under any
/// provider selection: OANDA exposes its configured instruments (normalised to the dashboard form), while Replay
/// derives the single symbol from its <c>&lt;SYMBOL&gt;-&lt;TF&gt;.csv</c> fixture filename.
/// </summary>
public static class ConfigStatusBuilder
{
    private const string OandaInstrumentsKey = "Ict:MarketData:Oanda:Instruments";
    private const string ReplayFixtureKey = "Ict:MarketData:Replay:FixturePath";
    private const string OandaProvider = "Oanda";
    private const string OandaInstrumentSeparator = "_";
    private const string DefaultOandaInstrument = "EUR_USD";

    public static IReadOnlyList<string> ResolveSymbols(string provider, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.Equals(provider, OandaProvider, StringComparison.OrdinalIgnoreCase))
        {
            var instruments = configuration.GetSection(OandaInstrumentsKey).Get<string[]>() ?? [];
            var resolved = instruments.Length == 0 ? [DefaultOandaInstrument] : instruments;
            return resolved
                .Select(i => i.Replace(OandaInstrumentSeparator, string.Empty, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        // Replay: the symbol is the leading segment of the "<SYMBOL>-<TF>.csv" fixture filename.
        var fixture = configuration.GetValue<string>(ReplayFixtureKey);
        if (string.IsNullOrWhiteSpace(fixture))
        {
            return [];
        }

        // The timeframe is the LAST dash-delimited segment ("<SYMBOL>-<TF>.csv"), so split on the last dash — robust
        // even if a future fixture ever uses a dashed symbol.
        var stem = Path.GetFileNameWithoutExtension(fixture);
        var dash = stem.LastIndexOf('-');
        var symbol = dash > 0 ? stem[..dash] : stem;
        return string.IsNullOrWhiteSpace(symbol) ? [] : [symbol];
    }
}
