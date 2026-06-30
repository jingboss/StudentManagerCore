using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class AddRepairRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassSchedule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClassId = table.Column<int>(type: "int", nullable: false),
                    SemesterId = table.Column<int>(type: "int", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    TeacherId = table.Column<int>(type: "int", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdateTime = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassSchedule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClassSchedule_Admin_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Admin",
                        principalColumn: "AdminID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassSchedule_ClassInfo_ClassId",
                        column: x => x.ClassId,
                        principalTable: "ClassInfo",
                        principalColumn: "ClassInfoID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassSchedule_Semester_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semester",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClassSchedule_Subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ScheduleSetting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DaysPerWeek = table.Column<int>(type: "int", nullable: false),
                    PeriodsPerDay = table.Column<int>(type: "int", nullable: false),
                    PeriodDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BreakMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSetting", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SchedulePeriod",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SettingId = table.Column<int>(type: "int", nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EndTime = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulePeriod", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulePeriod_ScheduleSetting_SettingId",
                        column: x => x.SettingId,
                        principalTable: "ScheduleSetting",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedule_ClassId_SemesterId_DayOfWeek_Period",
                table: "ClassSchedule",
                columns: new[] { "ClassId", "SemesterId", "DayOfWeek", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedule_SemesterId",
                table: "ClassSchedule",
                column: "SemesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedule_SubjectId",
                table: "ClassSchedule",
                column: "SubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ClassSchedule_TeacherId_SemesterId_DayOfWeek_Period",
                table: "ClassSchedule",
                columns: new[] { "TeacherId", "SemesterId", "DayOfWeek", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulePeriod_SettingId_PeriodNumber",
                table: "SchedulePeriod",
                columns: new[] { "SettingId", "PeriodNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassSchedule");

            migrationBuilder.DropTable(
                name: "SchedulePeriod");

            migrationBuilder.DropTable(
                name: "ScheduleSetting");
        }
    }
}
