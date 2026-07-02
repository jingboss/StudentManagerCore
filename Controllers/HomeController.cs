using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;
using System.Text.Json;

namespace StudentManagerCore.Controllers;

[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        Admin? teacher = null;
        if (int.TryParse(adminIdStr, out int adminId))
        {
            teacher = await _db.Admins.FindAsync(adminId);
        }

        var trimmedRole = role.Trim();
        bool isTeacher = trimmedRole != "管理员";

        if (isTeacher)
        {
            return await BuildTeacherDashboard(teacher, trimmedRole);
        }

        // 管理员视图
        var studentCountAll = await _db.Students.CountAsync();
        var teacherCount = await _db.Admins.CountAsync(a => a.Role != null && a.Role.Contains("班主任")
            && (a.Status == null || a.Status != "已删除"));
        var adminCount = await _db.Admins.CountAsync(a => a.Role != null && a.Role.Contains("管理员")
            && (a.Status == null || a.Status != "已删除"));
        var nonBzrCount = await _db.Admins.CountAsync(a => a.Role != null && !a.Role.Contains("班主任")
            && !a.Role.Contains("管理员")
            && (a.Status == null || a.Status != "已删除"));

        ViewBag.StudentCount = studentCountAll;
        ViewBag.TeacherCount = teacherCount;
        ViewBag.AdminCount = adminCount;
        ViewBag.NonBzrCount = nonBzrCount;
        ViewBag.MaleCount = await _db.Students.CountAsync(s => s.Gender == "男");
        ViewBag.FemaleCount = await _db.Students.CountAsync(s => s.Gender == "女");
        ViewBag.ActiveCount = await _db.Students.CountAsync(s => s.Status == "在读");
        ViewBag.DeletedCount = await _db.Students.CountAsync(s => s.Status == "已删除");
        ViewBag.GraduatedCount = await _db.Students.CountAsync(s => s.Status == "已毕业");
        ViewBag.ClassCount = await _db.ClassInfos.CountAsync();
        ViewBag.GradeCount = await _db.GradeLevels.CountAsync();

        // 各年级人数分布（显示所有年级，含0人年级）
        var allGradeLevels = await _db.GradeLevels.ToListAsync();
        var gradeNames = allGradeLevels
            .Select(g => g.CurrentGradeName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct()
            .ToList();
        var gradeOrder = new Dictionary<string, int>
        {
            {"一年级", 1}, {"二年级", 2}, {"三年级", 3},
            {"四年级", 4}, {"五年级", 5}, {"六年级", 6},
            {"七年级", 7}, {"八年级", 8}, {"九年级", 9}
        };
        // 按年级规则排序，不在字典中的排最后
        gradeNames = gradeNames
            .OrderBy(n => gradeOrder.TryGetValue(n ?? "", out var o) ? o : 99)
            .ToList()!;

        var studentCountByGrade = await _db.Students
            .Where(s => s.Status == "在读")
            .GroupBy(s => s.Grade)
            .Select(g => new { Grade = g.Key, Count = g.Count() })
            .ToListAsync();
        var countLookup = studentCountByGrade.ToDictionary(g => g.Grade ?? "", g => g.Count);
        var labels = gradeNames.Select(n => n ?? "").ToList();
        var data = gradeNames.Select(n => countLookup.GetValueOrDefault(n ?? "", 0)).ToList();

        ViewBag.GradeLabels = JsonSerializer.Serialize(labels);
        ViewBag.GradeData = JsonSerializer.Serialize(data);

        // 性别分布
        ViewBag.GenderLabels = JsonSerializer.Serialize(new[] { "男", "女" });
        ViewBag.GenderData = JsonSerializer.Serialize(new[] { ViewBag.MaleCount, ViewBag.FemaleCount });

        // 学生状态分布
        var statusCounts = await _db.Students
            .GroupBy(s => s.Status ?? "未知")
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        ViewBag.StatusLabels = JsonSerializer.Serialize(statusCounts.Select(s => s.Status));
        ViewBag.StatusData = JsonSerializer.Serialize(statusCounts.Select(s => s.Count));

        // 最近操作日志
        var recentLogs = await _db.OperationLogs
            .OrderByDescending(l => l.CreateTime)
            .Take(10)
            .ToListAsync();
        ViewBag.RecentLogs = recentLogs;

        // ====== 管理员待办事项 ======
        ViewBag.PendingRepairCount = await _db.RepairRequests.CountAsync(r => r.Status == "待处理");

        return View();
    }

    private async Task<IActionResult> BuildTeacherDashboard(Admin? teacher, string role)
    {
        ViewBag.TeacherName = teacher?.RealName ?? "";
        ViewBag.RoleDisplay = role;
        ViewBag.MyClass = null;
        ViewBag.StudentCount = 0;

        // ====== 班级信息（班主任） ======
        if (teacher?.ClassID != null)
        {
            var classInfo = await _db.ClassInfos
                .Include(c => c.GradeLevel)
                .FirstOrDefaultAsync(c => c.ClassInfoID == teacher.ClassID);
            ViewBag.MyClass = classInfo;

            if (classInfo != null)
            {
                var gradeName = classInfo.GradeLevel?.CurrentGradeName ?? "";
                var className = classInfo.ClassName ?? "";
                var students = _db.Students.Where(s => s.Grade == gradeName && s.ClassName == className);

                ViewBag.StudentCount = await students.CountAsync();
                ViewBag.MaleCount = await students.CountAsync(s => s.Gender == "男");
                ViewBag.FemaleCount = await students.CountAsync(s => s.Gender == "女");
                ViewBag.ActiveCount = await students.CountAsync(s => s.Status == "在读");

                // 性别分布
                ViewBag.GenderLabels = JsonSerializer.Serialize(new[] { "男", "女" });
                ViewBag.GenderData = JsonSerializer.Serialize(new[] {
                    ViewBag.MaleCount, ViewBag.FemaleCount
                });
            }
        }

        // ====== 教学科目（SubjectTeacher 授权） ======
        var adminId = teacher?.AdminID ?? 0;
        var subjectTeacherRecords = await _db.SubjectTeachers
            .Where(st => st.AdminId == adminId)
            .Include(st => st.Subject)
            .ToListAsync();
        var teachingSubjects = subjectTeacherRecords
            .Select(st => st.Subject)
            .Where(s => s != null)
            .Select(s => s!)
            .Distinct()
            .ToList();
        ViewBag.TeachingSubjects = teachingSubjects;

        // 所教班级列表
        var classIds = subjectTeacherRecords
            .Select(st => st.ClassId)
            .Distinct()
            .ToList();
        if (classIds.Count > 0)
        {
            var classInfoMap = await _db.ClassInfos
                .Where(c => classIds.Contains(c.ClassInfoID))
                .Include(c => c.GradeLevel)
                .ToListAsync();
            ViewBag.TeachingClasses = classInfoMap;
        }
        else
        {
            ViewBag.TeachingClasses = new List<ClassInfo>();
        }

        // ====== 待办事项 ======
        var phone = User.FindFirst(ClaimTypes.MobilePhone)?.Value
                    ?? User.FindFirst("Phone")?.Value
                    ?? User.Identity?.Name ?? "";
        int unreadAnnCount = 0;
        if (!string.IsNullOrEmpty(phone))
        {
            unreadAnnCount = await _db.Announcements
                .Where(a => a.TargetRole == "全员" || a.TargetRole == role)
                .Where(a => !_db.AnnouncementReads
                    .Any(r => r.AnnouncementId == a.Id && r.TeacherPhone == phone))
                .CountAsync();
        }
        ViewBag.UnreadAnnCount = unreadAnnCount;

        // 近期待录入的考试
        var now = DateTime.Now;
        var teachingGrades = new List<string>();
        if (ViewBag.MyClass is ClassInfo myCi && myCi.GradeLevel != null)
            teachingGrades.Add(myCi.GradeLevel.DisplayName);
        foreach (var sc in (ViewBag.TeachingClasses as List<ClassInfo>) ?? new List<ClassInfo>())
        {
            if (sc.GradeLevel != null && !teachingGrades.Contains(sc.GradeLevel.DisplayName))
                teachingGrades.Add(sc.GradeLevel.DisplayName);
        }

        if (teachingGrades.Count > 0)
        {
            var pendingExams = await _db.ExamSchedules
                .Where(e => e.Status == "进行中")
                .ToListAsync();
            pendingExams = pendingExams
                .Where(e => e.Grades != null && teachingGrades.Any(g => ("," + e.Grades + ",").Contains("," + g + ",")))
                .ToList();
            ViewBag.PendingExams = pendingExams;
        }
        else
        {
            ViewBag.PendingExams = new List<ExamSchedule>();
        }

        // ====== 待处理维修申请（后勤主任/管理员） ======
        if (role == "管理员" || role == "后勤主任")
        {
            ViewBag.PendingRepairCount = await _db.RepairRequests.CountAsync(r => r.Status == "待处理");
        }
        else
        {
            ViewBag.PendingRepairCount = 0;
        }

        return View("TeacherDashboard");
    }

    /// <summary>
    /// AJAX: 获取当前用户未读公告
    /// </summary>
    public async Task<JsonResult> GetUnread()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        if (role == "管理员")
            return Json(new List<object>());

        var phone = User.FindFirst(ClaimTypes.MobilePhone)?.Value
                    ?? User.FindFirst("Phone")?.Value
                    ?? User.Identity?.Name ?? "";

        if (string.IsNullOrEmpty(phone))
            return Json(new List<object>());

        var unread = await _db.Announcements
            .Where(a => a.TargetRole == "全员" || a.TargetRole == role)
            .Where(a => !_db.AnnouncementReads
                .Any(r => r.AnnouncementId == a.Id && r.TeacherPhone == phone))
            .OrderByDescending(a => a.CreateTime)
            .Select(a => new { a.Id, a.Title, a.Content })
            .ToListAsync();

        return Json(unread);
    }

    /// <summary>错误页面（404、403 等状态码）</summary>
    [AllowAnonymous]
    public IActionResult Error(int statusCode = 0)
    {
        ViewBag.StatusCode = statusCode;
        return View();
    }
}
