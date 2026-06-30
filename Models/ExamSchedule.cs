using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentManagerCore.Models;

/// <summary>考试安排</summary>
[Table("ExamSchedule")]
public class ExamSchedule
{
    [Key]
    public int Id { get; set; }

    /// <summary>考试名称，如 "2024学年第一学期期中考试"</summary>
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    /// <summary>考试类型：期中/期末/月考/单元测试/模拟考</summary>
    [Required, StringLength(30)]
    public string ExamType { get; set; } = "";

    /// <summary>适用年级（逗号分隔），如 "小学2024级,小学2023级"</summary>
    [StringLength(500)]
    public string? Grades { get; set; }

    /// <summary>考试日期（开始日期）</summary>
    public DateTime ExamDate { get; set; }

    /// <summary>结束日期（可选，不填则默认为考试当天）</summary>
    public DateTime? EndDate { get; set; }

    /// <summary>关联学期</summary>
    public int SemesterId { get; set; }

    /// <summary>状态：未开始/进行中/已结束</summary>
    [StringLength(20)]
    public string Status { get; set; } = "未开始";

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [ForeignKey("SemesterId")]
    public Semester? Semester { get; set; }

    /// <summary>关联的考试科目</summary>
    [InverseProperty("ExamSchedule")]
    public List<ExamSubject> ExamSubjects { get; set; } = new();
}
