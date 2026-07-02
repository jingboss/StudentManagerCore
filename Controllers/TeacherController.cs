using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using ClosedXML.Excel;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

[Authorize]
public class TeacherController : Controller
{
    private readonly AppDbContext _db;
    private readonly Services.AuditService _auditService;

    public TeacherController(AppDbContext db, Services.AuditService auditService)
    {
        _db = db;
        _auditService = auditService;
    }

    public async Task<IActionResult> Index(string? keyword, int page = 1, string? status = null, string? role = null)
    {
        int pageSize = 20;
        var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        var isAdmin = currentUserRole == "管理员";

        var query = _db.Admins.Where(a => a.Role != null && !a.Role.Contains("管理员"));

        // 非管理员只能看到自己
        if (!isAdmin)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out var userId))
                query = query.Where(a => a.AdminID == userId);
            else
                return Forbid();
        }

        if (status == "已删除")
            query = query.Where(a => a.Status == "已删除");
        else
            query = query.Where(a => a.Status == null || a.Status == "正常");

        // 角色筛选（多角色支持：FIND_IN_SET 匹配）
        if (!string.IsNullOrWhiteSpace(role) && role != "全部")
            query = query.Where(a => a.Role != null && a.Role.Contains(role));

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(t =>
                (t.RealName != null && t.RealName.Contains(keyword)) ||
                (t.Username != null && t.Username.Contains(keyword)) ||
                (t.ClassName != null && t.ClassName.Contains(keyword)) ||
                (t.Grade != null && t.Grade.Contains(keyword)));
        }

        var total = await query.CountAsync();
        var teachers = await query
            .OrderByDescending(t => t.AdminID)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // 加载所教科目
        var teacherIds = teachers.Select(t => t.AdminID).ToList();
        var subjectData = await _db.TeacherSubjects
            .Where(ts => teacherIds.Contains(ts.AdminId))
            .Include(ts => ts.Subject)
            .ToListAsync();
        var teacherSubjects = subjectData
            .GroupBy(ts => ts.AdminId)
            .ToDictionary(g => g.Key, g => string.Join("、", g
                .Where(ts => ts.Subject != null && !string.IsNullOrEmpty(ts.Subject.Name))
                .Select(ts => ts.Subject!.Name.Trim())
                .Distinct()));
        ViewBag.TeacherSubjects = teacherSubjects;

        // 角色统计（多角色支持）
        var totalTeachers = await _db.Admins.CountAsync(a =>
            a.Role != null && !a.Role.Contains("管理员")
            && (a.Status == null || a.Status != "已删除"));
        var headCount = await _db.Admins.CountAsync(a =>
            a.Role != null && a.Role.Contains("班主任")
            && (a.Status == null || a.Status != "已删除"));
        var subjectCount = await _db.Admins.CountAsync(a =>
            a.Role != null && a.Role.Contains("科任教师")
            && (a.Status == null || a.Status != "已删除"));
        var gradeLeaderCount = await _db.Admins.CountAsync(a =>
            a.Role != null && a.Role.Contains("年级级长")
            && (a.Status == null || a.Status != "已删除"));

        ViewBag.Keyword = keyword;
        ViewBag.CurrentStatus = status ?? "正常";
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
        ViewBag.Total = total;
        ViewBag.FilterRole = role;
        ViewBag.TotalTeachers = totalTeachers;
        ViewBag.HeadCount = headCount;
        ViewBag.SubjectCount = subjectCount;
        ViewBag.GradeLeaderCount = gradeLeaderCount;

        return View(teachers);
    }

    [Authorize(Roles = "管理员")]
    public IActionResult Add()    {
        ViewBag.GradeList = _db.GradeLevels.ToList()
            .Where(g => {
                var name = g.CurrentGradeName;
                return name != "未入学" && !name.StartsWith("已毕业");
            })
            .Select(g => new { 
                Value = g.CurrentGradeName, 
                Text = g.CurrentGradeName,
                SchoolType = g.SchoolType 
            })
            .ToList();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> Add(Models.Admin admin)
    {
        if (string.IsNullOrWhiteSpace(admin.RealName))
            ModelState.AddModelError("RealName", "姓名不能为空");
        if (string.IsNullOrWhiteSpace(admin.Username))
            ModelState.AddModelError("Username", "用户名不能为空");
        if (string.IsNullOrWhiteSpace(admin.Password))
        {
            ModelState.AddModelError("Password", "密码不能为空");
        }
        else
        {
            var pwdError = StudentManagerCore.Services.PasswordHelper.Validate(admin.Password);
            if (pwdError != null)
                ModelState.AddModelError("Password", pwdError);
        }
        if (string.IsNullOrWhiteSpace(admin.Role))
            ModelState.AddModelError("Role", "请至少选择一个角色");

        if (ModelState.IsValid)
        {
            // Check duplicate username
            var exists = await _db.Admins.AnyAsync(a => a.Username == admin.Username);
            if (exists)
            {
                ModelState.AddModelError("Username", "用户名已存在");
                if (IsAjaxRequest())
                    return Json(new { success = false, message = "用户名已存在" });
                return View(admin);
            }

            if (!string.IsNullOrWhiteSpace(admin.Role))
                admin.Role = admin.Role.Trim().TrimEnd(',');
            admin.CreateTime = DateTime.Now;
            admin.Password = PasswordHelper.Hash(admin.Password);

            // 根据年级+班级同步 ClassID（关联班级管理）
            if (!string.IsNullOrWhiteSpace(admin.Grade) && !string.IsNullOrWhiteSpace(admin.ClassName))
            {
                var entryYear = GradeHelper.GradeToEntryYear(admin.Grade, DateTime.Now.Year);
                var gradeLevel = await _db.GradeLevels
                    .FirstOrDefaultAsync(gl => gl.EntryYear == entryYear && gl.SchoolType == admin.SchoolType);
                if (gradeLevel != null)
                {
                    var classInfo = await _db.ClassInfos
                        .FirstOrDefaultAsync(c => c.GradeLevelID == gradeLevel.GradeLevelID && c.ClassName == admin.ClassName);
                    admin.ClassID = classInfo?.ClassInfoID;
                }
            }

            _db.Admins.Add(admin);
            await _db.SaveChangesAsync();
            await _auditService.LogAsync("添加", $"添加教职工: {admin.RealName}", targetName: admin.RealName);
            TempData["Success"] = "添加教职工成功！";
            if (IsAjaxRequest())
                return Json(new { success = true, adminId = admin.AdminID });
            return RedirectToAction("Index");
        }

        if (IsAjaxRequest())
        {
            var errors = string.Join("; ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return Json(new { success = false, message = errors });
        }

        return View(admin);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        var isAdmin = currentUserRole == "管理员";

        // 非管理员只能编辑自己的信息
        if (!isAdmin)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId) || userId != id)
                return Forbid();
        }

        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return NotFound();
        ViewBag.GradeList = _db.GradeLevels.ToList()
            .Where(g => {
                var name = g.CurrentGradeName;
                return name != "未入学" && !name.StartsWith("已毕业");
            })
            .Select(g => new { 
                Value = g.CurrentGradeName, 
                Text = g.CurrentGradeName,
                SchoolType = g.SchoolType 
            })
            .ToList();
        return View(admin);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Models.Admin admin, List<int>? subjectIds)
    {
        if (id != admin.AdminID)
        {
            if (IsAjaxRequest()) return Json(new { success = false, message = "参数错误" });
            return NotFound();
        }

        // 非管理员只能编辑自己的信息
        var currentUserRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        var isAdmin = currentUserRole == "管理员";
        if (!isAdmin)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId) || userId != id)
                return Forbid();
        }

        if (string.IsNullOrWhiteSpace(admin.RealName))
            ModelState.AddModelError("RealName", "姓名不能为空");

        // 编辑模式下用户名和密码字段不在表单中，移除验证避免失败
        ModelState.Remove("Username");
        ModelState.Remove("Password");

        if (ModelState.IsValid)
        {
            var existing = await _db.Admins.FindAsync(id);
            if (existing == null)
            {
                if (IsAjaxRequest()) return Json(new { success = false, message = "用户不存在" });
                return NotFound();
            }

            existing.RealName = admin.RealName;
            existing.Gender = admin.Gender;
            existing.Nation = admin.Nation;
            existing.BirthDate = admin.BirthDate;
            existing.RegisteredDomicile = admin.RegisteredDomicile;
            existing.HighestEducation = admin.HighestEducation;
            existing.CertSubject = admin.CertSubject;
            existing.CertNumber = admin.CertNumber;
            existing.CertAuthority = admin.CertAuthority;
            existing.Phone = admin.Phone;

            // 根据年级+班级同步 ClassID（关联班级管理）
            if (!string.IsNullOrWhiteSpace(admin.Grade) && !string.IsNullOrWhiteSpace(admin.ClassName))
            {
                var entryYear = GradeHelper.GradeToEntryYear(admin.Grade, DateTime.Now.Year);
                var gradeLevel = await _db.GradeLevels
                    .FirstOrDefaultAsync(gl => gl.EntryYear == entryYear && gl.SchoolType == admin.SchoolType);
                if (gradeLevel != null)
                {
                    var classInfo = await _db.ClassInfos
                        .FirstOrDefaultAsync(c => c.GradeLevelID == gradeLevel.GradeLevelID && c.ClassName == admin.ClassName);
                    existing.ClassID = classInfo?.ClassInfoID;
                }
                else
                {
                    existing.ClassID = null;
                }
            }
            else
            {
                existing.ClassID = null;
            }
            existing.ClassName = admin.ClassName;
            existing.Grade = admin.Grade;
            existing.SchoolType = admin.SchoolType;
            if (!string.IsNullOrWhiteSpace(admin.Role))
                existing.Role = admin.Role.Trim().TrimEnd(',');

            if (!string.IsNullOrWhiteSpace(admin.Password))
            {
                var pwdError = StudentManagerCore.Services.PasswordHelper.Validate(admin.Password);
                if (pwdError != null)
                {
                    if (IsAjaxRequest()) return Json(new { success = false, message = pwdError });
                    ModelState.AddModelError("Password", pwdError);
                    return View(admin);
                }
                existing.Password = StudentManagerCore.Services.PasswordHelper.Hash(admin.Password);
            }

            await _db.SaveChangesAsync();

            // 保存所教科目（展开为代表ID对应的全部年级科目）
            var existingSubjects = await _db.TeacherSubjects.Where(ts => ts.AdminId == id).ToListAsync();
            _db.TeacherSubjects.RemoveRange(existingSubjects);
            if (subjectIds != null && subjectIds.Count > 0)
            {
                var allIds = await ExpandSubjectIds(subjectIds, existing.SchoolType);
                foreach (var subjId in allIds)
                {
                    _db.TeacherSubjects.Add(new TeacherSubject
                    {
                        AdminId = id,
                        SubjectId = subjId
                    });
                }
            }
            await _db.SaveChangesAsync();

            await _auditService.LogAsync("编辑", "编辑教职工信息", targetName: existing.RealName);

            if (IsAjaxRequest())
                return Json(new { success = true, message = "保存成功" });

            TempData["Success"] = "修改教职工信息成功！";
            return RedirectToAction("Index");
        }

        if (IsAjaxRequest())
            return Json(new { success = false, message = "表单验证失败，请检查输入" });

        return View(admin);
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }

    // ========== 修改密码 ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(int id, string newPassword)
    {
        var pwdError = StudentManagerCore.Services.PasswordHelper.Validate(newPassword);
        if (pwdError != null)
            return Json(new { success = false, message = pwdError });

        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        admin.Password = PasswordHelper.Hash(newPassword);
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "密码修改成功" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> Delete(int id)    {
        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        admin.Status = "已删除";
        await _db.SaveChangesAsync();
        await _auditService.LogAsync("删除", $"删除教职工: {admin.RealName}", targetName: admin.RealName);
        return Json(new { success = true, message = "删除成功" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> BatchDelete(string ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return Json(new { success = false, message = "请选择要删除的教职工" });

        var idList = ids.Split(',').Select(int.Parse).ToList();
        var admins = await _db.Admins.Where(a => idList.Contains(a.AdminID)).ToListAsync();
        foreach (var admin in admins)
        {
            admin.Status = "已删除";
        }
        await _db.SaveChangesAsync();
        await _auditService.LogAsync("批量删除", $"批量删除教职工 {admins.Count} 人", targetName: string.Join(",", admins.Select(a => a.RealName)));
        return Json(new { success = true, message = $"已删除 {admins.Count} 人" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> BatchRestore(string ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return Json(new { success = false, message = "请选择要恢复的教职工" });

        var idList = ids.Split(',').Select(int.Parse).ToList();
        var admins = await _db.Admins.Where(a => idList.Contains(a.AdminID)).ToListAsync();
        foreach (var admin in admins)
        {
            admin.Status = "正常";
        }
        await _db.SaveChangesAsync();
        await _auditService.LogAsync("批量恢复", $"批量恢复教职工 {admins.Count} 人", targetName: string.Join(",", admins.Select(a => a.RealName)));
        return Json(new { success = true, message = $"已恢复 {admins.Count} 人" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchHardDelete(string ids, string securityCode)
    {
        var sc = await _db.SiteConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "SecurityCode");
        var requiredCode = sc?.ConfigValue ?? PasswordHelper.Hash("320324");
        bool codeValid;
        if (PasswordHelper.IsHashed(requiredCode))
            codeValid = PasswordHelper.Verify(securityCode?.Trim() ?? "", requiredCode);
        else
            codeValid = (securityCode?.Trim() == requiredCode); // 兼容旧明文
        if (!codeValid)
            return Json(new { success = false, message = "安全码错误，操作已取消" });

        if (string.IsNullOrWhiteSpace(ids))
            return Json(new { success = false, message = "请选择要删除的教职工" });

        var idList = ids.Split(',').Select(int.Parse).ToList();
        var admins = await _db.Admins.Where(a => idList.Contains(a.AdminID)).ToListAsync();
        var names = string.Join(",", admins.Select(a => a.RealName));
        _db.Admins.RemoveRange(admins);
        await _db.SaveChangesAsync();
        await _auditService.LogAsync("批量彻底删除", $"批量彻底删除教职工 {admins.Count} 人", targetName: names);
        return Json(new { success = true, message = $"已彻底删除 {admins.Count} 人" });
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        admin.Status = "正常";
        await _db.SaveChangesAsync();
        await _auditService.LogAsync("恢复", $"恢复教职工: {admin.RealName}", targetName: admin.RealName);
        return Json(new { success = true, message = "已恢复" });
    }

    /// <summary>彻底删除（从数据库移除），需安全码验证</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentDelete(int id, string securityCode)
    {
        var sc = await _db.SiteConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "SecurityCode");
        var requiredCode = sc?.ConfigValue ?? PasswordHelper.Hash("320324");
        bool codeValid;
        if (PasswordHelper.IsHashed(requiredCode))
            codeValid = PasswordHelper.Verify(securityCode?.Trim() ?? "", requiredCode);
        else
            codeValid = (securityCode?.Trim() == requiredCode); // 兼容旧明文
        if (!codeValid)
            return Json(new { success = false, message = "安全码错误，操作已取消" });

        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        var name = admin.RealName ?? "未知";
        _db.Admins.Remove(admin);
        await _db.SaveChangesAsync();
        await _auditService.LogAsync("彻底删除", $"彻底删除教职工: {name}", targetName: name);
        return Json(new { success = true, message = $"已从数据库彻底删除「{name}」" });
    }

    public IActionResult Import()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError("", "请选择文件");
            return View();
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            ModelState.AddModelError("", "CSV文件不能超过5MB");
            return View();
        }

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".csv")
        {
            ModelState.AddModelError("", "仅支持CSV文件");
            return View();
        }

        int imported = 0;
        int failed = 0;

        using var reader = new StreamReader(file.OpenReadStream());
        // Skip header
        if (!reader.EndOfStream)
            await reader.ReadLineAsync();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 4)
            {
                failed++;
                continue;
            }

            try
            {
                var plainPwd = parts[1].Trim();
                var pwdError = StudentManagerCore.Services.PasswordHelper.Validate(plainPwd);
                if (pwdError != null)
                {
                    failed++;
                    continue;
                }

                var admin = new Models.Admin
                {
                    Username = parts[0].Trim(),
                    Password = StudentManagerCore.Services.PasswordHelper.Hash(plainPwd),
                    RealName = parts[2].Trim(),
                    Role = "班主任",
                    Phone = parts.Length > 3 ? parts[3].Trim() : null,
                    Grade = parts.Length > 4 ? parts[4].Trim() : null,
                    ClassName = parts.Length > 5 ? parts[5].Trim() : null,
                    Position = parts.Length > 6 ? parts[6].Trim() : null
                };

                // Check duplicate
                var exists = await _db.Admins.AnyAsync(a => a.Username == admin.Username);
                if (exists)
                {
                    failed++;
                    continue;
                }

                _db.Admins.Add(admin);
                imported++;
            }
            catch
            {
                failed++;
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"导入完成：成功 {imported} 条，失败 {failed} 条";
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("班主任模板");

        var headers = new[] { "用户名", "密码", "姓名", "性别", "民族", "出生日期",
            "户口所在地", "最高学历", "教师资格证科目", "教师资格证号",
            "教师资格所属教育局", "手机号", "学段", "角色", "年级", "班级", "职务" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        ws.Columns().AdjustToContents();
        for (int col = 1; col <= headers.Length; col++)
            if (ws.Column(col).Width < 14) ws.Column(col).Width = 14;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "班主任导入模板.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "请上传文件" });

        if (file.Length > 20 * 1024 * 1024)
            return Json(new { success = false, message = "Excel文件不能超过20MB" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx")
            return Json(new { success = false, message = "仅支持 .xlsx 格式" });

        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet(1);
            var range = ws.RangeUsed();
            if (range == null || range.RowCount() < 2)
                return Json(new { success = false, message = "文件为空或只有表头" });

            int successCount = 0, skipCount = 0;
            var existingUsernames = new HashSet<string>(await _db.Admins.Select(a => a.Username).ToListAsync());

            // 从第 2 行开始（第 1 行是表头）
            for (int row = 2; row <= range.RowCount(); row++)
            {
                var username = ws.Cell(row, 1).GetString().Trim();
                var password = ws.Cell(row, 2).GetString().Trim();
                var realName = ws.Cell(row, 3).GetString().Trim();

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(realName))
                {
                    skipCount++;
                    continue;
                }

                var pwdError = StudentManagerCore.Services.PasswordHelper.Validate(password);
                if (pwdError != null)
                {
                    skipCount++;
                    continue;
                }

                if (existingUsernames.Contains(username))
                {
                    skipCount++;
                    continue;
                }

                // 出生日期解析
                DateTime? birthDate = null;
                try { birthDate = ws.Cell(row, 6).GetDateTime(); } catch { }

                var admin = new Models.Admin
                {
                    Username = username,
                    Password = StudentManagerCore.Services.PasswordHelper.Hash(password),
                    RealName = realName,
                    Gender = CellOrNull(ws, row, 4),
                    Nation = CellOrNull(ws, row, 5),
                    BirthDate = birthDate,
                    RegisteredDomicile = CellOrNull(ws, row, 7),
                    HighestEducation = CellOrNull(ws, row, 8),
                    CertSubject = CellOrNull(ws, row, 9),
                    CertNumber = CellOrNull(ws, row, 10),
                    CertAuthority = CellOrNull(ws, row, 11),
                    Phone = CellOrNull(ws, row, 12),
                    SchoolType = CellOrNull(ws, row, 13),
                    Role = ws.Cell(row, 14).GetString().Trim().PadRight(10)[..10],
                    Grade = CellOrNull(ws, row, 15),
                    ClassName = CellOrNull(ws, row, 16),
                    Position = CellOrNull(ws, row, 17),
                    CreateTime = DateTime.Now
                };

                _db.Admins.Add(admin);
                existingUsernames.Add(username);
                successCount++;
            }

            await _db.SaveChangesAsync();

            await _auditService.LogAsync("导入", $"导入教职工: 新增 {successCount} 人, 跳过 {skipCount} 人");

            return Json(new
            {
                success = true,
                message = $"导入成功！新增 {successCount} 人，跳过 {skipCount} 人（空行/已存在）"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "导入失败：" + ex.Message });
        }
    }

    private static string? CellOrNull(IXLWorksheet ws, int row, int col)
    {
        var val = ws.Cell(row, col).GetString().Trim();
        return string.IsNullOrEmpty(val) ? null : val;
    }

    // ========== 所教科目管理 API ==========

    private static readonly HashSet<string> PrimaryGrades = new()
        { "一年级", "二年级", "三年级", "四年级", "五年级", "六年级" };
    private static readonly HashSet<string> MiddleGrades = new()
        { "七年级", "八年级", "九年级" };

    /// <summary>
    /// 获取某学段下的所有年级集合
    /// </summary>
    private static HashSet<string> GetGradeSet(string? schoolType) => schoolType switch
    {
        "小学" => PrimaryGrades,
        "初中" => MiddleGrades,
        _ => new HashSet<string>()
    };

    [HttpGet]
    public IActionResult GetSubjectsByType(string? type, string? grade)
    {
        var query = _db.Subjects.AsQueryable();

        if (!string.IsNullOrWhiteSpace(grade))
        {
            // 按具体年级筛选：该校通用的科目 + 该年级的科目
            query = query.Where(s => s.Grade == null || s.Grade == grade);
        }
        else if (!string.IsNullOrWhiteSpace(type))
        {
            // 按学段筛选
            var grades = GetGradeSet(type);
            if (grades.Count > 0)
                query = query.Where(s => s.Grade == null || grades.Contains(s.Grade));
            else
                query = query.Where(s => s.Grade != null && s.Grade == type);
        }
        // 按名称去重，每个科目只显示一次
        var list = query
            .GroupBy(s => s.Name!)
            .Select(g => new { Id = g.Min(s => s.Id), Name = g.Key })
            .OrderBy(s => s.Id)
            .ToList();
        return Json(list);
    }

    [HttpGet]
    public async Task<IActionResult> GetTeacherSubjects(int adminId)
    {
        // 查出该教师已关联的科目名称，返回对应的代表ID
        var names = await _db.TeacherSubjects
            .Where(ts => ts.AdminId == adminId)
            .Join(_db.Subjects, ts => ts.SubjectId, s => s.Id, (ts, s) => s.Name)
            .Distinct()
            .ToListAsync();

        var repIds = await _db.Subjects
            .Where(s => names.Contains(s.Name!))
            .GroupBy(s => s.Name!)
            .Select(g => g.Min(s => s.Id))
            .ToListAsync();
        return Json(repIds);
    }

    [HttpGet]
    public IActionResult GetClassesByGrade(string schoolType, string gradeName)
    {
        if (string.IsNullOrWhiteSpace(schoolType) || string.IsNullOrWhiteSpace(gradeName))
            return Json(new List<object>());

        int entryYear = GradeHelper.GradeToEntryYear(gradeName, DateTime.Now.Year);

        var gradeLevel = _db.GradeLevels
            .FirstOrDefault(gl => gl.EntryYear == entryYear && gl.SchoolType == schoolType);

        if (gradeLevel == null)
            return Json(new List<object>());

        var classes = _db.ClassInfos
            .Where(c => c.GradeLevelID == gradeLevel.GradeLevelID)
            .OrderBy(c => c.ClassName)
            .Select(c => new { id = c.ClassInfoID, name = c.ClassName })
            .ToList();

        return Json(classes);
    }

    /// <summary>
    /// 将代表科目ID展开为该学段下所有匹配的 Subject.Id
    /// </summary>
    private async Task<List<int>> ExpandSubjectIds(List<int> repIds, string? schoolType)
    {
        var names = await _db.Subjects
            .Where(s => repIds.Contains(s.Id))
            .Select(s => s.Name!)
            .Distinct()
            .ToListAsync();

        var grades = GetGradeSet(schoolType);
        return await _db.Subjects
            .Where(s => names.Contains(s.Name!))
            .Where(s => s.Grade == null || grades.Contains(s.Grade))
            .Select(s => s.Id)
            .ToListAsync();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTeacherSubjects(int adminId, string? schoolType, [FromBody] List<int> subjectIds)
    {
        try
        {
            var existing = await _db.TeacherSubjects.Where(ts => ts.AdminId == adminId).ToListAsync();
            _db.TeacherSubjects.RemoveRange(existing);

            if (subjectIds != null && subjectIds.Count > 0)
            {
                var allIds = await ExpandSubjectIds(subjectIds, schoolType);
                foreach (var subjId in allIds)
                {
                    _db.TeacherSubjects.Add(new TeacherSubject
                    {
                        AdminId = adminId,
                        SubjectId = subjId
                    });
                }
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}
