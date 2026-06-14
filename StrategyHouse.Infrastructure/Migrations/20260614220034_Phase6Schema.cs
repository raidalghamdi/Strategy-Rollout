using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase6Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatbotConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AskedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Answer = table.Column<string>(type: "longtext", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MatchedIntent = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ResultCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatbotConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DepartmentRoster",
                columns: table => new
                {
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    IsDefaultAttending = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentRoster", x => x.MemberId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatbotConversations_AskedAt",
                table: "ChatbotConversations",
                column: "AskedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentRoster_DeptCode_IsActive",
                table: "DepartmentRoster",
                columns: new[] { "DeptCode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatbotConversations");

            migrationBuilder.DropTable(
                name: "DepartmentRoster");
        }
    }
}
