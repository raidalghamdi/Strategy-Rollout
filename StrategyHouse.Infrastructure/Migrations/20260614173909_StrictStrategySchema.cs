using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StrategyHouse.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StrictStrategySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullNameAr = table.Column<string>(type: "TEXT", nullable: false),
                    AppRole = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Dept_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Name_Ar = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Name_En = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Parent_Sector = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Level = table.Column<int>(type: "INTEGER", nullable: true),
                    Is_Active = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Dept_Code);
                });

            migrationBuilder.CreateTable(
                name: "Pillars",
                columns: table => new
                {
                    PLR_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    PILLAR_Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Start_Dates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    End_Dates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PLR_Periods = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pillars", x => x.PLR_Code);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Objectives",
                columns: table => new
                {
                    Objective_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Objective_Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    PLR_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Start_Dates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    End_Dates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Obj_Period = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Objectives", x => x.Objective_Code);
                    table.ForeignKey(
                        name: "FK_Objectives_Pillars_PLR_Code",
                        column: x => x.PLR_Code,
                        principalTable: "Pillars",
                        principalColumn: "PLR_Code");
                });

            migrationBuilder.CreateTable(
                name: "Initiatives",
                columns: table => new
                {
                    Initiative_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Initiative_Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Objective_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Objective_Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Owners = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Start_Dates = table.Column<DateTime>(type: "TEXT", nullable: true),
                    End_Dates = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Initiatives", x => x.Initiative_Code);
                    table.ForeignKey(
                        name: "FK_Initiatives_Objectives_Objective_Code",
                        column: x => x.Objective_Code,
                        principalTable: "Objectives",
                        principalColumn: "Objective_Code");
                });

            migrationBuilder.CreateTable(
                name: "KPIs",
                columns: table => new
                {
                    KPI_Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    KPI_Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Activation_Status = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    KPI_Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Objective_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    PLR_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Division = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Department_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Index_Weight = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    Minimum = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Maximum = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Target_2025 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Target_2026 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Target_2027 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Target_2028 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Target_2029 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Target_2030 = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Automation_Status = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KPIs", x => x.KPI_Code);
                    table.ForeignKey(
                        name: "FK_KPIs_Objectives_Objective_Code",
                        column: x => x.Objective_Code,
                        principalTable: "Objectives",
                        principalColumn: "Objective_Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KPIs_Pillars_PLR_Code",
                        column: x => x.PLR_Code,
                        principalTable: "Pillars",
                        principalColumn: "PLR_Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Project_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    Project_Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Initiative_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    PLR_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Project_Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Project_Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2025 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2026 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2027 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2028 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2029 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2030 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Liquidity_2031 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GAC_Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Project_Sponsor = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Project_Manager = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Division = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Department_Code = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Project_Phase = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Project_Code);
                    table.ForeignKey(
                        name: "FK_Projects_Initiatives_Initiative_Code",
                        column: x => x.Initiative_Code,
                        principalTable: "Initiatives",
                        principalColumn: "Initiative_Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_Pillars_PLR_Code",
                        column: x => x.PLR_Code,
                        principalTable: "Pillars",
                        principalColumn: "PLR_Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_Objective_Code",
                table: "Initiatives",
                column: "Objective_Code");

            migrationBuilder.CreateIndex(
                name: "IX_KPIs_Objective_Code",
                table: "KPIs",
                column: "Objective_Code");

            migrationBuilder.CreateIndex(
                name: "IX_KPIs_PLR_Code",
                table: "KPIs",
                column: "PLR_Code");

            migrationBuilder.CreateIndex(
                name: "IX_Objectives_PLR_Code",
                table: "Objectives",
                column: "PLR_Code");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Initiative_Code",
                table: "Projects",
                column: "Initiative_Code");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_PLR_Code",
                table: "Projects",
                column: "PLR_Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "KPIs");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Initiatives");

            migrationBuilder.DropTable(
                name: "Objectives");

            migrationBuilder.DropTable(
                name: "Pillars");
        }
    }
}
