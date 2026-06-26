using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IctTrader.PaperTrading.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddArmedEntryStackedFartherBound : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "stacked_farther_bound",
            table: "armed_entries",
            type: "numeric(18,8)",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "stacked_farther_bound",
            table: "armed_entries");
    }
}
