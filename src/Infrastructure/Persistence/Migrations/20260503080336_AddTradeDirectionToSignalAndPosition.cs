using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeDirectionToSignalAndPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Side",
                table: "Positions",
                newName: "Direction");

            migrationBuilder.RenameColumn(
                name: "PeakMarkPrice",
                table: "Positions",
                newName: "ExtremeMarkPrice");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ExtremeMarkPrice",
                table: "Positions",
                newName: "PeakMarkPrice");

            migrationBuilder.RenameColumn(
                name: "Direction",
                table: "Positions",
                newName: "Side");
        }
    }
}
