using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

[Authorize(Roles = "管理员")]
public class SemesterController : Controller
{
    private readonly AppDbContext _db;

    public SemesterController(AppDbContext db)
    {
        _db = db;
    }

    // ========== 学年管理 ==========
    public async Task<IActionResult> Index()
    {
        var years = await _db.AcademicYears.OrderByDescending(y => y.YearName).ToListAsync();
        var semesters = await _db.Semesters.Include(s => s.AcademicYear).OrderByDescending(s => s.Id).ToListAsync();
        ViewBag.Semesters = semesters;
        return View(years);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddAcademicYear(string yearName)
    {
        if (string.IsNullOrEmpty(yearName))
            return Json(new { success = false, message = "请输入学年名称" });

        if (await _db.AcademicYears.AnyAsync(y => y.YearName == yearName))
            return Json(new { success = false, message = "该学年已存在" });

        _db.AcademicYears.Add(new AcademicYear { YearName = yearName });
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCurrentYear(int id)
    {
        // 清除所有学年的当前状态
        var allYears = await _db.AcademicYears.ToListAsync();
        foreach (var y in allYears) y.IsCurrent = false;

        var year = await _db.AcademicYears.FindAsync(id);
        if (year == null) return Json(new { success = false, message = "学年不存在" });
        year.IsCurrent = true;
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteYear(int id)
    {
        var year = await _db.AcademicYears.FindAsync(id);
        if (year == null) return Json(new { success = false, message = "学年不存在" });
        if (year.IsCurrent) return Json(new { success = false, message = "不能删除当前学年" });
        if (await _db.Semesters.AnyAsync(s => s.AcademicYearId == id))
            return Json(new { success = false, message = "请先删除该学年下的学期" });

        _db.AcademicYears.Remove(year);
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    // ========== 学期管理 ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSemester(int academicYearId, string semesterName)
    {
        if (string.IsNullOrEmpty(semesterName))
            return Json(new { success = false, message = "请输入学期名称" });

        if (!await _db.AcademicYears.AnyAsync(y => y.Id == academicYearId))
            return Json(new { success = false, message = "学年不存在" });

        _db.Semesters.Add(new Semester
        {
            AcademicYearId = academicYearId,
            SemesterName = semesterName
        });
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCurrentSemester(int id)
    {
        var allSemesters = await _db.Semesters.ToListAsync();
        foreach (var s in allSemesters) s.IsCurrent = false;

        var semester = await _db.Semesters.FindAsync(id);
        if (semester == null) return Json(new { success = false, message = "学期不存在" });
        semester.IsCurrent = true;
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSemester(int id)
    {
        var semester = await _db.Semesters.FindAsync(id);
        if (semester == null) return Json(new { success = false, message = "学期不存在" });
        if (semester.IsCurrent) return Json(new { success = false, message = "不能删除当前学期" });
        _db.Semesters.Remove(semester);
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    // ========== 诊断接口（临时） ==========
    [AllowAnonymous]
    [HttpGet]
    public IActionResult DiagView()
    {
        try
        {
            // 测试是否能找到 Semester/Index 视图
            return View("Index");
        }
        catch (Exception ex)
        {
            return Content("ERROR: " + ex.Message);
        }
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> DiagPromote()
    {
        try
        {
            var grades = await _db.Students
                .Where(s => s.Grade != null && s.Grade != "" && s.Status == "在读")
                .Select(s => s.Grade).Distinct().OrderBy(g => g).ToListAsync();
            ViewBag.Grades = grades;
            return View("Promote");
        }
        catch (Exception ex)
        {
            return Content("ERROR: " + ex.Message + "\n" + ex.InnerException?.Message + "\n" + ex.StackTrace);
        }
    }

    // ========== 升级 / 毕业 ==========
    [HttpGet]
    public async Task<IActionResult> Promote()
    {
        try
        {
            var grades = await _db.Students
                .Where(s => s.Grade != null && s.Grade != "" && s.Status == "在读")
                .Select(s => s.Grade).Distinct().OrderBy(g => g).ToListAsync();
            ViewBag.Grades = grades;
            return View();
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Promote Error: {ex}\n");
            return Content($"Promote Error: {ex.Message}\n{ex.InnerException?.Message}");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoPromote(string fromGrade)
    {
        if (string.IsNullOrEmpty(fromGrade))
            return Json(new { success = false, message = "请选择要升级的年级" });

        // 定义年级升级顺序（小学6年，初中3年）
        var primarySchool = new[] { "一年级", "二年级", "三年级", "四年级", "五年级", "六年级" };
        var middleSchool = new[] { "七年级", "八年级", "九年级" };
        var allGrades = primarySchool.Concat(middleSchool).ToList();

        int idx = allGrades.IndexOf(fromGrade);
        if (idx < 0 || idx >= allGrades.Count - 1)
            return Json(new { success = false, message = "该年级无法升级（可能是最高年级）" });

        var toGrade = allGrades[idx + 1];
        var students = await _db.Students.Where(s => s.Grade == fromGrade && s.Status == "在读").ToListAsync();
        int count = students.Count;
        foreach (var s in students)
        {
            s.Grade = toGrade;
            s.UpdateTime = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        // 记录操作日志
        _db.OperationLogs.Add(new OperationLog
        {
            OperatorName = User.FindFirst("RealName")?.Value ?? "管理员",
            OperatorRole = "管理员",
            ActionType = "升级",
            Detail = $"将 {count} 名学生从「{fromGrade}」升级到「{toGrade}」",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return Json(new { success = true, message = $"成功将 {count} 名学生从「{fromGrade}」升级到「{toGrade}」" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DoGraduate(string fromGrade)
    {
        if (string.IsNullOrEmpty(fromGrade))
            return Json(new { success = false, message = "请选择要毕业的年级" });

        var students = await _db.Students.Where(s => s.Grade == fromGrade && s.Status == "在读").ToListAsync();
        int count = students.Count;
        foreach (var s in students)
        {
            s.Status = "已毕业";
            s.UpdateTime = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        _db.OperationLogs.Add(new OperationLog
        {
            OperatorName = User.FindFirst("RealName")?.Value ?? "管理员",
            OperatorRole = "管理员",
            ActionType = "毕业",
            Detail = $"将 {count} 名「{fromGrade}」学生标记为已毕业",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return Json(new { success = true, message = $"成功将 {count} 名学生标记为已毕业" });
    }
}
