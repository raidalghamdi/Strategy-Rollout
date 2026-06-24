using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20_29_RosterEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 20.29 — email-only access to the journey. Members can sign in
            // with just their email (no password, no department code).
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "DepartmentRoster",
                type: "TEXT",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailNormalized",
                table: "DepartmentRoster",
                type: "TEXT",
                maxLength: 320,
                nullable: true);

            // Unique-when-set: SQLite supports filtered indexes via raw SQL only.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_DepartmentRoster_EmailNormalized\" " +
                "ON \"DepartmentRoster\" (\"EmailNormalized\") " +
                "WHERE \"EmailNormalized\" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_DepartmentRoster_EmailNormalized\";");

            migrationBuilder.DropColumn(
                name: "EmailNormalized",
                table: "DepartmentRoster");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "DepartmentRoster");
        }
    }
}
