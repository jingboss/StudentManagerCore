using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminSchoolType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SchoolType",
                table: "Admin",
                type: "varchar(10)",
                maxLength: 10,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchoolType",
                table: "Admin");
        }
    }
}
