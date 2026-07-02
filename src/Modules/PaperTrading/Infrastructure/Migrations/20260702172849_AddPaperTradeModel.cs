using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IctTrader.PaperTrading.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddPaperTradeModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "model",
            table: "paper_trades",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Ict2022");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "model",
            table: "paper_trades");
    }
}
