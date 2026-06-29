using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IctTrader.MarketData.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddCandles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "candles",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                timeframe = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                open_time_utc = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                open = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                high = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                low = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                close = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                volume = table.Column<decimal>(type: "numeric(18,8)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_candles", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "uq_candles_symbol_timeframe_open_time",
            table: "candles",
            columns: new[] { "symbol", "timeframe", "open_time_utc" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "candles");
    }
}
