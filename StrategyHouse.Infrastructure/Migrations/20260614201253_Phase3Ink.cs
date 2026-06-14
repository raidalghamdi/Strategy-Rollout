using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Ink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MemberId",
                table: "MapInkAssets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MemberId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RespondentName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    AnswersJson = table.Column<string>(type: "longtext", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DeptCodeFilter = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    QuestionType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QuestionAr = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OptionsJson = table.Column<string>(type: "longtext", nullable: false),
                    CorrectIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ExplanationAr = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsApproved = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Surveys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TitleAr = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DescriptionAr = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    OpensAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClosesAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublicToken = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Surveys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SurveyQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QuestionAr = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OptionsJson = table.Column<string>(type: "longtext", nullable: true),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyQuestions_Surveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "Surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SurveyResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RespondentName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    RespondentRole = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DeptCode = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    AnswersJson = table.Column<string>(type: "longtext", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClientFingerprint = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SurveyResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SurveyResponses_Surveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "Surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapInkAssets_MemberId",
                table: "MapInkAssets",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_Scope_IsApproved_IsActive",
                table: "QuizQuestions",
                columns: new[] { "Scope", "IsApproved", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SurveyQuestions_SurveyId",
                table: "SurveyQuestions",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SurveyResponses_SurveyId",
                table: "SurveyResponses",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_PublicToken",
                table: "Surveys",
                column: "PublicToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizAttempts");

            migrationBuilder.DropTable(
                name: "QuizQuestions");

            migrationBuilder.DropTable(
                name: "SurveyQuestions");

            migrationBuilder.DropTable(
                name: "SurveyResponses");

            migrationBuilder.DropTable(
                name: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_MapInkAssets_MemberId",
                table: "MapInkAssets");

            migrationBuilder.DropColumn(
                name: "MemberId",
                table: "MapInkAssets");
        }
    }
}
