using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IctTrader.PaperTrading.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaperTradeManagedFromUtc : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // The management-eligibility edge (plan §4.1). For an immediate open it equals opened_at_utc; for an
        // armed-triggered open it is the trigger bar's OPEN (opened_at_utc is the trigger bar's CLOSE). Any legacy row
        // predates this column, so back-fill it from opened_at_utc — the prior single-edge behavior — via a non-null
        // default for the ADD, then a one-shot UPDATE so existing rows carry the faithful value rather than MinValue.
        migrationBuilder.AddColumn<System.DateTimeOffset>(
            name: "managed_from_utc",
            table: "paper_trades",
            type: "timestamptz",
            nullable: false,
            defaultValueSql: "now()");

        migrationBuilder.Sql("UPDATE paper_trades SET managed_from_utc = opened_at_utc;");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "managed_from_utc",
            table: "paper_trades");
    }
}
