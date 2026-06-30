using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class RefactorScoreModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 清理数据：删除没有关联考试安排的旧成绩记录
            migrationBuilder.Sql("DELETE FROM `Score` WHERE `ExamScheduleId` IS NULL");
            // 清理数据：删除 SubjectTeacher 中无班级的旧记录（数据不完整）
            migrationBuilder.Sql("DELETE FROM `SubjectTeacher` WHERE `ClassId` IS NULL");

            // 注意：数据库中不存在 FK_Score_ExamSchedule_ExamScheduleId 外键，故跳过删除

            // 不再需要删除旧索引（IX_Score_StudentId 和 IX_Score_SubjectId 是FK所需，和新唯一索引不冲突）

            migrationBuilder.AlterColumn<int>(
                name: "ClassId",
                table: "SubjectTeacher",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ExamScheduleId",
                table: "Score",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClassInfoId",
                table: "Score",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradeLevelId",
                table: "Score",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GradeSubject",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GradeLevelId = table.Column<int>(type: "int", nullable: false),
                    SubjectId = table.Column<int>(type: "int", nullable: false),
                    FullScore = table.Column<int>(type: "int", nullable: true),
                    CreateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeSubject", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeSubject_GradeLevel_GradeLevelId",
                        column: x => x.GradeLevelId,
                        principalTable: "GradeLevel",
                        principalColumn: "GradeLevelID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GradeSubject_Subject_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subject",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SubjectTeacher_ClassId",
                table: "SubjectTeacher",
                column: "ClassId");

            migrationBuilder.CreateIndex(
                name: "IX_Score_ClassInfoId",
                table: "Score",
                column: "ClassInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_Score_GradeLevelId",
                table: "Score",
                column: "GradeLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_Score_StudentId_SubjectId_ExamScheduleId",
                table: "Score",
                columns: new[] { "StudentId", "SubjectId", "ExamScheduleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeSubject_GradeLevelId_SubjectId",
                table: "GradeSubject",
                columns: new[] { "GradeLevelId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeSubject_SubjectId",
                table: "GradeSubject",
                column: "SubjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Score_ClassInfo_ClassInfoId",
                table: "Score",
                column: "ClassInfoId",
                principalTable: "ClassInfo",
                principalColumn: "ClassInfoID");

            migrationBuilder.AddForeignKey(
                name: "FK_Score_ExamSchedule_ExamScheduleId",
                table: "Score",
                column: "ExamScheduleId",
                principalTable: "ExamSchedule",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Score_GradeLevel_GradeLevelId",
                table: "Score",
                column: "GradeLevelId",
                principalTable: "GradeLevel",
                principalColumn: "GradeLevelID");

            migrationBuilder.AddForeignKey(
                name: "FK_SubjectTeacher_ClassInfo_ClassId",
                table: "SubjectTeacher",
                column: "ClassId",
                principalTable: "ClassInfo",
                principalColumn: "ClassInfoID",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Score_ClassInfo_ClassInfoId",
                table: "Score");

            migrationBuilder.DropForeignKey(
                name: "FK_Score_ExamSchedule_ExamScheduleId",
                table: "Score");

            migrationBuilder.DropForeignKey(
                name: "FK_Score_GradeLevel_GradeLevelId",
                table: "Score");

            migrationBuilder.DropForeignKey(
                name: "FK_SubjectTeacher_ClassInfo_ClassId",
                table: "SubjectTeacher");

            migrationBuilder.DropTable(
                name: "GradeSubject");

            migrationBuilder.DropIndex(
                name: "IX_SubjectTeacher_ClassId",
                table: "SubjectTeacher");

            migrationBuilder.DropIndex(
                name: "IX_Score_ClassInfoId",
                table: "Score");

            migrationBuilder.DropIndex(
                name: "IX_Score_GradeLevelId",
                table: "Score");

            migrationBuilder.DropIndex(
                name: "IX_Score_StudentId_SubjectId_ExamScheduleId",
                table: "Score");

            migrationBuilder.DropColumn(
                name: "ClassInfoId",
                table: "Score");

            migrationBuilder.DropColumn(
                name: "GradeLevelId",
                table: "Score");

            migrationBuilder.AlterColumn<int>(
                name: "ClassId",
                table: "SubjectTeacher",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "ExamScheduleId",
                table: "Score",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_Score_ExamSchedule_ExamScheduleId",
                table: "Score",
                column: "ExamScheduleId",
                principalTable: "ExamSchedule",
                principalColumn: "Id");
        }
    }
}
