using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase18_NewStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Phase18_OpeningReflections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JourneyCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DepartmentCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ReflectionText = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Phase18_OpeningReflections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Phase18_RoleContributions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JourneyCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DepartmentCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    SelectedInitiativeCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PerceivedImpact = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Phase18_RoleContributions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Phase18_OpeningReflections_SessionId",
                table: "Phase18_OpeningReflections",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Phase18_RoleContributions_SessionId",
                table: "Phase18_RoleContributions",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Phase18_OpeningReflections");

            migrationBuilder.DropTable(
                name: "Phase18_RoleContributions");
        }
    }
}
