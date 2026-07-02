using IctTrader.Domain.Setups;
using IctTrader.Domain.Styles;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PaperTrade"/> to the <c>paper_trades</c> table (plan §7).
/// <para>
/// Backing-field access: every get-only or private-setter property is mapped via
/// <see cref="PropertyAccessMode.Field"/> so EF sets the fields directly after constructing the aggregate
/// via its <c>private</c> parameterless constructor, without invoking the public constructor's invariant
/// guards.
/// </para>
/// <para>
/// <see cref="TradePlan"/> (which contains the nested <see cref="TargetLadder"/>) is stored as
/// <c>jsonb</c> via <see cref="JsonConverters.TradePlanConverter"/>.  The nested <see cref="TargetLadder"/>
/// requires the direction to validate its price ordering, which an <c>OwnsOne</c> decomposition cannot
/// supply at construction time.  Storing the whole plan as a single JSONB object lets the converter pass
/// the direction to the inner constructor in one shot.  The <see cref="TradePlan.RewardRatio"/> is
/// intentionally omitted from the DTO — it is derived from the geometry by the primary constructor and
/// must not be double-stored.
/// </para>
/// <para>
/// The <c>_legs</c> fill-leg list is stored as <c>jsonb</c> (plan §7 "PaperTrade.Fills") via
/// <see cref="JsonConverters.FillLegListConverter"/>.  The blended close-side results
/// (<see cref="PaperTrade.RealizedR"/>, <see cref="PaperTrade.GrossPnl"/>, <see cref="PaperTrade.Costs"/>,
/// <see cref="PaperTrade.RealizedPnl"/>, plus <see cref="PaperTrade.ExitPrice"/> /
/// <see cref="PaperTrade.CloseReason"/> / <see cref="PaperTrade.ClosedAtUtc"/>) ARE persisted to their own
/// columns: the aggregate folds them once at close and does NOT recompute them on materialization, so a
/// reloaded closed trade returns the booked figures directly.  Only the always-computed alias members
/// (<see cref="PaperTrade.NetPnl"/>, <see cref="PaperTrade.NetR"/>, <see cref="PaperTrade.Direction"/>,
/// <see cref="PaperTrade.Entry"/>, <see cref="PaperTrade.Stop"/>, <see cref="PaperTrade.IsBreakevenArmed"/>)
/// are <c>Ignore</c>d.
/// </para>
/// </summary>
internal sealed class PaperTradeConfiguration : IEntityTypeConfiguration<PaperTrade>
{
    public void Configure(EntityTypeBuilder<PaperTrade> builder)
    {
        builder.ToTable("paper_trades");

        // Primary key — EF sets the auto-property backing field via field access.
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("id")
            .IsRequired();

        // Optimistic concurrency via the Postgres xmin system column (plan §7).
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .HasColumnName("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // ── Account + symbol ──────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.AccountId)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("account_id")
            .IsRequired();

        builder.Property(t => t.Symbol)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("symbol")
            .HasConversion(
                sym => sym.Value,
                val => new Symbol(val))
            .HasMaxLength(20)
            .IsRequired();

        // ── Trade style + timeframe ───────────────────────────────────────────────────────────────────

        builder.Property(t => t.Style)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("style")
            .HasConversion<string>()     // enum as string (plan §7)
            .HasMaxLength(20)
            .IsRequired();

        // The setup model that produced this trade (plan §16). Every pre-multi-model row IS a canonical-model
        // trade, so the column default backfills them truthfully on migration.
        builder.Property(t => t.Model)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("model")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(SetupModel.Ict2022)
            .IsRequired();

        builder.Property(t => t.Timeframe)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("timeframe")
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        // ── Trade plan: jsonb (plan §7) ───────────────────────────────────────────────────────────────
        // TradePlan / TargetLadder are readonly record structs whose constructors validate price ordering.
        // TargetLadder requires the direction at construction time, which OwnsOne cannot provide.  JSONB
        // stores the full plan as a flat object; the converter supplies the direction in one shot.

        builder.Property(t => t.Plan)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("plan")
            .HasConversion(JsonConverters.TradePlanConverter)
            .HasColumnType("jsonb")
            .IsRequired();

        // ── Position geometry ─────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.Size)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("size_lots")
            .HasConversion(
                ps => ps.Lots,
                lots => new PositionSize(lots))
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(t => t.RemainingSize)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("remaining_size_lots")
            .HasConversion(
                ps => ps.Lots,
                lots => new PositionSize(lots))
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        // Private backing fields for pip / value-per-pip geometry — mapped by field name.
        builder.Property<decimal>("_pipSize")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("pip_size")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property<decimal>("_valuePerPip")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("value_per_pip")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property<decimal>("_valuePerPipForPosition")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("value_per_pip_for_position")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        // ── Risk budget ───────────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.RiskBudget)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("risk_budget")
            .HasConversion(
                money => money.Amount,
                amount => new Money(amount))
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(t => t.InitialRiskPerUnit)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("initial_risk_per_unit")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.Status)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("status")
            .HasConversion<string>()     // enum as string (plan §7)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.Lifecycle)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("lifecycle")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.HasScaledOut)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("has_scaled_out")
            .IsRequired();

        // ── Timestamps ────────────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.OpenedAtUtc)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("opened_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        // The management-eligibility edge (the open-bar's open) — distinct from OpenedAtUtc (the fill time) for an
        // armed-triggered open, so it MUST round-trip or the per-candle look-ahead filter would mis-time a reloaded
        // trade (plan §4.1).
        builder.Property(t => t.ManagedFromUtc)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("managed_from_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        // Private _lastActivityAtUtc — keeps the management timeline monotonic.
        builder.Property<DateTimeOffset>("_lastActivityAtUtc")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("last_activity_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.ClosedAtUtc)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("closed_at_utc")
            .HasColumnType("timestamptz");

        // ── Live stop ─────────────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.CurrentStop)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("current_stop")
            .HasConversion(
                price => price.Value,
                val => new Price(val))
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        // ── Close-side results ────────────────────────────────────────────────────────────────────────

        builder.Property(t => t.ExitPrice)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("exit_price")
            .HasConversion(
                price => price.HasValue ? (decimal?)price.Value.Value : null,
                val => val.HasValue ? new Price(val.Value) : (Price?)null)
            .HasColumnType("numeric(18,8)");

        builder.Property(t => t.CloseReason)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("close_reason")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(t => t.RealizedR)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("realized_r")
            .HasColumnType("numeric(18,8)");

        builder.Property(t => t.GrossPnl)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("gross_pnl")
            .HasConversion(
                money => money.HasValue ? (decimal?)money.Value.Amount : null,
                amount => amount.HasValue ? new Money(amount.Value) : (Money?)null)
            .HasColumnType("numeric(18,2)");

        builder.Property(t => t.Costs)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("costs")
            .HasConversion(
                money => money.HasValue ? (decimal?)money.Value.Amount : null,
                amount => amount.HasValue ? new Money(amount.Value) : (Money?)null)
            .HasColumnType("numeric(18,2)");

        builder.Property(t => t.RealizedPnl)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("realized_pnl")
            .HasConversion(
                money => money.HasValue ? (decimal?)money.Value.Amount : null,
                amount => amount.HasValue ? new Money(amount.Value) : (Money?)null)
            .HasColumnType("numeric(18,2)");

        // ── Fill-leg ledger: jsonb (plan §7) ──────────────────────────────────────────────────────────
        // _legs is the append-only exit ledger. The blended close-side results (RealizedR, GrossPnl, Costs,
        // RealizedPnl) are folded once at close and persisted to their own columns above — only the
        // always-computed aliases (NetPnl, NetR, Direction, ...) are Ignore()d below.
        //
        // A custom ValueComparer is required because EF's change tracker uses reference equality for
        // collection values stored via value converters — it would miss in-place mutations (Add calls)
        // to the same List<FillLeg> instance.  The comparer forces EF to re-evaluate the converter
        // on every SaveChanges so the serialized JSON always reflects the current ledger state.
        var legsComparer = new ValueComparer<List<FillLeg>>(
            (a, b) => a != null && b != null && a.SequenceEqual(b),
            legs => legs.Aggregate(0, (hash, leg) => HashCode.Combine(hash, leg.GetHashCode())),
            legs => legs.ToList());

        builder.Property<List<FillLeg>>("_legs")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("legs")
            .HasConversion(JsonConverters.FillLegListConverter, legsComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        // ── Computed / alias properties — explicitly ignored ──────────────────────────────────────────
        // These are derived at runtime; storing them would risk drift from the persisted fields.

        builder.Ignore(t => t.NetPnl);
        builder.Ignore(t => t.NetR);
        builder.Ignore(t => t.Direction);
        builder.Ignore(t => t.Entry);
        builder.Ignore(t => t.Stop);
        builder.Ignore(t => t.IsBreakevenArmed);
        builder.Ignore(t => t.ValuePerPipForPosition);
        builder.Ignore(t => t.Legs);

        // ── Indexes (plan §7) ─────────────────────────────────────────────────────────────────────────

        builder.HasIndex(t => new { t.AccountId, t.Status })
            .HasDatabaseName("ix_paper_trades_account_id_status");

        builder.HasIndex(t => new { t.Symbol, t.Status })
            .HasDatabaseName("ix_paper_trades_symbol_status");

        builder.HasIndex(t => new { t.AccountId, t.ClosedAtUtc })
            .HasDatabaseName("ix_paper_trades_account_id_closed_at");

        // ── Relationship ──────────────────────────────────────────────────────────────────────────────

        builder.HasOne<PaperAccount>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
