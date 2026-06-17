using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMssqlMirrorTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MirrorInitiatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InitiativeCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    InitiativeName = table.Column<string>(type: "TEXT", nullable: true),
                    ObjectiveCode = table.Column<string>(type: "TEXT", nullable: true),
                    ObjectiveName = table.Column<string>(type: "TEXT", nullable: true),
                    Owners = table.Column<string>(type: "TEXT", nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity = table.Column<decimal>(type: "TEXT", nullable: true),
                    StartDates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndDates = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorInitiatives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MirrorKpis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    KpiCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    KpiName = table.Column<string>(type: "TEXT", nullable: true),
                    ActivationStatus = table.Column<string>(type: "TEXT", nullable: true),
                    KpiType = table.Column<string>(type: "TEXT", nullable: true),
                    ObjectiveCode = table.Column<string>(type: "TEXT", nullable: true),
                    PlrCode = table.Column<string>(type: "TEXT", nullable: true),
                    Division = table.Column<string>(type: "TEXT", nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", nullable: true),
                    UnitDirection = table.Column<string>(type: "TEXT", nullable: true),
                    IndexWeight = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumMaximum = table.Column<decimal>(type: "TEXT", nullable: true),
                    Target2025 = table.Column<string>(type: "TEXT", nullable: true),
                    Target2026 = table.Column<string>(type: "TEXT", nullable: true),
                    Target2027 = table.Column<string>(type: "TEXT", nullable: true),
                    Target2028 = table.Column<string>(type: "TEXT", nullable: true),
                    Target2029 = table.Column<string>(type: "TEXT", nullable: true),
                    Target2030 = table.Column<string>(type: "TEXT", nullable: true),
                    AutomationStatus = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorKpis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MirrorMetadata",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastPushAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RecordCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MirrorObjectives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ObjectiveCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    ObjectiveName = table.Column<string>(type: "TEXT", nullable: true),
                    PlrCode = table.Column<string>(type: "TEXT", nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity = table.Column<decimal>(type: "TEXT", nullable: true),
                    StartDates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndDates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ObjPeriod = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorObjectives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MirrorPillars",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlrCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    PillarName = table.Column<string>(type: "TEXT", nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity = table.Column<decimal>(type: "TEXT", nullable: true),
                    StartDates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndDates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlrPeriods = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorPillars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MirrorProjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: true),
                    InitiativeCode = table.Column<string>(type: "TEXT", nullable: true),
                    PlrCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectType = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectStatus = table.Column<string>(type: "TEXT", nullable: true),
                    BudgetLiquidity = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2025 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2026 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2027 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2028 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2029 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2030 = table.Column<decimal>(type: "TEXT", nullable: true),
                    Liquidity2031 = table.Column<decimal>(type: "TEXT", nullable: true),
                    GacBudget = table.Column<decimal>(type: "TEXT", nullable: true),
                    ProjectSponsor = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectManager = table.Column<string>(type: "TEXT", nullable: true),
                    Division = table.Column<string>(type: "TEXT", nullable: true),
                    ProjectPhase = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MirrorProjects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MirrorInitiatives_InitiativeCode",
                table: "MirrorInitiatives",
                column: "InitiativeCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorInitiatives_ObjectiveCode",
                table: "MirrorInitiatives",
                column: "ObjectiveCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorKpis_KpiCode",
                table: "MirrorKpis",
                column: "KpiCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorKpis_ObjectiveCode",
                table: "MirrorKpis",
                column: "ObjectiveCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorObjectives_ObjectiveCode",
                table: "MirrorObjectives",
                column: "ObjectiveCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorObjectives_PlrCode",
                table: "MirrorObjectives",
                column: "PlrCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorPillars_PlrCode",
                table: "MirrorPillars",
                column: "PlrCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorProjects_InitiativeCode",
                table: "MirrorProjects",
                column: "InitiativeCode");

            migrationBuilder.CreateIndex(
                name: "IX_MirrorProjects_ProjectCode",
                table: "MirrorProjects",
                column: "ProjectCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MirrorInitiatives");

            migrationBuilder.DropTable(
                name: "MirrorKpis");

            migrationBuilder.DropTable(
                name: "MirrorMetadata");

            migrationBuilder.DropTable(
                name: "MirrorObjectives");

            migrationBuilder.DropTable(
                name: "MirrorPillars");

            migrationBuilder.DropTable(
                name: "MirrorProjects");
        }
    }
}
