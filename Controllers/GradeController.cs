using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

public static class GradeHelper
{
    private static readonly Dictionary<string, int> GradeOffset = new()
    {
        {"一年级", 0}, {"二年级", 1}, {"三年级", 2},
        {"四年级", 3}, {"五年级", 4}, {"六年级", 5},
        {"七年级", 0}, {"八年级", 1}, {"九年级", 2}
    };

    private static readonly HashSet<string> PrimaryGrades = new()
    {
        "一年级", "二年级", "三年级", "四年级", "五年级", "六年级"
    };

    private static readonly HashSet<string> MiddleGrades = new()
    {
        "七年级", "八年级", "九年级"
    };

    public static int GradeToEntryYear(string gradeName, int currentYear)
    {
        if (GradeOffset.TryGetValue(gradeName, out int offset))
            return currentYear - offset;
        return currentYear;
    }

    public static string GetSchoolType(string gradeName)
    {
        if (PrimaryGrades.Contains(gradeName)) return "小学";
        if (MiddleGrades.Contains(gradeName)) return "初中";
        return "未知";
    }
}

[Authorize]
public class GradeController : Controller
{
    private readonly AppDbContext _db;

    public GradeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var grades = await _db.GradeLevels
            .Include(g => g.Classes)
            .OrderByDescending(g => g.EntryYear)
            .ThenBy(g => g.SchoolType)
            .ToListAsync();

        // 计算可选的入学年份列表（前10年到后3年）
        var currentYear = DateTime.Now.Year;
        var years = Enumerable.Range(currentYear - 10, 14).ToList();
        ViewBag.Years = years;

        // 加载各班级的当前班主任：Admin.ClassID -> RealName
        var teacherNames = await _db.Admins
            .Where(a => a.ClassID != null && a.Role != null && a.Role.Contains("班主任")
                && (a.Status == null || a.Status != "已删除"))
            .ToDictionaryAsync(a => a.ClassID!.Value, a => a.RealName ?? "");
        ViewBag.TeacherNames = teacherNames;

        // 统计各班学生人数：按学段+入学年份(EntryYear)+班级名匹配
        var allStudents = await _db.Students
            .Where(s => s.Status == "在读" && s.Grade != null && s.Grade != "" && s.ClassName != null && s.ClassName != "")
            .ToListAsync();
        var studentCounts = allStudents
            .GroupBy(s => new {
                SchoolType = GradeHelper.GetSchoolType(s.Grade!),
                EntryYear = GradeHelper.GradeToEntryYear(s.Grade!, currentYear),
                ClassName = s.ClassName!
            })
            .ToDictionary(g => $"{g.Key.SchoolType}|{g.Key.EntryYear}|{g.Key.ClassName}", g => g.Count());
        ViewBag.StudentCounts = studentCounts;

        // 统计各年级总人数（区分学段）
        var gradeStudentCounts = allStudents
            .GroupBy(s => new {
                SchoolType = GradeHelper.GetSchoolType(s.Grade!),
                EntryYear = GradeHelper.GradeToEntryYear(s.Grade!, currentYear)
            })
            .ToDictionary(g => $"{g.Key.SchoolType}|{g.Key.EntryYear}", g => g.Count());
        ViewBag.GradeStudentCounts = gradeStudentCounts;

        return View(grades);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddGrade(string schoolType, string gradeName)
    {
        if (string.IsNullOrWhiteSpace(schoolType) || (schoolType != "小学" && schoolType != "初中"))
        {
            TempData["Error"] = "学段只能为 小学 或 初中";
            return RedirectToAction("Index");
        }

        // 年级名称 → 入学年份偏移量
        var gradeMap = new Dictionary<string, int>
        {
            { "一年级", 0 }, { "二年级", 1 }, { "三年级", 2 }, { "四年级", 3 }, { "五年级", 4 }, { "六年级", 5 },
            { "七年级", 0 }, { "八年级", 1 }, { "九年级", 2 }
        };

        if (!gradeMap.ContainsKey(gradeName))
        {
            TempData["Error"] = "请选择有效的年级";
            return RedirectToAction("Index");
        }

        // 验证学段与年级匹配
        var primaryGrades = new[] { "一年级", "二年级", "三年级", "四年级", "五年级", "六年级" };
        var middleGrades = new[] { "七年级", "八年级", "九年级" };
        if (schoolType == "小学" && !primaryGrades.Contains(gradeName))
        {
            TempData["Error"] = "小学学段只能选择一年级至六年级";
            return RedirectToAction("Index");
        }
        if (schoolType == "初中" && !middleGrades.Contains(gradeName))
        {
            TempData["Error"] = "初中学段只能选择七年级至九年级";
            return RedirectToAction("Index");
        }

        int currentYear = DateTime.Now.Year;
        int offset = gradeMap[gradeName];
        int entryYear = currentYear - offset;

        // 检查是否已存在相同年级
        var exists = await _db.GradeLevels
            .AnyAsync(g => g.EntryYear == entryYear && g.SchoolType == schoolType);
        if (exists)
        {
            TempData["Error"] = $"{schoolType}{entryYear} 级（{gradeName}）已存在";
            return RedirectToAction("Index");
        }

        var grade = new GradeLevel
        {
            EntryYear = entryYear,
            SchoolType = schoolType,
            CreateTime = DateTime.Now
        };

        _db.GradeLevels.Add(grade);
        await _db.SaveChangesAsync();

        await LogOperation("添加年级", grade.GradeLevelID.ToString(), grade.DisplayName);
        TempData["Success"] = $"新增 {grade.DisplayName} 成功";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGrade(int id)
    {
        var grade = await _db.GradeLevels.Include(g => g.Classes).FirstOrDefaultAsync(g => g.GradeLevelID == id);
        if (grade == null)
        {
            if (IsAjaxRequest()) return Json(new { success = false, message = "年级不存在" });
            TempData["Error"] = "年级不存在";
            return RedirectToAction("Index");
        }

        var name = grade.DisplayName;
        _db.GradeLevels.Remove(grade); // Cascade 会自动删除关联的班级
        await _db.SaveChangesAsync();

        await LogOperation("删除年级", id.ToString(), name);

        if (IsAjaxRequest())
            return Json(new { success = true, message = $"已删除 {name}" });

        TempData["Success"] = $"已删除 {name}";
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddClass(int gradeLevelId, string? className, int count = 1)
    {
        var grade = await _db.GradeLevels.FindAsync(gradeLevelId);
        if (grade == null)
        {
            TempData["Error"] = "年级不存在";
            return RedirectToAction("Index");
        }

        // 批量创建模式：输入数字，自动创建 "1班"、"2班"……
        if (count > 1)
        {
            var created = 0;
            for (int i = 1; i <= count; i++)
            {
                var name = $"{i}班";
                var exists = await _db.ClassInfos
                    .AnyAsync(c => c.GradeLevelID == gradeLevelId && c.ClassName == name);
                if (!exists)
                {
                    _db.ClassInfos.Add(new ClassInfo
                    {
                        GradeLevelID = gradeLevelId,
                        ClassName = name,
                        CreateTime = DateTime.Now
                    });
                    created++;
                }
            }
            await _db.SaveChangesAsync();
            await LogOperation("添加班级", grade.GradeLevelID.ToString(), $"{grade.DisplayName}: 批量创建 {created} 个班级");
            TempData["Success"] = $"已创建 {created} 个班级（1班 ~ {count}班）";
            return RedirectToAction("Index");
        }

        // 单班创建模式
        if (string.IsNullOrWhiteSpace(className))
        {
            TempData["Error"] = "请输入班级名称或数量";
            return RedirectToAction("Index");
        }

        var existsSingle = await _db.ClassInfos
            .AnyAsync(c => c.GradeLevelID == gradeLevelId && c.ClassName == className);
        if (existsSingle)
        {
            TempData["Error"] = $"该年级下已存在班级 \"{className}\"";
            return RedirectToAction("Index");
        }

        _db.ClassInfos.Add(new ClassInfo
        {
            GradeLevelID = gradeLevelId,
            ClassName = className,
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();

        await LogOperation("添加班级", grade.GradeLevelID.ToString(), $"{grade.DisplayName}: 添加班级 {className}");
        TempData["Success"] = $"新增班级 \"{className}\" 成功";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public async Task<IActionResult> GetAvailableTeachers()
    {
        var teachers = await _db.Admins
            .Where(a => a.Role != null && a.Role.Contains("班主任") && a.ClassID == null)
            .Select(a => new
            {
                a.AdminID,
                a.RealName,
                a.Username
            })
            .ToListAsync();
        return Json(teachers);
    }

    [HttpGet]
    public async Task<IActionResult> SearchTeachers(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Json(new List<object>());

        var teachers = await _db.Admins
            .Where(a => a.Role != null && a.Role.Contains("班主任") && a.ClassID == null
                && ((a.RealName != null && a.RealName.Contains(keyword))
                    || (a.Phone != null && a.Phone.Contains(keyword))))
            .Select(a => new
            {
                a.AdminID,
                a.RealName,
                a.Phone
            })
            .Take(20)
            .ToListAsync();
        return Json(teachers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTeacher(int classId, int teacherId)
    {
        var classInfo = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == classId);
        if (classInfo == null)
            return Json(new { success = false, message = "班级不存在" });

        var teacher = await _db.Admins.FindAsync(teacherId);
        if (teacher == null)
            return Json(new { success = false, message = "教师不存在" });

        // 一个班主任只能管理一个班：如果该教师已分配其他班级，先清除旧关联
        if (teacher.ClassID.HasValue && teacher.ClassID.Value != classId)
        {
            teacher.ClassID = null;
            teacher.Grade = null;
            teacher.ClassName = null;
        }

        var fullClassName = $"{classInfo.GradeLevel?.DisplayName}{classInfo.ClassName}";
        teacher.ClassID = classId;
        teacher.Grade = classInfo.GradeLevel?.CurrentGradeName;
        teacher.ClassName = classInfo.ClassName;
        await _db.SaveChangesAsync();

        await LogOperation("分配班主任", classId.ToString(), $"分配 {teacher.RealName} 到 {classInfo.GradeLevel?.DisplayName}{classInfo.ClassName}");
        return Json(new { success = true, message = $"已设置 {teacher.RealName} 为 {classInfo.GradeLevel?.DisplayName}{classInfo.ClassName} 的班主任" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTeacher(int classId)
    {
        var classInfo = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == classId);
        if (classInfo == null)
        {
            if (IsAjaxRequest()) return Json(new { success = false, message = "班级不存在" });
            TempData["Error"] = "班级不存在";
            return RedirectToAction("Index");
        }

        var teacher = await _db.Admins.FirstOrDefaultAsync(a => a.ClassID == classId
            && a.Role != null && a.Role.Contains("班主任"));
        if (teacher == null)
        {
            var msg = "该班级未设置班主任";
            if (IsAjaxRequest()) return Json(new { success = false, message = msg });
            TempData["Error"] = msg;
            return RedirectToAction("Index");
        }

        var teacherName = teacher.RealName ?? "";
        var fullClassName = $"{classInfo.GradeLevel?.DisplayName}{classInfo.ClassName}";
        teacher.ClassID = null;
        teacher.Grade = null;
        teacher.ClassName = null;
        await _db.SaveChangesAsync();

        await LogOperation("取消班主任", classId.ToString(), $"取消 {teacherName} 的 {fullClassName} 班主任");

        var successMsg = $"已取消 {teacherName} 的 {fullClassName} 班主任职务";
        if (IsAjaxRequest())
            return Json(new { success = true, message = successMsg });

        TempData["Success"] = successMsg;
        return RedirectToAction("Index");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteClass(int id)
    {
        var cls = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == id);
        if (cls == null)
        {
            if (IsAjaxRequest()) return Json(new { success = false, message = "班级不存在" });
            TempData["Error"] = "班级不存在";
            return RedirectToAction("Index");
        }

        // 检查该班级下是否有学生
        var gradeName = cls.GradeLevel?.CurrentGradeName ?? "";
        var hasStudents = await _db.Students
            .AnyAsync(s => s.Grade == gradeName && s.ClassName == cls.ClassName);
        if (hasStudents)
        {
            var msg = $"该班级下有学生，请先清空学生后再删除";
            if (IsAjaxRequest()) return Json(new { success = false, message = msg });
            TempData["Error"] = msg;
            return RedirectToAction("Index");
        }

        // 清除该班级的班主任关联
        var teacher = await _db.Admins.FirstOrDefaultAsync(a => a.ClassID == id
            && a.Role != null && a.Role.Contains("班主任"));
        if (teacher != null)
        {
            teacher.ClassID = null;
            teacher.Grade = null;
            teacher.ClassName = null;
        }

        var fullName = $"{cls.GradeLevel?.DisplayName}{cls.ClassName}";
        var name = cls.ClassName;
        _db.ClassInfos.Remove(cls);
        await _db.SaveChangesAsync();

        await LogOperation("删除班级", id.ToString(), $"删除班级 {fullName}");

        if (IsAjaxRequest())
            return Json(new { success = true, message = $"已删除班级 \"{name}\"" });

        TempData["Success"] = $"已删除班级 \"{name}\"";
        return RedirectToAction("Index");
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }

    private async Task LogOperation(string actionType, string targetNo, string detail)
    {
        var operatorName = User.FindFirst("RealName")?.Value ?? User.Identity?.Name ?? "未知";
        var operatorRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "管理员";

        _db.OperationLogs.Add(new OperationLog
        {
            OperatorName = operatorName,
            OperatorRole = operatorRole,
            ActionType = actionType,
            TargetNo = targetNo,
            Detail = detail,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }
}
