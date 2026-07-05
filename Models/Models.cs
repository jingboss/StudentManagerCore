using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentManagerCore.Models;

public class Admin
{
    [Key]
    public int AdminID { get; set; }

    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Password { get; set; } = string.Empty;

    [StringLength(50)]
    public string? RealName { get; set; }

    [StringLength(10)]
    public string? Gender { get; set; }

    [StringLength(20)]
    public string? Nation { get; set; }

    [Column(TypeName = "date")]
    public DateTime? BirthDate { get; set; }

    [StringLength(200)]
    public string? RegisteredDomicile { get; set; }

    [StringLength(50)]
    public string? HighestEducation { get; set; }

    [StringLength(100)]
    public string? CertSubject { get; set; }

    [StringLength(100)]
    public string? CertNumber { get; set; }

    [StringLength(200)]
    public string? CertAuthority { get; set; }

    /// <summary>权限（逗号分隔）：edit_student,delete_student,add_student,edit_basic,edit_phone,edit_idcard,edit_cert</summary>
    [StringLength(200)]
    public string? Permissions { get; set; }

    [StringLength(20)]
    public string? Status { get; set; }

    [Column(TypeName = "varchar(100)")]
    public string? Role { get; set; }

    [StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(18)]
    [NotMapped] // TODO: 数据库暂缺该列，后续通过 ALTER TABLE 添加
    public string? IDCardNumber { get; set; }

    public int? ClassID { get; set; }

    [StringLength(50)]
    public string? ClassName { get; set; }

    [StringLength(50)]
    public string? Grade { get; set; }

    [StringLength(50)]
    public string? Position { get; set; }

    /// <summary>学段：小学/初中</summary>
    [StringLength(10)]
    public string? SchoolType { get; set; }

    /// <summary>末期：毕业班/非毕业班</summary>
    [StringLength(20)]
    public string? EndStage { get; set; }

    /// <summary>钉钉用户唯一标识（用于钉钉扫码登录）</summary>
    [StringLength(100)]
    public string? DingTalkUnionId { get; set; }

    public DateTime? CreateTime { get; set; }

    /// <summary>首次登录是否必须修改密码（通过 NewDatabase 列存储，NotMapped 标记防止列不存在时出错）</summary>
    [NotMapped]
    public bool MustChangePassword { get; set; } = true;

    /// <summary>角色列表（按逗号拆分）</summary>
    [NotMapped]
    public List<string> RoleList =>
        (Role ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>取最高优先级角色（用于JWT认证）</summary>
    [NotMapped]
    public string PrimaryRole
    {
        get
        {
            var roles = RoleList;
            if (roles.Count == 0) return "";
            var priority = new[] { "管理员", "校长", "教务主任", "年级级长", "班主任", "科任教师", "后勤主任" };
            foreach (var p in priority)
                if (roles.Contains(p)) return p;
            return roles[0];
        }
    }

    /// <summary>是否包含某角色</summary>
    public bool HasRole(string role) => RoleList.Contains(role);

    /// <summary>角色Trimmed（兼容旧代码，返回最高优先级角色）</summary>
    [NotMapped]
    public string RoleTrimmed => PrimaryRole;
}

public class Student
{
    [Key]
    public int StudentID { get; set; }

    [StringLength(8, MinimumLength = 8, ErrorMessage = "学号必须为8位")]
    public string? StudentNo { get; set; }

    [StringLength(50)]
    public string? Grade { get; set; }

    [StringLength(50)]
    public string? ClassName { get; set; }

    [StringLength(50)]
    [RegularExpression(@"^[\u4e00-\u9fff]+$", ErrorMessage = "姓名必须为中文")]
    public string? Name { get; set; }

    [StringLength(10)]
    public string? Gender { get; set; }

    [StringLength(18, MinimumLength = 18, ErrorMessage = "身份证号码必须为18位")]
    [RegularExpression(@"^\d{17}[\dXx]$", ErrorMessage = "身份证号码格式不正确，应为18位数字或末位X")]
    public string? IDCardNumber { get; set; }

    [StringLength(20)]
    public string? Nation { get; set; }

    /// <summary>户口所在地（省市）</summary>
    [StringLength(200)]
    public string? HouseholdCity { get; set; }

    /// <summary>户口簿中首页家庭地址</summary>
    [StringLength(200)]
    public string? HouseholdAddress { get; set; }

    /// <summary>户口性质：农业/非农业/统一居民户口</summary>
    [StringLength(20)]
    public string? HouseholdType { get; set; }

    /// <summary>是否非本地户籍：是/否</summary>
    [StringLength(10)]
    public string? IsNonLocalHousehold { get; set; }

    /// <summary>是否随迁子女：是/否</summary>
    [StringLength(10)]
    public string? IsMigrantChild { get; set; }

    /// <summary>是否进城务工人员子女：是/否</summary>
    [StringLength(10)]
    public string? IsMigrantWorkerChild { get; set; }

    /// <summary>现居住家庭地址</summary>
    [StringLength(200)]
    public string? CurrentResidence { get; set; }

    [StringLength(50)]
    public string? FatherName { get; set; }

    [StringLength(20)]
    [RegularExpression(@"^$|^1[3-9]\d{9}$", ErrorMessage = "手机号格式不正确，应为11位手机号")]
    public string? FatherPhone { get; set; }

    [StringLength(50)]
    public string? MotherName { get; set; }

    [StringLength(20)]
    [RegularExpression(@"^$|^1[3-9]\d{9}$", ErrorMessage = "手机号格式不正确，应为11位手机号")]
    public string? MotherPhone { get; set; }

    public int? ClassID { get; set; }

    [StringLength(50)]
    public string? Status { get; set; }

    [StringLength(500)]
    public string? Remark { get; set; }

    public DateTime? CreateTime { get; set; }

    public DateTime? UpdateTime { get; set; }

    public DateTime? TransferOutTime { get; set; }
}

public class SiteConfig
{
    [Key]
    [StringLength(100)]
    public string ConfigKey { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ConfigValue { get; set; }
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

// View models for login
public class LoginViewModel
{
    [Required(ErrorMessage = "请输入用户名")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "请输入密码")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    [Display(Name = "验证码")]
    public string? CaptchaCode { get; set; }
}

// 公告
public class Announcement
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = "";

    /// <summary>目标角色：全员 / 班主任 / 科任老师 / 年级级长 / 教务主任 / 校长</summary>
    [StringLength(20)]
    public string TargetRole { get; set; } = "";

    [Required]
    public string Content { get; set; } = "";

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [StringLength(50)]
    public string? CreatedBy { get; set; }
}

// 公告已读记录
public class AnnouncementRead
{
    [Key]
    public int Id { get; set; }

    public int AnnouncementId { get; set; }

    [StringLength(20)]
    public string? TeacherPhone { get; set; }

    public DateTime ReadTime { get; set; } = DateTime.Now;
}

// 操作日志
public class OperationLog
{
    [Key]
    public int Id { get; set; }

    [StringLength(50)]
    public string? OperatorName { get; set; }       // 操作人姓名

    [StringLength(20)]
    public string? OperatorRole { get; set; }       // 操作人角色

    [StringLength(30)]
    public string? ActionType { get; set; }         // 操作类型：添加/编辑/删除/恢复/彻底删除/导入

    [StringLength(20)]
    public string? TargetNo { get; set; }           // 目标学号

    [StringLength(50)]
    public string? TargetName { get; set; }         // 目标学生姓名

    public string? Detail { get; set; }             // 详细信息

    /// <summary>操作来源IP地址</summary>
    [StringLength(50)]
    public string? IpAddress { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;
}

// 学年
public class AcademicYear
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(20)]
    public string? YearName { get; set; }          // 如 "2025-2026"

    public bool IsCurrent { get; set; } = false;   // 是否为当前学年

    public DateTime CreateTime { get; set; } = DateTime.Now;
}

// 学期
public class Semester
{
    [Key]
    public int Id { get; set; }

    public int AcademicYearId { get; set; }

    [Required, StringLength(20)]
    public string? SemesterName { get; set; }       // "上学期" / "下学期"

    public bool IsCurrent { get; set; } = false;    // 是否为当前学期

    [ForeignKey("AcademicYearId")]
    public AcademicYear? AcademicYear { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;
}

// 科目
public class Subject
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string? Name { get; set; }               // 科目名称：语文、数学...

    [StringLength(50)]
    public string? Grade { get; set; }              // 适用年级（null=全部年级）

    public int SortOrder { get; set; } = 0;          // 排序

    public int FullScore { get; set; } = 100;         // 满分（默认100）

    public DateTime CreateTime { get; set; } = DateTime.Now;
}

// 考试成绩
public class Score
{
    [Key]
    public int Id { get; set; }

    public int StudentId { get; set; }              // 关联学生ID

    public int SubjectId { get; set; }              // 关联科目ID

    [Column(TypeName = "decimal(5,1)")]
    public decimal ScoreValue { get; set; }          // 分数

    [StringLength(30)]
    public string? ExamType { get; set; }            // 考试类型：期中/期末/月考

    public DateTime ExamDate { get; set; }           // 考试日期

    /// <summary>关联考试安排</summary>
    [Required]
    public int ExamScheduleId { get; set; }

    /// <summary>考试时学生所属年级ID（快照）</summary>
    public int? GradeLevelId { get; set; }

    /// <summary>考试时学生所属班级ID（快照）</summary>
    public int? ClassInfoId { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;

    [ForeignKey("StudentId")]
    public Student? Student { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }

    [ForeignKey("ExamScheduleId")]
    public ExamSchedule? ExamSchedule { get; set; }

    [ForeignKey("GradeLevelId")]
    public GradeLevel? GradeLevel { get; set; }

    [ForeignKey("ClassInfoId")]
    public ClassInfo? ClassInfo { get; set; }
}

// 科目-教师关联（用于在线输分权限）
public class SubjectTeacher
{
    [Key]
    public int Id { get; set; }

    public int SubjectId { get; set; }

    public int AdminId { get; set; }

    /// <summary>任教班级</summary>
    public int ClassId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }

    [ForeignKey("AdminId")]
    public Admin? Admin { get; set; }

    [ForeignKey("ClassId")]
    public ClassInfo? ClassInfo { get; set; }
}

// 科目-班级关联
public class SubjectClass
{
    [Key]
    public int Id { get; set; }

    public int SubjectId { get; set; }

    public int ClassId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }
}

// 考试安排-科目关联
public class ExamSubject
{
    [Key]
    public int Id { get; set; }

    public int ExamScheduleId { get; set; }

    public int SubjectId { get; set; }

    /// <summary>该科目在该次考试的满分（null则使用Subject默认值）</summary>
    public int? FullScore { get; set; }

    /// <summary>该科目考试开始时间</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>该科目考试结束时间</summary>
    public DateTime? EndTime { get; set; }

    [ForeignKey("ExamScheduleId")]
    public ExamSchedule? ExamSchedule { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }
}

/// <summary>考场安排</summary>
public class ExamRoom
{
    [Key]
    public int Id { get; set; }

    public int ExamScheduleId { get; set; }

    [StringLength(50)]
    public string Grade { get; set; } = "";

    /// <summary>安排模式：Shuffle(全年级打乱) / InClass(原班考试)</summary>
    [StringLength(20)]
    public string ArrangeMode { get; set; } = "";

    /// <summary>考场名称</summary>
    [StringLength(100)]
    public string RoomName { get; set; } = "";

    /// <summary>座位数</summary>
    public int SeatCount { get; set; }

    public DateTime CreateTime { get; set; }

    [ForeignKey("ExamScheduleId")]
    public ExamSchedule? ExamSchedule { get; set; }

    public List<ExamRoomStudent>? Students { get; set; }
}

/// <summary>考场学生分配</summary>
public class ExamRoomStudent
{
    [Key]
    public int Id { get; set; }

    public int ExamRoomId { get; set; }

    public int StudentId { get; set; }

    /// <summary>座位号（每个考场从1开始）</summary>
    public int SeatNumber { get; set; }

    [ForeignKey("ExamRoomId")]
    public ExamRoom? ExamRoom { get; set; }

    [ForeignKey("StudentId")]
    public Student? Student { get; set; }
}

// 考试安排 - 科任教师分配
public class ExamSubjectTeacher
{
    [Key]
    public int Id { get; set; }

    public int ExamScheduleId { get; set; }

    public int SubjectId { get; set; }

    public int AdminId { get; set; }

    public int ClassId { get; set; }

    [ForeignKey("ExamScheduleId")]
    public ExamSchedule? ExamSchedule { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }

    [ForeignKey("AdminId")]
    public Admin? Admin { get; set; }

    [ForeignKey("ClassId")]
    public ClassInfo? ClassInfo { get; set; }
}

/// <summary>教职工-所教科目关联</summary>
public class TeacherSubject
{
    [Key]
    public int Id { get; set; }

    public int AdminId { get; set; }

    public int SubjectId { get; set; }

    [ForeignKey("AdminId")]
    public Admin? Admin { get; set; }

    [ForeignKey("SubjectId")]
    public Subject? Subject { get; set; }
}

public class AiAnalysisResult
{
    [Key]
    public int Id { get; set; }

    public int ExamScheduleId { get; set; }

    public int StudentId { get; set; }

    [Column(TypeName = "longtext")]
    public string AnalysisResult { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// AI 班级分析结果缓存
/// </summary>
public class AiClassAnalysisResult
{
    [Key]
    public int Id { get; set; }

    public int ExamScheduleId { get; set; }

    public int ClassInfoId { get; set; }

    public int GradeLevelId { get; set; }

    [Column(TypeName = "longtext")]
    public string AnalysisResult { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>维修申请</summary>
public class RepairRequest
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>故障描述</summary>
    public string? Description { get; set; }

    /// <summary>故障位置</summary>
    [StringLength(200)]
    public string? Location { get; set; }

    /// <summary>联系电话</summary>
    [StringLength(20)]
    public string? ContactPhone { get; set; }

    /// <summary>状态：待处理/处理中/已完成/已关闭</summary>
    [StringLength(20)]
    [Required]
    public string Status { get; set; } = "待处理";

    public DateTime CreateTime { get; set; }

    /// <summary>期望维修时间</summary>
    public DateTime? PreferredTime { get; set; }

    /// <summary>申报人 AdminID</summary>
    public int CreatedBy { get; set; }

    /// <summary>申报人姓名</summary>
    [StringLength(50)]
    public string? CreatorName { get; set; }

    /// <summary>处理时间</summary>
    public DateTime? ProcessTime { get; set; }

    /// <summary>处理人 AdminID</summary>
    public int? ProcessedBy { get; set; }

    /// <summary>处理人姓名</summary>
    [StringLength(50)]
    public string? ProcessorName { get; set; }

    /// <summary>处理备注</summary>
    public string? Remark { get; set; }
}

/// <summary>
/// AI 科目分析结果缓存
/// </summary>
public class AiSubjectAnalysisResult
{
    [Key]
    public int Id { get; set; }

    public int ExamScheduleId { get; set; }

    public int ClassInfoId { get; set; }

    public int SubjectId { get; set; }

    public int GradeLevelId { get; set; }

    [Column(TypeName = "longtext")]
    public string AnalysisResult { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
/// <summary>角色权限配置</summary>
public class RolePermission
{
    [Key]
    [StringLength(50)]
    public string Role { get; set; } = "";

    [StringLength(500)]
    public string? Permissions { get; set; }

    public string? Description { get; set; }
}
/// <summary>角色权限更新 DTO</summary>
public class RolePermissionUpdate
{
    public string Role { get; set; } = "";
    public bool StudentView { get; set; }
    public bool StudentAdd { get; set; }
    public bool StudentEdit { get; set; }
    public bool StudentDelete { get; set; }
    public bool TeacherView { get; set; }
    public bool TeacherAdd { get; set; }
    public bool TeacherEdit { get; set; }
    public bool TeacherDelete { get; set; }
    public bool SubjectManage { get; set; }
    public bool ExamAdd { get; set; }
    public bool ExamEdit { get; set; }
    public bool ExamDelete { get; set; }
    public bool ScoreInput { get; set; }
    public bool ScoreView { get; set; }
    public bool ScoreImport { get; set; }
    public bool AiSettings { get; set; }
    public bool GradeAdd { get; set; }
    public bool GradeEdit { get; set; }
    public bool GradeDelete { get; set; }
    public bool GradeSetTeacher { get; set; }
}
/// <summary>角色权限视图模型</summary>
public class RolePermissionViewModel
{
    public string Role { get; set; } = "";
    public string Permissions { get; set; } = "";
    public string? Description { get; set; }
}
