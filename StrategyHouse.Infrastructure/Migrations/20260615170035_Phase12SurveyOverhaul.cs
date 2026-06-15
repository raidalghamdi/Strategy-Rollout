using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase12SurveyOverhaul : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MeasurementFormula",
                table: "SurveyQuestions",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MeasurementMetric",
                table: "SurveyQuestions",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionType",
                table: "SurveyQuestions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OpenTextCategoryAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyResponseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyQuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenTextCategoryAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpenTextCategoryAssignments_SurveyQuestions_SurveyQuestionId",
                        column: x => x.SurveyQuestionId,
                        principalTable: "SurveyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OpenTextCategoryAssignments_SurveyResponses_SurveyResponseId",
                        column: x => x.SurveyResponseId,
                        principalTable: "SurveyResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SurveyQuestionCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyQuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyQuestionCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyQuestionCategories_SurveyQuestions_SurveyQuestionId",
                        column: x => x.SurveyQuestionId,
                        principalTable: "SurveyQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenTextCategoryAssignments_SurveyQuestionId",
                table: "OpenTextCategoryAssignments",
                column: "SurveyQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpenTextCategoryAssignments_SurveyResponseId_SurveyQuestionId",
                table: "OpenTextCategoryAssignments",
                columns: new[] { "SurveyResponseId", "SurveyQuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SurveyQuestionCategories_SurveyQuestionId",
                table: "SurveyQuestionCategories",
                column: "SurveyQuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenTextCategoryAssignments");

            migrationBuilder.DropTable(
                name: "SurveyQuestionCategories");

            migrationBuilder.DropColumn(
                name: "MeasurementFormula",
                table: "SurveyQuestions");

            migrationBuilder.DropColumn(
                name: "MeasurementMetric",
                table: "SurveyQuestions");

            migrationBuilder.DropColumn(
                name: "QuestionType",
                table: "SurveyQuestions");
        }
    }
}
