using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class AddExamRoom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamRoom",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExamScheduleId = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ArrangeMode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoomName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SeatCount = table.Column<int>(type: "int", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamRoom", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamRoom_ExamSchedule_ExamScheduleId",
                        column: x => x.ExamScheduleId,
                        principalTable: "ExamSchedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExamRoomStudent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ExamRoomId = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    SeatNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamRoomStudent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamRoomStudent_ExamRoom_ExamRoomId",
                        column: x => x.ExamRoomId,
                        principalTable: "ExamRoom",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamRoomStudent_Student_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Student",
                        principalColumn: "StudentID",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ExamRoom_ExamScheduleId",
                table: "ExamRoom",
                column: "ExamScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamRoomStudent_ExamRoomId",
                table: "ExamRoomStudent",
                column: "ExamRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamRoomStudent_StudentId",
                table: "ExamRoomStudent",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamRoomStudent");

            migrationBuilder.DropTable(
                name: "ExamRoom");
        }
    }
}
