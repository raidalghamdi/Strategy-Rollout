using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20_SectorsAndUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "StrategySessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JourneyScopeKey",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLoginAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JourneyAuditLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DetailsJson = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyAuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JourneyStageResets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StagesResetCsv = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ResetBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ResetAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JourneyStageResets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StrategySessions_OwnerUserId",
                table: "StrategySessions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyAuditLog_ActionType",
                table: "JourneyAuditLog",
                column: "ActionType");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyAuditLog_CreatedAt",
                table: "JourneyAuditLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_JourneyStageResets_DeptCode",
                table: "JourneyStageResets",
                column: "DeptCode");

            // Phase 20 — StrategySessions.OwnerUserId FK to AspNetUsers (no cascade).
            // NOTE: the Projects/KPIs Department_Code relationship is intentionally NOT
            // migrated here. Those columns already exist and are populated in the
            // production DB; EF only needs the relationship for JOINs (configured in
            // OnModelCreating without HasIndex), so adding an index/FK would force a
            // SQLite table rebuild on a live DB — exactly what the Phase 20 backward-
            // compatibility constraint forbids.
            migrationBuilder.AddForeignKey(
                name: "FK_StrategySessions_AspNetUsers_OwnerUserId",
                table: "StrategySessions",
                column: "OwnerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StrategySessions_AspNetUsers_OwnerUserId",
                table: "StrategySessions");

            migrationBuilder.DropTable(
                name: "JourneyAuditLog");

            migrationBuilder.DropTable(
                name: "JourneyStageResets");

            migrationBuilder.DropIndex(
                name: "IX_StrategySessions_OwnerUserId",
                table: "StrategySessions");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "StrategySessions");

            migrationBuilder.DropColumn(
                name: "JourneyScopeKey",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                table: "AspNetUsers");
        }
    }
}
