using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

[Authorize(Roles = "管理员")]
public class SubjectController : Controller
{
    private readonly AppDbContext _db;

    public SubjectController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? grade, int? classId)
    {
        var query = _db.Subjects.AsQueryable();

        if (!string.IsNullOrWhiteSpace(grade))
            query = query.Where(s => s.Grade == null || s.Grade == grade);

        if (classId.HasValue)
            query = query.Where(s => _db.SubjectClasses.Any(sc => sc.SubjectId == s.Id && sc.ClassId == classId.Value));

        var subjects = await query.OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync();
        var grades = await _db.GradeLevels
            .OrderByDescending(g => g.EntryYear)
            .ThenBy(g => g.SchoolType)
            .ToListAsync();
        ViewBag.Grades = grades
            .Select(g => new { Value = g.CurrentGradeName, Text = g.CurrentGradeName })
            .ToList();

        // 加载每科的登分教师（按班级）
        var allSt = await _db.SubjectTeachers
            .Include(st => st.Admin)
            .ToListAsync();
        var teacherMap = allSt
            .GroupBy(st => new { st.SubjectId, ClassId = st.ClassId })
            .ToDictionary(g => g.Key, g => g.Where(st => st.Admin != null).Select(st => st.Admin!.RealName).ToList());
        ViewBag.TeacherMap = teacherMap;

        // 加载每科的关联班级
        var allSc = await _db.SubjectClasses.ToListAsync();
        var classMap = new Dictionary<int, List<string>>();
        if (allSc.Count > 0)
        {
            var classIds = allSc.Select(sc => sc.ClassId).Distinct().ToList();
            var classInfoMap = await _db.ClassInfos
                .Where(c => classIds.Contains(c.ClassInfoID))
                .ToDictionaryAsync(c => c.ClassInfoID, c => c.ClassName);
            classMap = allSc
                .GroupBy(sc => sc.SubjectId)
                .ToDictionary(g => g.Key, g => g.Select(sc => classInfoMap.TryGetValue(sc.ClassId, out var cn) ? cn : "?").ToList());
        }
        ViewBag.ClassMap = classMap;

        // 所有班级信息（用于按班级显示教师）
        var allClasses = await _db.ClassInfos.OrderBy(c => c.ClassName).ToListAsync();
        ViewBag.AllClasses = allClasses;

        ViewBag.FilterGrade = grade;
        ViewBag.FilterClassId = classId;

        return View(subjects);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string[] names, string[] grades)
    {
        if (names == null || names.Length == 0)
            return Json(new { success = false, message = "请至少选择一个科目" });

        // 获取当前最大排序号
        var maxOrder = await _db.Subjects.MaxAsync(s => (int?)s.SortOrder) ?? 0;
        var added = 0;

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (grades != null && grades.Length > 0)
            {
                // 每个科目 × 每个年级 各建一条
                foreach (var grade in grades)
                {
                    if (string.IsNullOrWhiteSpace(grade)) continue;
                    _db.Subjects.Add(new Subject
                    {
                        Name = name.Trim(),
                        Grade = grade.Trim(),
                        SortOrder = ++maxOrder
                    });
                    added++;
                }
            }
            else
            {
                // 不选年级 = 全校通用
                _db.Subjects.Add(new Subject
                {
                    Name = name.Trim(),
                    Grade = null,
                    SortOrder = ++maxOrder
                });
                added++;
            }
        }

        await _db.SaveChangesAsync();
        await LogOperation("添加科目", null, $"批量创建 {added} 条科目记录: {string.Join(", ", names)}");
        return Json(new { success = true, message = $"成功创建 {added} 条科目记录" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string name, string grade, int sortOrder, int fullScore = 100)
    {
        var sub = await _db.Subjects.FindAsync(id);
        if (sub == null) return Json(new { success = false, message = "科目不存在" });

        sub.Name = name;
        sub.Grade = string.IsNullOrEmpty(grade) ? null : grade;
        sub.SortOrder = sortOrder;
        sub.FullScore = fullScore;
        await _db.SaveChangesAsync();
        await LogOperation("编辑科目", id.ToString(), $"编辑科目: {sub.Name} 年级={grade} 满分={fullScore}");
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var sub = await _db.Subjects.FindAsync(id);
        if (sub == null) return Json(new { success = false, message = "科目不存在" });
        if (await _db.Scores.AnyAsync(s => s.SubjectId == id))
            return Json(new { success = false, message = "该科目已有成绩记录，无法删除" });

        var name = sub.Name;
        _db.Subjects.Remove(sub);
        await _db.SaveChangesAsync();
        await LogOperation("删除科目", id.ToString(), $"删除科目: {name}");
        return Json(new { success = true });
    }

    /// <summary>
    /// 一键迁移：为 Subjects 表添加 FullScore 列
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MigrateFullScore()
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Subject' AND COLUMN_NAME='FullScore') " +
                "ALTER TABLE Subject ADD FullScore int NOT NULL DEFAULT 100");
            return Content("迁移成功：FullScore 列已添加");
        }
        catch (Exception ex)
        {
            return Content("迁移失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 迁移：为 SubjectTeacher 表添加 ClassId 列
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MigrateSubjectTeacherClassId()
    {
        try
        {
            await _db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (
                    SELECT * FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='SubjectTeacher' AND COLUMN_NAME='ClassId'
                )
                ALTER TABLE SubjectTeacher ADD ClassId int NULL
            ");
            return Content("✅ 迁移成功：SubjectTeacher.ClassId 列已添加");
        }
        catch (Exception ex)
        {
            return Content("❌ 迁移失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 迁移：为 Admin 表添加 MustChangePassword 列（首次登录强制改密）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MigrateMustChangePassword()
    {
        try
        {
            // 添加列
            await _db.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (
                    SELECT * FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Admin' AND COLUMN_NAME='MustChangePassword'
                )
                ALTER TABLE Admin ADD MustChangePassword bit NOT NULL DEFAULT 1
            ");
            // 管理员设为 false（不需要改密码）
            await _db.Database.ExecuteSqlRawAsync("UPDATE Admin SET MustChangePassword = 0 WHERE RTRIM(Role) = N'管理员'");
            // 超级管理员也设为 false
            await _db.Database.ExecuteSqlRawAsync("UPDATE Admin SET MustChangePassword = 0 WHERE Username = '13423297227'");
            return Content("✅ 迁移成功：Admin.MustChangePassword 列已添加，非管理员用户已标记为首次登录需改密");
        }
        catch (Exception ex)
        {
            return Content("❌ 迁移失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 迁移：将 Admin 表中现有的明文密码哈希化
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> MigrateHashPasswords()
    {
        try
        {
            var admins = await _db.Admins.ToListAsync();
            int count = 0;
            foreach (var a in admins)
            {
                if (!PasswordHelper.IsHashed(a.Password))
                {
                    a.Password = PasswordHelper.Hash(a.Password);
                    count++;
                }
            }
            await _db.SaveChangesAsync();
            return Content($"✅ 迁移成功：已哈希 {count} 个密码（PBKDF2 + SHA256），共 {admins.Count} 个用户");
        }
        catch (Exception ex)
        {
            return Content("❌ 迁移失败：" + ex.Message);
        }
    }

    // ========== 获取所有教师列表（不含管理员）=.
    [HttpGet]
    public async Task<IActionResult> GetTeachers()
    {
        var teachers = await _db.Admins
            .Where(a => a.Role != null && !a.Role.Contains("管理员"))
            .OrderBy(a => a.Role).ThenBy(a => a.RealName)
            .Select(a => new { a.AdminID, a.RealName, a.Role, a.ClassName })
            .ToListAsync();
        return Json(teachers);
    }

    // ========== 获取科目已授权的教师ID（按班级） ==========
    [HttpGet]
    public async Task<IActionResult> GetSubjectTeachers(int subjectId, int? classId)
    {
        var query = _db.SubjectTeachers.Where(st => st.SubjectId == subjectId);
        if (classId.HasValue)
            query = query.Where(st => st.ClassId == classId);
        var ids = await query.Select(st => st.AdminId).ToListAsync();
        return Json(ids);
    }

    // ========== 保存科目教师授权（含班级） ==========
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveSubjectTeachers(int subjectId, int classId, [FromBody] List<int> teacherIds)
    {
        // 删除旧的关联（同一科目+班级）
        var old = await _db.SubjectTeachers
            .Where(st => st.SubjectId == subjectId && st.ClassId == classId)
            .ToListAsync();
        _db.SubjectTeachers.RemoveRange(old);

        // 添加新的关联
        if (teacherIds != null && teacherIds.Count > 0)
        {
            foreach (var tid in teacherIds)
            {
                _db.SubjectTeachers.Add(new SubjectTeacher
                {
                    SubjectId = subjectId,
                    AdminId = tid,
                    ClassId = classId
                });
            }
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    // ========== 根据年级获取班级列表 ==========
    [HttpGet]
    public async Task<IActionResult> GetClassesByGrade(string grade)
    {
        if (string.IsNullOrEmpty(grade)) return Json(new List<object>());
        var gradeLevels = await _db.GradeLevels.ToListAsync();
        var matched = gradeLevels.FirstOrDefault(g =>
            string.Equals(g.CurrentGradeName, grade, StringComparison.OrdinalIgnoreCase));
        if (matched == null) return Json(new List<object>());
        var classes = await _db.ClassInfos
            .Where(c => c.GradeLevelID == matched.GradeLevelID)
            .OrderBy(c => c.ClassName)
            .Select(c => new { c.ClassInfoID, c.ClassName })
            .ToListAsync();
        return Json(classes);
    }

    // ========== 获取科目已关联的班级ID ==========
    [HttpGet]
    public async Task<IActionResult> GetSubjectClasses(int subjectId)
    {
        var ids = await _db.SubjectClasses
            .Where(sc => sc.SubjectId == subjectId)
            .Select(sc => sc.ClassId)
            .ToListAsync();
        return Json(ids);
    }

    // ========== 保存科目班级关联 ==========
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveSubjectClasses(int subjectId, [FromBody] List<int> classIds)
    {
        var old = await _db.SubjectClasses.Where(sc => sc.SubjectId == subjectId).ToListAsync();
        _db.SubjectClasses.RemoveRange(old);

        if (classIds != null && classIds.Count > 0)
        {
            foreach (var cid in classIds)
            {
                _db.SubjectClasses.Add(new SubjectClass { SubjectId = subjectId, ClassId = cid });
            }
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    private async Task LogOperation(string actionType, string? targetNo, string detail)
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
