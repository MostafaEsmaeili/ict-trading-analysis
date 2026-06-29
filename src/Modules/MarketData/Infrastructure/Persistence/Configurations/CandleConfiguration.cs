using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IctTrader.MarketData.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="CandleEntity"/> to the <c>candles</c> table (plan §7).
/// <para>
/// Prices are <c>numeric(18,8)</c>; <c>open_time_utc</c> is <c>timestamptz</c> (UTC-aware); enums are
/// stored as strings; there is NO <c>xmin</c> concurrency token (candles are immutable — the write model
/// is append-only).  The UNIQUE index on <c>(symbol, timeframe, open_time_utc)</c> is both the idempotent-
/// UPSERT guard and the covering index for range + recent reads.
/// </para>
/// </summary>
internal sealed class CandleConfiguration : IEntityTypeConfiguration<CandleEntity>
{
    public void Configure(EntityTypeBuilder<CandleEntity> builder)
    {
        builder.ToTable("candles");

        // Surrogate PK — auto-incremented serial so INSERT ON CONFLICT DO NOTHING can skip the id column.
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd()
            .IsRequired();

        // ── Natural key columns ───────────────────────────────────────────────────────────────────────

        builder.Property(c => c.Symbol)
            .HasColumnName("symbol")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Timeframe)
            .HasColumnName("timeframe")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.OpenTimeUtc)
            .HasColumnName("open_time_utc")
            .HasColumnType("timestamptz")   // UTC-aware (plan §7)
            .IsRequired();

        // ── OHLC prices — numeric(18,8) (plan §7) ────────────────────────────────────────────────────

        builder.Property(c => c.Open)
            .HasColumnName("open")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(c => c.High)
            .HasColumnName("high")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(c => c.Low)
            .HasColumnName("low")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(c => c.Close)
            .HasColumnName("close")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(c => c.Volume)
            .HasColumnName("volume")
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        // ── Indexes (plan §7) ─────────────────────────────────────────────────────────────────────────
        //
        // UNIQUE on (symbol, timeframe, open_time_utc) — the natural key used by both the idempotent UPSERT
        // (INSERT ... ON CONFLICT (symbol, timeframe, open_time_utc) DO NOTHING) and the covering index for
        // range queries (WHERE symbol = ? AND timeframe = ? AND open_time_utc BETWEEN ? AND ?).
        builder.HasIndex(c => new { c.Symbol, c.Timeframe, c.OpenTimeUtc })
            .IsUnique()
            .HasDatabaseName("uq_candles_symbol_timeframe_open_time");
    }
}
