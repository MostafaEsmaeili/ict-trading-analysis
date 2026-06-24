using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IctTrader.PaperTrading.Infrastructure.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "paper_accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                equity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                max_open_portfolio_risk_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                consecutive_losses = table.Column<int>(type: "integer", nullable: false),
                consecutive_wins = table.Column<int>(type: "integer", nullable: false),
                dip_trough = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                peak_equity = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                reserved_risk_by_trade = table.Column<string>(type: "jsonb", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_paper_accounts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "armed_entries",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                setup = table.Column<string>(type: "jsonb", nullable: false),
                size_lots = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                risk_budget = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                pip_size = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                value_per_pip = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                instrument_class = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                armed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_armed_entries", x => x.id);
                table.ForeignKey(
                    name: "FK_armed_entries_paper_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "paper_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "paper_trades",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                account_id = table.Column<Guid>(type: "uuid", nullable: false),
                symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                style = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                plan = table.Column<string>(type: "jsonb", nullable: false),
                size_lots = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                remaining_size_lots = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                has_scaled_out = table.Column<bool>(type: "boolean", nullable: false),
                risk_budget = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                opened_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                initial_risk_per_unit = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                lifecycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                exit_price = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                closed_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                close_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                realized_r = table.Column<decimal>(type: "numeric(18,8)", nullable: true),
                gross_pnl = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                costs = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                realized_pnl = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                current_stop = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                last_activity_at_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                legs = table.Column<string>(type: "jsonb", nullable: false),
                pip_size = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                value_per_pip = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                value_per_pip_for_position = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_paper_trades", x => x.id);
                table.ForeignKey(
                    name: "FK_paper_trades_paper_accounts_account_id",
                    column: x => x.account_id,
                    principalTable: "paper_accounts",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_armed_entries_account_id_status",
            table: "armed_entries",
            columns: new[] { "account_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_paper_trades_account_id_closed_at",
            table: "paper_trades",
            columns: new[] { "account_id", "closed_at_utc" });

        migrationBuilder.CreateIndex(
            name: "ix_paper_trades_account_id_status",
            table: "paper_trades",
            columns: new[] { "account_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_paper_trades_symbol_status",
            table: "paper_trades",
            columns: new[] { "symbol", "status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "armed_entries");

        migrationBuilder.DropTable(
            name: "paper_trades");

        migrationBuilder.DropTable(
            name: "paper_accounts");
    }
}
