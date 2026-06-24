using IctTrader.Domain.Trading;
using IctTrader.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IctTrader.PaperTrading.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="PaperAccount"/> to the <c>paper_accounts</c> table (plan §7).
/// <para>
/// Backing-field access: every get-only or private-setter property is mapped via
/// <see cref="PropertyAccessMode.Field"/> so EF never sees partially-materialised state and the
/// aggregate's invariants remain intact.
/// </para>
/// <para>
/// The reservation ledger (<c>_reservedRiskByTrade</c>) is stored as <c>jsonb</c> (plan §7) via a
/// <see cref="JsonConverters.ReservationLedgerConverter"/> that unwraps <see cref="Money"/> to its raw
/// decimal amount.  Loading the whole ledger from a single JSONB column is cheap (one row per account)
/// and avoids a join table that would expose internal plumbing.
/// </para>
/// </summary>
internal sealed class PaperAccountConfiguration : IEntityTypeConfiguration<PaperAccount>
{
    public void Configure(EntityTypeBuilder<PaperAccount> builder)
    {
        builder.ToTable("paper_accounts");

        // Primary key — EF sets the auto-property's backing field via PropertyAccessMode.Field.
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("id")
            .IsRequired();

        // Optimistic concurrency via the Postgres xmin system column (plan §7).
        // xmin is a uint Postgres system column (xid type) that auto-increments on every row update.
        // EF reads it back after writes and uses it as the concurrency token on the next command.
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .IsRowVersion()
            .HasColumnName("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Equity: currency numeric(18,2) (plan §7); private setter — use field access.
        builder.Property(a => a.Equity)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("equity")
            .HasConversion(
                money => money.Amount,
                amount => new Money(amount))
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        // Immutable portfolio cap — set only in the public ctor; EF reads the backing field.
        builder.Property(a => a.MaxOpenPortfolioRiskPercent)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("max_open_portfolio_risk_pct")
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        // Reservation ledger: private Dictionary<Guid, Money> field → jsonb.
        // Mapped by field name so EF bypasses the (non-existent) property getter.
        // A custom ValueComparer is required so EF's change tracker detects in-place mutations
        // (Reserve/Release/Remove) on the same Dictionary instance.  The TryGetValue out-param
        // cannot appear in an expression tree so we delegate to a static helper method.
        var ledgerComparer = new ValueComparer<Dictionary<Guid, Money>>(
            (a, b) => LedgerEqual(a, b),
            d => d.Aggregate(0, (hash, kv) => HashCode.Combine(hash, kv.Key, kv.Value.Amount)),
            d => d.ToDictionary(kv => kv.Key, kv => kv.Value));

        builder.Property<Dictionary<Guid, Money>>("_reservedRiskByTrade")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("reserved_risk_by_trade")
            .HasConversion(JsonConverters.ReservationLedgerConverter, ledgerComparer)
            .HasColumnType("jsonb")
            .IsRequired();
    }

    private static bool LedgerEqual(Dictionary<Guid, Money>? a, Dictionary<Guid, Money>? b)
    {
        if (a is null && b is null)
            return true;
        if (a is null || b is null || a.Count != b.Count)
            return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value)
                return false;
        }

        return true;
    }
}
