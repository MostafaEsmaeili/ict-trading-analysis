using System.Globalization;
using System.Text.Json;
using IctTrader.MarketData.Contracts;

namespace IctTrader.MarketData.Infrastructure.Feeds;

/// <summary>
/// Parses an OANDA v20 candles response (<c>GET /v3/instruments/{instr}/candles</c>) into <see cref="CandleDto"/>s
/// — the pure, unit-testable core of <see cref="OandaMarketDataFeed"/>. The shape (see the OANDA v20 instrument
/// API) is:
/// <code>
/// { "instrument": "EUR_USD", "granularity": "M5",
///   "candles": [ { "time": "2024-07-01T07:00:00.000000000Z",
///                  "mid": { "o": "1.0832", "h": "1.0840", "l": "1.0828", "c": "1.0836" },
///                  "volume": 123, "complete": true }, ... ] }
/// </code>
/// Rules: only <c>complete: true</c> candles are mapped (an in-progress candle is skipped); the RFC3339
/// nanosecond <c>time</c> is read as UTC; o/h/l/c are STRINGS parsed with the invariant culture; <c>volume</c>
/// is a number; the instrument is normalised to the dashboard form by stripping the underscore
/// (<c>EUR_USD</c> → <c>EURUSD</c>). A malformed candle fails fast with a <see cref="FormatException"/>.
/// <para><b>Read-only by shape:</b> this type only reads JSON — it has no order/trade/write path.</para>
/// </summary>
internal static class OandaCandleParser
{
    // The OANDA v20 JSON property names — the external API contract, so these string literals are unavoidable.
    private const string CandlesProperty = "candles";
    private const string InstrumentProperty = "instrument";
    private const string GranularityProperty = "granularity";
    private const string CompleteProperty = "complete";
    private const string TimeProperty = "time";
    private const string MidProperty = "mid";
    private const string OpenProperty = "o";
    private const string HighProperty = "h";
    private const string LowProperty = "l";
    private const string CloseProperty = "c";
    private const string VolumeProperty = "volume";

    private const char OandaInstrumentSeparator = '_';

    /// <summary>
    /// Parses the OANDA candles <paramref name="json"/>, returning one <see cref="CandleDto"/> per
    /// <c>complete</c> candle in the order OANDA supplied them (chronological).
    /// </summary>
    public static CandleDto[] Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var instrument = GetRequiredString(root, InstrumentProperty);
        var granularity = GetRequiredString(root, GranularityProperty);
        var symbol = NormaliseSymbol(instrument);

        if (!root.TryGetProperty(CandlesProperty, out var candlesElement)
            || candlesElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"OANDA response is missing the '{CandlesProperty}' array.");
        }

        var candles = new List<CandleDto>(candlesElement.GetArrayLength());
        foreach (var candle in candlesElement.EnumerateArray())
        {
            if (!IsComplete(candle))
            {
                continue;   // an in-progress candle is not yet tradeable — skip it (§2.5.8 closed-bar only)
            }

            candles.Add(ParseCandle(candle, symbol, granularity));
        }

        return [.. candles];
    }

    private static CandleDto ParseCandle(JsonElement candle, string symbol, string granularity)
    {
        if (!candle.TryGetProperty(MidProperty, out var mid) || mid.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"OANDA candle is missing its '{MidProperty}' price object.");
        }

        return new CandleDto(
            Symbol: symbol,
            Timeframe: NormaliseGranularity(granularity),
            OpenTimeUtc: ParseTime(GetRequiredString(candle, TimeProperty)),
            Open: ParsePrice(mid, OpenProperty),
            High: ParsePrice(mid, HighProperty),
            Low: ParsePrice(mid, LowProperty),
            Close: ParsePrice(mid, CloseProperty),
            Volume: ParseVolume(candle));
    }

    private static bool IsComplete(JsonElement candle)
        => candle.TryGetProperty(CompleteProperty, out var complete)
            && complete.ValueKind is JsonValueKind.True or JsonValueKind.False
            && complete.GetBoolean();

    /// <summary>Strips the OANDA underscore so <c>EUR_USD</c> becomes the dashboard form <c>EURUSD</c>.</summary>
    private static string NormaliseSymbol(string instrument)
        => instrument.Replace(OandaInstrumentSeparator.ToString(), string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Maps an OANDA granularity to the scanner <c>Timeframe</c> member NAME so the persisted candle's timeframe
    /// string parses downstream: the intraday granularities (M1/M5/M15/M30/H1/H4) already match the enum members,
    /// while OANDA's daily <c>D</c> → <c>D1</c> and weekly <c>W</c> → <c>W1</c>. Anything else is passed through
    /// (the options allowlist has already rejected unusable granularities at startup).
    /// </summary>
    private static string NormaliseGranularity(string granularity) => granularity switch
    {
        "D" => "D1",
        "W" => "W1",
        _ => granularity,
    };

    /// <summary>
    /// Parses the RFC3339 timestamp as UTC. OANDA emits nanosecond precision
    /// (<c>2024-07-01T07:00:00.000000000Z</c>), which <see cref="DateTimeOffset"/> cannot parse directly, so the
    /// fractional seconds are trimmed to the 7-digit (100ns tick) form .NET supports before parsing.
    /// </summary>
    private static DateTimeOffset ParseTime(string time)
    {
        var trimmed = TrimFractionalSecondsToTicks(time);

        if (!DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new FormatException($"OANDA candle has an unparseable '{TimeProperty}' value '{time}'.");
        }

        return parsed;
    }

    /// <summary>
    /// Trims an RFC3339 fractional-seconds field to at most 7 digits (the .NET tick resolution), preserving the
    /// trailing zone designator. OANDA's nanosecond (9-digit) precision otherwise overflows the BCL parser.
    /// </summary>
    private static string TrimFractionalSecondsToTicks(string time)
    {
        const int maxFractionalDigits = 7;

        var dotIndex = time.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex < 0)
        {
            return time;
        }

        var fractionStart = dotIndex + 1;
        var fractionEnd = fractionStart;
        while (fractionEnd < time.Length && char.IsAsciiDigit(time[fractionEnd]))
        {
            fractionEnd++;
        }

        var fractionLength = fractionEnd - fractionStart;
        if (fractionLength <= maxFractionalDigits)
        {
            return time;
        }

        var keptFraction = time.AsSpan(fractionStart, maxFractionalDigits);
        var zoneSuffix = time.AsSpan(fractionEnd);
        return string.Concat(time.AsSpan(0, fractionStart), keptFraction, zoneSuffix);
    }

    private static decimal ParsePrice(JsonElement mid, string property)
    {
        if (!mid.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"OANDA candle 'mid' is missing the string price '{property}'.");
        }

        var raw = element.GetString();
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
        {
            throw new FormatException($"OANDA candle price '{property}' value '{raw}' is not a valid number.");
        }

        return price;
    }

    private static decimal ParseVolume(JsonElement candle)
    {
        if (!candle.TryGetProperty(VolumeProperty, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDecimal(out var volume))
        {
            throw new FormatException($"OANDA candle has a missing or invalid '{VolumeProperty}'.");
        }

        return volume;
    }

    private static string GetRequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(value.GetString()))
        {
            throw new FormatException($"OANDA response is missing the required string '{property}'.");
        }

        return value.GetString()!;
    }
}
