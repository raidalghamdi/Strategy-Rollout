using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTypedTextToSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TypedText",
                table: "MapInkAssets",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TypedText",
                table: "MapInkAssets");
        }
    }
}
