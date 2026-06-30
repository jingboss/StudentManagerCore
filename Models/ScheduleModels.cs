using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentManagerCore.Models;

/// <summary>
/// 排课设置 - 配置每周上课天数、每天节数、每节课时间段
/// </summary>
[Table("ScheduleSetting")]
public class ScheduleSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>设置名称</summary>
    [Required, StringLength(50)]
    public string Name { get; set; } = "默认排课";

    /// <summary>每周上课天数 (5 = 周一至周五)</summary>
    public int DaysPerWeek { get; set; } = 5;

    /// <summary>每天总节数</summary>
    public int PeriodsPerDay { get; set; } = 8;

    /// <summary>每节课时长（分钟）</summary>
    public int PeriodDurationMinutes { get; set; } = 45;

    /// <summary>第一节课开始时间（HH:mm）</summary>
    [Required, StringLength(5)]
    public string StartTime { get; set; } = "08:00";

    /// <summary>课间休息（分钟）</summary>
    public int BreakMinutes { get; set; } = 10;

    /// <summary>是否为当前生效设置</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;
}

/// <summary>
/// 每节课的具体时间段
/// </summary>
[Table("SchedulePeriod")]
public class SchedulePeriod
{
    [Key]
    public int Id { get; set; }

    public int SettingId { get; set; }

    /// <summary>第几节（从1开始）</summary>
    public int PeriodNumber { get; set; }

    /// <summary>开始时间 HH:mm</summary>
    [Required, StringLength(5)]
    public string StartTime { get; set; } = "";

    /// <summary>结束时间 HH:mm</summary>
    [Required, StringLength(5)]
    public string EndTime { get; set; } = "";

    [ForeignKey(nameof(SettingId))]
    public ScheduleSetting? Setting { get; set; }
}

/// <summary>
/// 年级作息配置 - 每个年级可独立设置每周上课天数和每天节数
/// </summary>
[Table("GradeScheduleConfig")]
public class GradeScheduleConfig
{
    [Key]
    public int Id { get; set; }

    public int GradeLevelId { get; set; }

    /// <summary>每周上课天数 (5 = 周一至周五)</summary>
    public int DaysPerWeek { get; set; } = 5;

    /// <summary>每天总节数</summary>
    public int PeriodsPerDay { get; set; } = 8;

    /// <summary>是否当前生效</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [ForeignKey(nameof(GradeLevelId))]
    public GradeLevel? GradeLevel { get; set; }
}

/// <summary>
/// 年级每节课的具体时间段
/// </summary>
[Table("GradePeriod")]
public class GradePeriod
{
    [Key]
    public int Id { get; set; }

    public int GradeLevelId { get; set; }

    /// <summary>第几节（从1开始）</summary>
    public int PeriodNumber { get; set; }

    /// <summary>开始时间 HH:mm</summary>
    [Required, StringLength(5)]
    public string StartTime { get; set; } = "";

    /// <summary>结束时间 HH:mm</summary>
    [Required, StringLength(5)]
    public string EndTime { get; set; } = "";

    /// <summary>所属节次分组：早晨/上午/下午/晚修</summary>
    [StringLength(20)]
    public string? SectionName { get; set; }

    [ForeignKey(nameof(GradeLevelId))]
    public GradeLevel? GradeLevel { get; set; }
}

/// <summary>
/// 年级科目周课时配置 - 每个年级每个科目每周上几节课
/// </summary>
[Table("GradeSubjectHour")]
public class GradeSubjectHour
{
    [Key]
    public int Id { get; set; }

    public int GradeLevelId { get; set; }

    public int SubjectId { get; set; }

    /// <summary>每周课时数</summary>
    public int PeriodsPerWeek { get; set; } = 0;

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [ForeignKey(nameof(GradeLevelId))]
    public GradeLevel? GradeLevel { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }
}

/// <summary>
/// 班级课表 - 每个班级每周每天每节课的排课记录
/// </summary>
[Table("ClassSchedule")]
public class ClassSchedule
{
    [Key]
    public int Id { get; set; }

    /// <summary>班级ID (ClassInfo.ClassInfoID)</summary>
    public int ClassId { get; set; }

    /// <summary>学期ID (Semester.Id)</summary>
    public int SemesterId { get; set; }

    /// <summary>星期几：1=周一, 2=周二 ... 5=周五</summary>
    public int DayOfWeek { get; set; }

    /// <summary>第几节（从1开始）</summary>
    public int Period { get; set; }

    /// <summary>科目ID (Subject.Id)</summary>
    public int SubjectId { get; set; }

    /// <summary>任课教师ID (Admin.AdminID)</summary>
    public int TeacherId { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;
    public DateTime? UpdateTime { get; set; }

    [ForeignKey(nameof(ClassId))]
    public ClassInfo? ClassInfo { get; set; }

    [ForeignKey(nameof(SemesterId))]
    public Semester? Semester { get; set; }

    [ForeignKey(nameof(SubjectId))]
    public Subject? Subject { get; set; }

    [ForeignKey(nameof(TeacherId))]
    public Admin? Teacher { get; set; }
}
