using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class AddExamSubjectTeacher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamSubjectTeacher",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExamScheduleId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    AdminId = table.Column<int>(type: "int", nullable: false),
                    ClassId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSubjectTeacher", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamSubjectTeacher_Admin_AdminId",
                        column: x => x.AdminId,
                        principalTable: "Admin",
                        principalColumn: "AdminID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamSubjectTeacher_ClassInfo_ClassId",
                        column: x => x.ClassId,
                        principalTable: "ClassInfo",
                        principalColumn: "ClassInfoID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamSubjectTeacher_ExamSchedule_ExamScheduleId",
                        column: x => x.ExamScheduleId,
                        principalTable: "ExamSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamSubjectTeacher_Subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjectTeacher_AdminId",
                table: "ExamSubjectTeacher",
                column: "AdminId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjectTeacher_ClassId",
                table: "ExamSubjectTeacher",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjectTeacher_ExamScheduleId_SubjectId_AdminId_ClassId",
                table: "ExamSubjectTeacher",
                columns: new[] { "ExamScheduleId", "SubjectId", "AdminId", "ClassId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamSubjectTeacher_SubjectId",
                table: "ExamSubjectTeacher",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamSubjectTeacher");
        }
    }
}
