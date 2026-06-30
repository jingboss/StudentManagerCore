using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentManagerCore.Models;

[Table("GradeLevel")]
public class GradeLevel
{
    [Key]
    public int GradeLevelID { get; set; }

    /// <summary>入学年份，如2025</summary>
    public int EntryYear { get; set; }

    /// <summary>学段：小学 / 初中</summary>
    [Required]
    [MaxLength(10)]
    public string SchoolType { get; set; } = "";

    public DateTime CreateTime { get; set; } = DateTime.Now;

    /// <summary>内部标识名，"小学2025级" 格式（用于数据索引键值）</summary>
    [NotMapped]
    public string DisplayName => $"{SchoolType}{EntryYear}级";

    /// <summary>根据当前年份计算当前年级名称</summary>
    [NotMapped]
    public string CurrentGradeName
    {
        get
        {
            int currentYear = DateTime.Now.Year;
            int offset = currentYear - EntryYear; // 0=入学年, 1=第二年...

            if (SchoolType == "小学")
            {
                if (offset < 0) return "未入学";
                if (offset >= 6) return "已毕业(小学)";
                var gradeNames = new[] { "一年级", "二年级", "三年级", "四年级", "五年级", "六年级" };
                return gradeNames[offset];
            }
            else if (SchoolType == "初中")
            {
                if (offset < 0) return "未入学";
                if (offset >= 3) return "已毕业(初中)";
                var gradeNames = new[] { "七年级", "八年级", "九年级" };
                return gradeNames[offset];
            }

            return "未知";
        }
    }

    public ICollection<ClassInfo>? Classes { get; set; }
}

[Table("ClassInfo")]
public class ClassInfo
{
    [Key]
    public int ClassInfoID { get; set; }

    public int GradeLevelID { get; set; }

    /// <summary>班级名称，如 "一班"、"二班"</summary>
    [Required]
    [MaxLength(20)]
    public string ClassName { get; set; } = "";

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [ForeignKey(nameof(GradeLevelID))]
    public GradeLevel? GradeLevel { get; set; }
}

/// <summary>年级开设科目配置（年级考什么科目）</summary>
[Table("GradeSubject")]
public class GradeSubject
{
    [Key]
    public int Id { get; set; }

    /// <summary>年级ID</summary>
    public int GradeLevelId { get; set; }

    /// <summary>科目ID</summary>
    public int SubjectId { get; set; }

    /// <summary>该科目在该年级的满分（可覆盖Subject默认值）</summary>
    public int? FullScore { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [ForeignKey(nameof(GradeLevelId))]
    public GradeLevel? GradeLevel { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }
}
