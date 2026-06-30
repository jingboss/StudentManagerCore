using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using System.Security.Claims;
using ClosedXML.Excel;

namespace StudentManagerCore.Controllers;

[Authorize]
public class AdminCenterController : Controller
{
    private readonly AppDbContext _db;

    public AdminCenterController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>校验当前用户是否为管理员，否就返回 403</summary>
    private bool RequireAdmin()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        if (role != "管理员")
            return false;
        return true;
    }

    private IActionResult ForbidJson(string message = "权限不足")
    {
        return Json(new { success = false, message });
    }

    public async Task<IActionResult> Index()
    {
        if (!RequireAdmin()) return Forbid();
        var admins = await _db.Admins
            .Where(a => a.Role != null && a.Role.Contains("管理员"))
            .OrderByDescending(a => a.AdminID)
            .ToListAsync();
        var sc = await _db.SiteConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "SecurityCode");
        ViewBag.SecurityCode = sc?.ConfigValue ?? "";
        return View(admins);
    }

    /// <summary>个人中心 — 查看/修改个人信息（支持模态框 AJAX）</summary>
    public async Task<IActionResult> Profile()
    {
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        if (!int.TryParse(adminIdStr, out int adminId)) return NotFound();
        var admin = await _db.Admins.FindAsync(adminId);
        if (admin == null) return NotFound();

        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        ViewBag.IsAdmin = role.Trim() == "管理员";
        ViewBag.Role = role.Trim();

        var perms = (admin.Permissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool hasPerm(string p) => perms.Contains(p);
        ViewBag.CanEditBasic = ViewBag.IsAdmin || hasPerm("profile_basic");
        ViewBag.CanEditPhone = ViewBag.IsAdmin || hasPerm("profile_phone");
        ViewBag.CanEditIdCard = ViewBag.IsAdmin || hasPerm("profile_idcard");
        ViewBag.CanEditCert = ViewBag.IsAdmin || hasPerm("profile_cert");
        ViewBag.CanEditPassword = true;

        admin.Password = "";

        if (IsAjaxRequest())
            return PartialView(admin);
        return View(admin);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(Models.Admin model)
    {
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        if (!int.TryParse(adminIdStr, out int adminId)) return NotFound();
        var existing = await _db.Admins.FindAsync(adminId);
        if (existing == null) return NotFound();

        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var isAdmin = role.Trim() == "管理员";
        var perms = (existing.Permissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool hasPerm(string p) => isAdmin || perms.Contains(p);

        if (hasPerm("profile_basic"))
        {
            existing.RealName = model.RealName;
            existing.Gender = model.Gender;
            existing.Nation = model.Nation;
            existing.BirthDate = model.BirthDate;
            existing.RegisteredDomicile = model.RegisteredDomicile;
            existing.HighestEducation = model.HighestEducation;
        }
        if (hasPerm("profile_phone"))
        {
            var phoneExists = await _db.Admins.AnyAsync(a => a.Phone == model.Phone && a.AdminID != existing.AdminID);
            if (phoneExists)
            {
                if (IsAjaxRequest()) return Json(new { success = false, message = "手机号已被使用" });
                ModelState.AddModelError("Phone", "手机号已被使用");
                SetProfileViewBag(isAdmin, perms);
                return View(existing);
            }
            existing.Phone = model.Phone;
        }
        if (hasPerm("profile_idcard"))
        {
            existing.IDCardNumber = model.IDCardNumber;
        }
        if (hasPerm("profile_cert"))
        {
            existing.CertSubject = model.CertSubject;
            existing.CertNumber = model.CertNumber;
            existing.CertAuthority = model.CertAuthority;
        }
        // 密码修改（所有用户均可修改，需验证旧密码）
        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            // 管理员直接从表单获取新密码
            if (isAdmin)
            {
                existing.Password = PasswordHelper.Hash(model.Password);
            }
            else
            {
                // 非管理员：前端通过 Password 字段传 "旧密码|新密码" 格式
                var parts = (model.Password ?? "").Split('|');
                var oldPassword = parts.Length > 0 ? parts[0] : "";
                var newPassword = parts.Length > 1 ? parts[1] : "";

                if (string.IsNullOrWhiteSpace(oldPassword))
                {
                    ModelState.AddModelError("Password", "请输入原密码");
                    SetProfileViewBag(isAdmin, perms);
                    return View(existing);
                }
                if (!PasswordHelper.Verify(oldPassword, existing.Password))
                {
                    ModelState.AddModelError("Password", "原密码错误");
                    SetProfileViewBag(isAdmin, perms);
                    return View(existing);
                }
                // 使用新规则验证（至少8位、字母+数字）
                var pwdError = ValidatePassword(newPassword);
                if (pwdError != null)
                {
                    ModelState.AddModelError("Password", pwdError);
                    SetProfileViewBag(isAdmin, perms);
                    return View(existing);
                }
                existing.Password = PasswordHelper.Hash(newPassword);
            }
        }

        await _db.SaveChangesAsync();

        if (IsAjaxRequest())
            return Json(new { success = true, message = "保存成功" });

        TempData["Success"] = "保存成功";
        return RedirectToAction("Profile");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
    {
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        if (!int.TryParse(adminIdStr, out int adminId))
            return Json(new { success = false, message = "用户未登录" });

        var admin = await _db.Admins.FindAsync(adminId);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        if (string.IsNullOrWhiteSpace(oldPassword))
            return Json(new { success = false, message = "请输入原密码" });

        if (!PasswordHelper.Verify(oldPassword, admin.Password))
            return Json(new { success = false, message = "原密码错误" });

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return Json(new { success = false, message = "新密码至少8位，需包含字母和数字" });

        var pwdError = PasswordHelper.Validate(newPassword);
        if (pwdError != null)
            return Json(new { success = false, message = pwdError });

        admin.Password = PasswordHelper.Hash(newPassword);
        await _db.SaveChangesAsync();

        // 记录操作日志
        _db.OperationLogs.Add(new OperationLog
        {
            OperatorName = admin.RealName ?? "",
            OperatorRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "",
            ActionType = "修改密码",
            Detail = $"{admin.RealName} 修改了自己的密码",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();

        return Json(new { success = true, message = "密码修改成功" });
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }

    private void SetProfileViewBag(bool isAdmin, string[] perms)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        ViewBag.IsAdmin = isAdmin;
        ViewBag.Role = role.Trim();
        ViewBag.CanEditBasic = isAdmin || perms.Contains("profile_basic");
        ViewBag.CanEditPhone = isAdmin || perms.Contains("profile_phone");
        ViewBag.CanEditIdCard = isAdmin || perms.Contains("profile_idcard");
        ViewBag.CanEditCert = isAdmin || perms.Contains("profile_cert");
        ViewBag.CanEditPassword = true;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (!RequireAdmin()) return ForbidJson();
        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        // Prevent deleting the super admin
        if (admin.Username == "13423297227")
            return Json(new { success = false, message = "不能删除超级管理员" });

        _db.Admins.Remove(admin);
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "删除成功" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string username, string password, string realName, string phone, string role, string grade, string className, string position)
    {
        if (!RequireAdmin()) return ForbidJson();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Json(new { success = false, message = "用户名和密码不能为空" });

        var exists = await _db.Admins.AnyAsync(a => a.Username == username);
        if (exists)
            return Json(new { success = false, message = "用户名已存在" });

        var admin = new Models.Admin
        {
            Username = username,
            Password = PasswordHelper.Hash(password),
            RealName = realName,
            Phone = phone,
            Role = (role ?? "管理员"),
            Grade = grade,
            ClassName = className,
            Position = position
        };

        _db.Admins.Add(admin);
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "添加成功" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string? username, string? password, string? realName, string? phone)
    {
        if (!RequireAdmin()) return ForbidJson();
        var admin = await _db.Admins.FindAsync(id);
        if (admin == null)
            return Json(new { success = false, message = "用户不存在" });

        if (!string.IsNullOrWhiteSpace(username))
        {
            var exists = await _db.Admins.AnyAsync(a => a.Username == username && a.AdminID != id);
            if (exists)
                return Json(new { success = false, message = "用户名已被使用" });
            admin.Username = username;
        }

        if (!string.IsNullOrWhiteSpace(password))
            admin.Password = PasswordHelper.Hash(password);

        if (!string.IsNullOrWhiteSpace(realName))
            admin.RealName = realName;

        admin.Phone = phone;

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "修改成功" });
    }

    /// <summary>批量学生权限管理页面</summary>
    public async Task<IActionResult> StudentPermissions()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role.Trim() != "管理员")
            return RedirectToAction("Index", "Home");

        // 获取所有非管理员用户（班主任/教师）
        var teachers = await _db.Admins
            .Where(a => a.RealName != null && a.RealName.Trim() != "")
            .Where(a => a.Role == null || !a.Role.Contains("管理员"))
            .OrderBy(a => a.RealName)
            .ToListAsync();

        return View(teachers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchUpdateStudentPermissions([FromBody] List<StudentPermUpdateModel> updates)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role.Trim() != "管理员")
            return Json(new { success = false, message = "无权限" });

        if (updates == null || updates.Count == 0)
            return Json(new { success = false, message = "没有需要更新的数据" });

        foreach (var update in updates)
        {
            var admin = await _db.Admins.FindAsync(update.Id);
            if (admin == null) continue;

            // 保留其他权限组，只更新学生权限
            var allPerms = (admin.Permissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !p.StartsWith("student_")).ToList();

            if (update.EditStudent) allPerms.Add("student_edit");
            if (update.DeleteStudent) allPerms.Add("student_delete");
            if (update.AddStudent) allPerms.Add("student_add");

            admin.Permissions = string.Join(",", allPerms);
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "学生权限更新成功" });
    }

    /// <summary>批量教职工权限管理页面</summary>
    public async Task<IActionResult> TeacherPermissions()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role.Trim() != "管理员")
            return RedirectToAction("Index", "Home");

        var teachers = await _db.Admins
            .Where(a => a.RealName != null && a.RealName.Trim() != "")
            .Where(a => a.Role == null || !a.Role.Contains("管理员"))
            .OrderBy(a => a.RealName)
            .ToListAsync();

        return View(teachers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchUpdateTeacherPermissions([FromBody] List<TeacherPermUpdateModel> updates)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role.Trim() != "管理员")
            return Json(new { success = false, message = "无权限" });

        if (updates == null || updates.Count == 0)
            return Json(new { success = false, message = "没有需要更新的数据" });

        foreach (var update in updates)
        {
            var admin = await _db.Admins.FindAsync(update.Id);
            if (admin == null) continue;

            // 保留学生权限，只更新个人中心权限
            var allPerms = (admin.Permissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p.StartsWith("student_")).ToList();

            if (update.ProfileBasic) allPerms.Add("profile_basic");
            if (update.ProfilePhone) allPerms.Add("profile_phone");
            if (update.ProfileIdCard) allPerms.Add("profile_idcard");
            if (update.ProfileCert) allPerms.Add("profile_cert");

            admin.Permissions = string.Join(",", allPerms);
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "教职工权限更新成功" });
    }

    [HttpGet]
    public async Task<IActionResult> OperationLogs(int page = 1, int pageSize = 20, string? actionType = null, string? keyword = null)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        if (role != "管理员")
            return Forbid();

        var query = _db.OperationLogs.AsQueryable();

        // 筛选
        if (!string.IsNullOrWhiteSpace(actionType) && actionType != "全部")
            query = query.Where(o => o.ActionType == actionType);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(o =>
                (o.OperatorName != null && o.OperatorName.Contains(keyword)) ||
                (o.Detail != null && o.Detail.Contains(keyword)) ||
                (o.TargetName != null && o.TargetName.Contains(keyword)));

        // 统计
        var totalCount = await _db.OperationLogs.CountAsync();
        var todayStart = DateTime.Today;
        var todayCount = await _db.OperationLogs.CountAsync(o => o.CreateTime >= todayStart);

        query = query.OrderByDescending(o => o.CreateTime);
        var totalFiltered = await query.CountAsync();
        var logs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)totalFiltered / pageSize);
        ViewBag.TotalCount = totalCount;
        ViewBag.TodayCount = todayCount;
        ViewBag.FilterActionType = actionType;
        ViewBag.FilterKeyword = keyword;
        return View(logs);
    }

    [HttpGet]
    public async Task<IActionResult> ExportLogs(string? actionType = null, string? keyword = null)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        if (role != "管理员") return Forbid();

        var query = _db.OperationLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(actionType) && actionType != "全部")
            query = query.Where(o => o.ActionType == actionType);
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(o =>
                (o.OperatorName != null && o.OperatorName.Contains(keyword)) ||
                (o.Detail != null && o.Detail.Contains(keyword)) ||
                (o.TargetName != null && o.TargetName.Contains(keyword)));

        var logs = await query.OrderByDescending(o => o.CreateTime).ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("操作日志");
        ws.Cell(1, 1).Value = "序号";
        ws.Cell(1, 2).Value = "操作人";
        ws.Cell(1, 3).Value = "角色";
        ws.Cell(1, 4).Value = "操作类型";
        ws.Cell(1, 5).Value = "目标";
        ws.Cell(1, 6).Value = "详情";
        ws.Cell(1, 7).Value = "IP地址";
        ws.Cell(1, 8).Value = "操作时间";
        var header = ws.Range(1, 1, 1, 8);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var log in logs)
        {
            ws.Cell(row, 1).Value = row - 1;
            ws.Cell(row, 2).Value = log.OperatorName;
            ws.Cell(row, 3).Value = log.OperatorRole;
            ws.Cell(row, 4).Value = log.ActionType;
            ws.Cell(row, 5).Value = log.TargetName;
            ws.Cell(row, 6).Value = log.Detail;
            ws.Cell(row, 7).Value = log.IpAddress;
            ws.Cell(row, 8).Value = log.CreateTime.ToString("yyyy-MM-dd HH:mm:ss");
            row++;
        }
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"操作日志_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearLogs()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        if (role != "管理员") return Forbid();

        _db.OperationLogs.RemoveRange(_db.OperationLogs);
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "所有操作日志已清空" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSecurityCode(string? securityCode)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "";
        if (role != "管理员") return Forbid();

        var sc = await _db.SiteConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "SecurityCode");
        if (string.IsNullOrEmpty(securityCode))
        {
            return Json(new { success = true, message = "安全码未变更" });
        }
        if (sc == null)
        {
            sc = new SiteConfig { ConfigKey = "SecurityCode", ConfigValue = PasswordHelper.Hash(securityCode) };
            _db.SiteConfigs.Add(sc);
        }
        else
        {
            sc.ConfigValue = PasswordHelper.Hash(securityCode);
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "安全码已保存" });
    }

    /// <summary>密码规则：至少8位，包含字母和数字</summary>
    private static string? ValidatePassword(string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return "新密码至少8位，包含字母和数字";
        if (!System.Text.RegularExpressions.Regex.IsMatch(newPassword, "[a-zA-Z]"))
            return "新密码必须包含字母";
        if (!System.Text.RegularExpressions.Regex.IsMatch(newPassword, "[0-9]"))
            return "新密码必须包含数字";
        return null;
    }
}

public class StudentPermUpdateModel
{
    public int Id { get; set; }
    public bool EditStudent { get; set; }
    public bool DeleteStudent { get; set; }
    public bool AddStudent { get; set; }
}

public class TeacherPermUpdateModel
{
    public int Id { get; set; }
    public bool ProfileBasic { get; set; }
    public bool ProfilePhone { get; set; }
    public bool ProfileIdCard { get; set; }
    public bool ProfileCert { get; set; }
}
