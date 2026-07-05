using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StudentManagerCore.Migrations
{
    /// <inheritdoc />
    public partial class EnsureExamSubjectUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. 先清理重复数据，只保留每个 (ExamScheduleId, SubjectId) 组合中 Id 最小的那条
            migrationBuilder.Sql(@"
                DELETE e1 FROM ExamSubject e1
                INNER JOIN ExamSubject e2
                WHERE e1.Id > e2.Id
                  AND e1.ExamScheduleId = e2.ExamScheduleId
                  AND e1.SubjectId = e2.SubjectId
            ");

            // 2. 添加唯一索引（如果不存在）
            migrationBuilder.Sql(@"
                SET @exist := (SELECT COUNT(*) FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                    AND TABLE_NAME = 'ExamSubject'
                    AND INDEX_NAME = 'IX_ExamSubject_ExamScheduleId_SubjectId');
                SET @sql := IF(@exist = 0,
                    'CREATE UNIQUE INDEX IX_ExamSubject_ExamScheduleId_SubjectId ON ExamSubject (ExamScheduleId, SubjectId)',
                    'SELECT 1');
                PREPARE stmt FROM @sql;
                EXECUTE stmt;
                DEALLOCATE PREPARE stmt;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IX_ExamSubject_ExamScheduleId_SubjectId ON ExamSubject");
        }
    }
}
