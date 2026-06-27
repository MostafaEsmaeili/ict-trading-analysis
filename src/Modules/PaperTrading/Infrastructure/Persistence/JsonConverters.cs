using System.Text.Json;
using System.Text.Json.Serialization;
using IctTrader.Domain.Setups;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IctTrader.PaperTrading.Infrastructure.Persistence;

/// <summary>
/// EF Core <see cref="ValueConverter{TModel,TProvider}"/>s for domain value objects that are stored as
/// JSONB columns (plan §7). All are internal — they exist only for the <see cref="PaperTradingDbContext"/>
/// and its entity configurations.
/// </summary>
internal static class JsonConverters
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ──────────────────────────────────────────────────────────────────────────────────────────────────
    // PaperAccount: _reservedRiskByTrade  Dictionary<Guid, Money>  →  jsonb
    // Stored as { "guid": amount } — Money unwrapped to its raw decimal for compact, queryable JSON.
    // ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the reservation-ledger backing field between its domain type
    /// (<see cref="Dictionary{Guid,Money}"/>) and a compact JSON object stored in a <c>jsonb</c> column.
    /// The Money wrapper is unwrapped to the raw decimal amount so the JSON is human-readable and
    /// Postgres operators can inspect individual entries.
    /// </summary>
    public static readonly ValueConverter<Dictionary<Guid, Money>, string> ReservationLedgerConverter =
        new(
            dict => JsonSerializer.Serialize(
                dict.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Amount),
                SerializerOptions),
            json => JsonSerializer
                .Deserialize<Dictionary<string, decimal>>(json, SerializerOptions)!
                .ToDictionary(kv => Guid.Parse(kv.Key), kv => new Money(kv.Value)));

    // ──────────────────────────────────────────────────────────────────────────────────────────────────
    // PaperTrade: _legs  List<FillLeg>  →  jsonb
    // FillLeg is a readonly record struct with PositionSize/Price/TradeCloseReason/TradeCosts/DateTimeOffset.
    // Stored as a JSON array of flat objects.
    // ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts the fill-leg ledger between its domain type (<see cref="List{FillLeg}"/>) and a JSON
    /// array stored in a <c>jsonb</c> column.  Each leg is flattened to a plain DTO for compact storage;
    /// the aggregate's <c>_legs</c> backing field is populated directly on materialisation so the ledger
    /// order is preserved.
    /// </summary>
    public static readonly ValueConverter<List<FillLeg>, string> FillLegListConverter =
        new(
            legs => JsonSerializer.Serialize(legs.Select(FillLegDto.From).ToList(), SerializerOptions),
            json => JsonSerializer
                .Deserialize<List<FillLegDto>>(json, SerializerOptions)!
                .Select(dto => dto.ToDomain())
                .ToList());

    // ──────────────────────────────────────────────────────────────────────────────────────────────────
    // PaperTrade: Plan  TradePlan  →  jsonb
    // TradePlan is a readonly record struct whose TargetLadder nested struct requires the direction to
    // validate ordering.  OwnsOne decomposition cannot supply the direction to the nested constructor, so
    // JSONB is used instead — it stores the complete plan as a flat JSON object and reconstructs it in
    // one shot (direction available, so TargetLadder validates cleanly).
    // ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="TradePlan"/> between its domain type and a flat JSON object stored in a
    /// <c>jsonb</c> column.  RewardRatio is intentionally omitted from the DTO — it is derived from the
    /// geometry by the <see cref="TradePlan"/> primary constructor and never double-stored.
    /// </summary>
    public static readonly ValueConverter<TradePlan, string> TradePlanConverter =
        new(
            plan => JsonSerializer.Serialize(TradePlanDto.From(plan), SerializerOptions),
            json => JsonSerializer.Deserialize<TradePlanDto>(json, SerializerOptions)!.ToDomain());

    // ──────────────────────────────────────────────────────────────────────────────────────────────────
    // ArmedEntry: Setup  →  jsonb
    // Setup is not an entity (no Id); storing it as JSONB preserves the full advisory snapshot without
    // coupling the PaperTrading schema to a Setups table (which doesn't exist yet in WP2).
    // ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a confirmed advisory <see cref="Domain.Setups.Setup"/> between its domain type and a JSON
    /// object stored in a <c>jsonb</c> column.  Setup has no persistence identity (it is value-object-like
    /// despite its class shape), so JSONB is the correct plan §7 vehicle for it.  The snapshot is
    /// advisory-only and never mutated after arming, and the DTO carries EVERY field that feeds a domain
    /// reconstruction — including the FVG-SEM-2b <c>StackedFartherBound</c> — so the round-trip is exact and
    /// the reloaded Setup does not diverge from the queryable <c>armed_entries.stacked_farther_bound</c> mirror.
    /// </summary>
    public static readonly ValueConverter<Domain.Setups.Setup, string> SetupConverter =
        new(
            setup => JsonSerializer.Serialize(SetupDto.From(setup), SerializerOptions),
            json => JsonSerializer.Deserialize<SetupDto>(json, SerializerOptions)!.ToDomain());

    // ─── Private DTO types used only by this module's converters ─────────────────────────────────────

    private sealed record TradePlanDto(
        string Direction,
        decimal Entry,
        decimal Stop,
        IReadOnlyList<decimal> Targets,
        int RunnerIndex)
    {
        public static TradePlanDto From(TradePlan p) => new(
            p.Direction.ToString(),
            p.Entry.Value,
            p.Stop.Value,
            // Persist the FULL ordered N-tier ladder + the runner index so deeper TGR-1/2 SD-projection tiers survive
            // the round-trip (a 2-target shape silently dropped tiers[2..] under DB-as-state).
            p.Targets.Targets.Select(price => price.Value).ToList(),
            p.Targets.RunnerIndex);

        public TradePlan ToDomain()
        {
            var dir = Enum.Parse<Domain.ValueObjects.Direction>(Direction);
            return new TradePlan(
                dir,
                new Price(Entry),
                new Price(Stop),
                new TargetLadder(dir, Targets.Select(value => new Price(value)).ToList(), RunnerIndex));
        }
    }

    private sealed record FillLegDto(
        decimal Lots,
        decimal ExitPrice,
        string Reason,
        decimal SpreadCost,
        decimal Commission,
        DateTimeOffset AtUtc)
    {
        public static FillLegDto From(FillLeg leg) => new(
            leg.Lots.Lots,
            leg.ExitPrice.Value,
            leg.Reason.ToString(),
            leg.Costs.SpreadCost.Amount,
            leg.Costs.Commission.Amount,
            leg.AtUtc);

        public FillLeg ToDomain() => new(
            new PositionSize(Lots),
            new Price(ExitPrice),
            Enum.Parse<TradeCloseReason>(Reason),
            new TradeCosts(new Money(SpreadCost), new Money(Commission)),
            AtUtc);
    }

    private sealed record SetupDto(
        string Symbol,
        string Style,
        string Timeframe,
        string Grade,
        int Score,
        string Direction,
        decimal Entry,
        decimal Stop,
        IReadOnlyList<decimal> Targets,
        int RunnerIndex,
        string Reason,
        DateTimeOffset ConfirmedAtUtc,
        // FVG-SEM-2b: the stacked first-FVG far edge (null off the default path). It is NOT a TradePlan tier, so it is
        // NOT carried by Targets — without this field the snapshot silently dropped it and the reloaded
        // Setup.StackedFartherBound came back null, diverging from the queryable armed_entries.stacked_farther_bound
        // column (which IS what the runtime NIX reads). Serialising it here keeps the snapshot faithful.
        decimal? StackedFartherBound)
    {
        public static SetupDto From(Domain.Setups.Setup s) => new(
            s.Symbol.Value,
            s.Style.ToString(),
            s.Timeframe.ToString(),
            s.Grade.ToString(),
            s.Score,
            s.Direction.ToString(),
            s.Plan.Entry.Value,
            s.Plan.Stop.Value,
            // Persist the FULL ordered N-tier ladder + the runner index (see TradePlanDto) so the snapshot's deeper
            // SD-projection targets survive the JSONB round-trip.
            s.Plan.Targets.Targets.Select(price => price.Value).ToList(),
            s.Plan.Targets.RunnerIndex,
            s.Reason.Text,
            s.ConfirmedAtUtc,
            s.StackedFartherBound);

        public Domain.Setups.Setup ToDomain()
        {
            var direction = Enum.Parse<Domain.ValueObjects.Direction>(Direction);
            var plan = new Domain.Setups.TradePlan(
                direction,
                new Price(Entry),
                new Price(Stop),
                new Domain.Setups.TargetLadder(
                    direction, Targets.Select(value => new Price(value)).ToList(), RunnerIndex));

            return new Domain.Setups.Setup(
                new Domain.ValueObjects.Symbol(Symbol),
                Enum.Parse<Domain.Styles.TradeStyle>(Style),
                Enum.Parse<Domain.ValueObjects.Timeframe>(Timeframe),
                Enum.Parse<Domain.Setups.SetupGrade>(Grade),
                Score,
                plan,
                new Domain.Setups.SetupReason(Reason),
                ConfirmedAtUtc,
                StackedFartherBound);
        }
    }
}
