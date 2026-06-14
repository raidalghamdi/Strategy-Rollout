using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase1Journey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepartmentAccessCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UsedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentAccessCodes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "ModerationAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StrategySessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    AccessCodeUsed = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategySessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContributionPledges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    ElementType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ElementCode = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ContributionKind = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionPledges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionPledges_StrategySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StrategySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentStrategyMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    MapLayoutJson = table.Column<string>(type: "longtext", nullable: true),
                    OpinionsText = table.Column<string>(type: "longtext", nullable: true),
                    WishesText = table.Column<string>(type: "longtext", nullable: true),
                    CommitmentsText = table.Column<string>(type: "longtext", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PdfBlob = table.Column<byte[]>(type: "longblob", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentStrategyMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentStrategyMaps_StrategySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StrategySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    TypedSignature = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionMembers_StrategySessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "StrategySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MapInkAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MapId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetKind = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PngBlob = table.Column<byte[]>(type: "longblob", nullable: true),
                    StrokesJson = table.Column<string>(type: "longtext", nullable: true),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ModerationStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ModeratedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModeratedBy = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    ModerationNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapInkAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MapInkAssets_DepartmentStrategyMaps_MapId",
                        column: x => x.MapId,
                        principalTable: "DepartmentStrategyMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionPledges_SessionId",
                table: "ContributionPledges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentStrategyMaps_SessionId",
                table: "DepartmentStrategyMaps",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_MapInkAssets_MapId",
                table: "MapInkAssets",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionMembers_SessionId",
                table: "SessionMembers",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContributionPledges");

            migrationBuilder.DropTable(
                name: "DepartmentAccessCodes");

            migrationBuilder.DropTable(
                name: "MapInkAssets");

            migrationBuilder.DropTable(
                name: "ModerationAuditLogs");

            migrationBuilder.DropTable(
                name: "SessionMembers");

            migrationBuilder.DropTable(
                name: "DepartmentStrategyMaps");

            migrationBuilder.DropTable(
                name: "StrategySessions");
        }
    }
}
