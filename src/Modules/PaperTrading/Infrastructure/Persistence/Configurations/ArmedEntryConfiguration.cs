using IctTrader.Domain.Setups;
using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="ArmedEntry"/> to the <c>armed_entries</c> table (plan §7).
/// <para>
/// The <see cref="Setup"/> property (the confirmed advisory setup snapshot) is stored as <c>jsonb</c>
/// via <see cref="JsonConverters.SetupConverter"/>.  <see cref="Setup"/> is not an entity — it carries no
/// persistence identity — and coupling this table to a future Setups table would be premature for WP2.
/// The JSONB snapshot is advisory-only and never mutated after the entry is armed.
/// </para>
/// <para>
/// Computed delegation properties (<see cref="ArmedEntry.Symbol"/>, <see cref="ArmedEntry.Direction"/>)
/// are explicitly ignored — they delegate to <c>Setup</c> at runtime and must not be stored separately.
/// </para>
/// </summary>
internal sealed class ArmedEntryConfiguration : IEntityTypeConfiguration<ArmedEntry>
{
    public void Configure(EntityTypeBuilder<ArmedEntry> builder)
    {
        builder.ToTable("armed_entries");

        // Primary key (also the future trade id — the arm→trigger key re-label).
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
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

        // ── Account ───────────────────────────────────────────────────────────────────────────────────

        builder.Property(e => e.AccountId)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("account_id")
            .IsRequired();

        // ── Advisory setup snapshot: jsonb (plan §7) ─────────────────────────────────────────────────
        // Setup is value-object-like — no identity, never mutated after arming.  JSONB preserves the
        // full advisory snapshot without coupling to a Setups table.

        builder.Property(e => e.Setup)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("setup")
            .HasConversion(JsonConverters.SetupConverter)
            .HasColumnType("jsonb")
            .IsRequired();

        // ── Position geometry ─────────────────────────────────────────────────────────────────────────

        builder.Property(e => e.Size)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("size_lots")
            .HasConversion(
                ps => ps.Lots,
                lots => new PositionSize(lots))
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(e => e.RiskBudget)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("risk_budget")
            .HasConversion(
                money => money.Amount,
                amount => new Money(amount))
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(e => e.PipSize)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("pip_size")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(e => e.ValuePerPip)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("value_per_pip")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        // ── FVG-SEM-2b stacked farther bound (nullable) ───────────────────────────────────────────────
        // The far-edge of the deeper stacked FVG the wrong-order NIX watches; null when the entry was not stacked.

        builder.Property(e => e.StackedFartherBound)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("stacked_farther_bound")
            .HasColumnType("numeric(18,8)");

        // ── Instrument class ──────────────────────────────────────────────────────────────────────────

        builder.Property(e => e.InstrumentClass)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("instrument_class")
            .HasConversion<string>()     // enum as string (plan §7)
            .HasMaxLength(10)
            .IsRequired();

        // ── Timestamps ────────────────────────────────────────────────────────────────────────────────

        builder.Property(e => e.ArmedAtUtc)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("armed_at_utc")
            .HasColumnType("timestamptz")
            .IsRequired();

        // ── Status ────────────────────────────────────────────────────────────────────────────────────

        builder.Property(e => e.Status)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ── Computed / delegation properties — explicitly ignored ─────────────────────────────────────

        builder.Ignore(e => e.Symbol);
        builder.Ignore(e => e.Direction);

        // ── Index: active armed entries per account ───────────────────────────────────────────────────

        builder.HasIndex(e => new { e.AccountId, e.Status })
            .HasDatabaseName("ix_armed_entries_account_id_status");

        // ── Relationship: an armed entry belongs to one account ───────────────────────────────────────
        // Mirrors paper_trades — a resting limit's reserved risk lives on its PaperAccount, so the
        // account_id is a foreign key (Restrict) to paper_accounts.id; an orphan armed entry cannot persist.
        builder.HasOne<PaperAccount>()
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
