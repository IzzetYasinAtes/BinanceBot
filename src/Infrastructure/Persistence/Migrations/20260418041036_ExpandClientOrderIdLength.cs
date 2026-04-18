using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandClientOrderIdLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ClientOrderId",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(36)",
                oldMaxLength: 36);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ClientOrderId",
                table: "Orders",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);
        }
    }
}
