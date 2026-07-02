using IctTrader.Domain.Configuration;
using IctTrader.Domain.Confluence;
using IctTrader.Domain.Setups;
using IctTrader.Domain.ValueObjects;
using IctTrader.Scanning.Contracts;

namespace IctTrader.Scanning.Application.Signals;

/// <summary>
/// The Scanning module's signals read-side service: it folds the live <see cref="SignalFeedStore"/> snapshot through
/// the pure-domain <see cref="SignalRanker"/> into the ranked, filtered "best opportunities" feed (plan §9). It
/// ORCHESTRATES only — the ranking total-order DECISION lives in the domain ranker; the store owns the bounded set;
/// this service just projects the wire <see cref="SetupDto"/>s to the ranker's domain-typed input, applies the
/// optional query filters (symbol / style / grade floor), caps the top-N, and re-projects the ranked result to
/// <see cref="RankedSignalDto"/> (stamping the 1-based rank).
///
/// <para>Read-only/advisory (plan §6.3): every input and output is an advisory <see cref="SetupDto"/> that routes
/// nowhere near an order path.</para>
/// </summary>
public sealed class SignalRankingService
{
    private readonly SignalFeedStore _store;
    private readonly SignalRanker _ranker;
    private readonly SignalRankingOptions _options;
    private readonly ISignalTakeStateProvider _takeState;

    public SignalRankingService(
        SignalFeedStore store,
        SignalRanker ranker,
        SignalRankingOptions options,
        ISignalTakeStateProvider? takeState = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ranker);
        ArgumentNullException.ThrowIfNull(options);
        _store = store;
        _ranker = ranker;
        _options = options;
        // The PaperTrading-backed take-state enricher (the Host wires it); a standalone module/test falls back to the
        // no-op default so every signal stays in its takeable-unknown wire default.
        _takeState = takeState ?? ISignalTakeStateProvider.None;
    }

    /// <summary>
    /// The ranked top-N over the live feed, with NO query filters — the default feed (used by the live-push handler so
    /// the pushed feed mirrors the unfiltered REST feed). Equivalent to <see cref="Rank"/> with all filters null.
    /// </summary>
    public IReadOnlyList<RankedSignalDto> Top(DateTimeOffset nowUtc) => Rank(nowUtc, null, null, null, null, null);

    /// <summary>
    /// The ranked, filtered top-N. <paramref name="symbol"/>/<paramref name="style"/> narrow case-insensitively on the
    /// wire enum names; <paramref name="minGrade"/> raises the entry floor above the configured
    /// <see cref="SignalRankingOptions.MinGrade"/> (it can never LOWER it — the feed never surfaces a non-alertable
    /// setup); <paramref name="max"/> caps the result (default <see cref="SignalRankingOptions.MaxFeedSize"/>).
    /// </summary>
    public IReadOnlyList<RankedSignalDto> Rank(
        DateTimeOffset nowUtc, string? symbol, string? style, string? minGrade, int? max, string? model = null)
    {
        // The effective floor is the STRICTER of the configured floor and any requested floor (never weaker than the
        // §2.5.4 alertable floor the options pin).
        var floor = _options.MinGrade;
        if (TryParseGrade(minGrade, out var requested) && requested > floor)
        {
            floor = requested;
        }

        var cap = max is { } m && m > 0 ? Math.Min(m, _options.MaxFeedSize) : _options.MaxFeedSize;

        var candidates = _store.Snapshot(nowUtc)
            .Where(s => symbol is null || string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .Where(s => style is null || string.Equals(s.Style, style, StringComparison.OrdinalIgnoreCase))
            .Where(s => model is null || string.Equals(s.Model, model, StringComparison.OrdinalIgnoreCase))
            .Select(ToRankable)
            .Where(r => r.HasValue && r.Value.Grade >= floor)
            .Select(r => r!.Value)
            // Strict determinism: pre-order the input by the deterministic SetupDto.Id so the ranker's STABLE OrderBy
            // chain yields a TOTAL order even for two signals that tie on every rank key (grade/score/RR/TF/recency) —
            // e.g. two different symbols confirmed on the same bar-close. Without this their relative rank would depend
            // on the store's enumeration order (non-deterministic). The Id is the deterministic per-setup hash.
            .OrderBy(r => r.Payload.Id);

        var ranked = _ranker.Rank(candidates);

        return ranked
            .Take(cap)
            .Select((r, i) =>
            {
                // Enrich with the semi-auto TAKE state (entry mode / taken / block reason / expiry) via the
                // consumer-owned port the Host backs with PaperTrading; the no-op default leaves the wire default.
                var take = _takeState.DescribeFor(r.Payload, nowUtc);
                return new RankedSignalDto(
                    Rank: i + 1,
                    Score: r.Score,
                    Setup: r.Payload,
                    EntryMode: take.EntryMode,
                    IsTaken: take.IsTaken,
                    BlockReason: take.BlockReason,
                    ExpiresAtUtc: take.ExpiresAtUtc);
            })
            .ToList();
    }

    /// <summary>
    /// Projects a wire <see cref="SetupDto"/> to the ranker's domain-typed input, parsing the frozen wire enum names
    /// (Grade, TriggerTimeframe). A DTO with an unparseable grade/timeframe (never produced by the real pipeline) is
    /// dropped (null) rather than throwing — the feed stays robust to a malformed redelivery.
    /// </summary>
    private static RankableSignal<SetupDto>? ToRankable(SetupDto dto)
    {
        if (!TryParseGrade(dto.Grade, out var grade) ||
            !Enum.TryParse<Timeframe>(dto.TriggerTimeframe, ignoreCase: true, out var timeframe))
        {
            return null;
        }

        return new RankableSignal<SetupDto>(grade, dto.Score, dto.RewardRatio, timeframe, dto.DetectedAtUtc, dto);
    }

    private static bool TryParseGrade(string? value, out SetupGrade grade)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value.Trim(), ignoreCase: true, out grade) &&
            Enum.IsDefined(grade))
        {
            return true;
        }

        grade = default;
        return false;
    }
}
