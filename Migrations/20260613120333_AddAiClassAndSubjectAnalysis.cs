using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAiClassAndSubjectAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiClassAnalysisResult",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExamScheduleId = table.Column<int>(type: "int", nullable: false),
                    ClassInfoId = table.Column<int>(type: "int", nullable: false),
                    GradeLevelId = table.Column<int>(type: "int", nullable: false),
                    AnalysisResult = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiClassAnalysisResult", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiSubjectAnalysisResult",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExamScheduleId = table.Column<int>(type: "int", nullable: false),
                    ClassInfoId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    GradeLevelId = table.Column<int>(type: "int", nullable: false),
                    AnalysisResult = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSubjectAnalysisResult", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AiClassAnalysisResult_ExamScheduleId_ClassInfoId",
                table: "AiClassAnalysisResult",
                columns: new[] { "ExamScheduleId", "ClassInfoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiSubjectAnalysisResult_ExamScheduleId_ClassInfoId_SubjectId",
                table: "AiSubjectAnalysisResult",
                columns: new[] { "ExamScheduleId", "ClassInfoId", "SubjectId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiClassAnalysisResult");

            migrationBuilder.DropTable(
                name: "AiSubjectAnalysisResult");
        }
    }
}
