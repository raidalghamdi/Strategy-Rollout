using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20_35_CategoryKeywordsAndReadyForReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ReadyForReport defaults to TRUE to preserve existing FinalReport behaviour for all
            // pre-existing questions. New OpenText questions can be flipped to FALSE explicitly
            // by the auto-categorizer after fresh responses arrive, forcing analyst review.
            migrationBuilder.AddColumn<bool>(
                name: "ReadyForReport",
                table: "SurveyQuestions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyForReportAt",
                table: "SurveyQuestions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadyForReportByUserId",
                table: "SurveyQuestions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "SurveyQuestionCategories",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            // IsActive defaults to TRUE so pre-existing categories keep appearing in dropdowns
            // and auto-categorisation without manual intervention after the migration runs.
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "SurveyQuestionCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltin",
                table: "SurveyQuestionCategories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // KeywordsJson defaults to an empty JSON array. The Phase 20.35 seeder runs after
            // this migration and populates Q4/Q5/Q7 categories with the keyword lists that used
            // to live in OpenTextAutoCategorizer.cs.
            migrationBuilder.AddColumn<string>(
                name: "KeywordsJson",
                table: "SurveyQuestionCategories",
                type: "longtext",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadyForReport",
                table: "SurveyQuestions");

            migrationBuilder.DropColumn(
                name: "ReadyForReportAt",
                table: "SurveyQuestions");

            migrationBuilder.DropColumn(
                name: "ReadyForReportByUserId",
                table: "SurveyQuestions");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "SurveyQuestionCategories");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "SurveyQuestionCategories");

            migrationBuilder.DropColumn(
                name: "IsBuiltin",
                table: "SurveyQuestionCategories");

            migrationBuilder.DropColumn(
                name: "KeywordsJson",
                table: "SurveyQuestionCategories");
        }
    }
}
