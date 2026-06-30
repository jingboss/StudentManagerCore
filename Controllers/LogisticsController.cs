using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;

namespace StudentManagerCore.Controllers;

[Authorize]
public class LogisticsController : Controller
{
    private readonly AppDbContext _db;

    public LogisticsController(AppDbContext db)
    {
        _db = db;
    }

    private string GetCurrentRole()
    {
        return User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value?.Trim() ?? "";
    }

    private int GetCurrentAdminId()
    {
        var idStr = User.FindFirst("AdminID")?.Value ?? "";
        int.TryParse(idStr, out var id);
        return id;
    }

    private string GetCurrentRealName()
    {
        return User.FindFirst("RealName")?.Value ?? "";
    }

    /// <summary>维修清单（后勤主任/管理员查看所有）</summary>
    [Authorize(Roles = "管理员,后勤主任")]
    public async Task<IActionResult> Index(string status, string keyword, int page = 1)
    {
        var query = _db.RepairRequests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(r => r.Title.Contains(keyword) || (r.CreatorName != null && r.CreatorName.Contains(keyword)));

        var total = await query.CountAsync();
        var pageSize = 20;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var list = await query
            .OrderByDescending(r => r.CreateTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.Status = status;
        ViewBag.Keyword = keyword;

        return View(list);
    }

    /// <summary>我的申报（普通用户查看自己的申请）</summary>
    public async Task<IActionResult> MyRequests(int page = 1)
    {
        var adminId = GetCurrentAdminId();
        var query = _db.RepairRequests.Where(r => r.CreatedBy == adminId);

        var total = await query.CountAsync();
        var pageSize = 20;
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var list = await query
            .OrderByDescending(r => r.CreateTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;

        return View(list);
    }

    /// <summary>申请维修</summary>
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RepairRequest model, string? PreferredDate)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            ModelState.AddModelError("Title", "请填写维修标题");
        if (string.IsNullOrWhiteSpace(model.Description))
            ModelState.AddModelError("Description", "请填写故障描述");
        if (string.IsNullOrWhiteSpace(model.Location))
            ModelState.AddModelError("Location", "请填写故障位置");
        if (string.IsNullOrWhiteSpace(model.ContactPhone))
            ModelState.AddModelError("ContactPhone", "请填写联系电话");
        if (string.IsNullOrWhiteSpace(PreferredDate))
            ModelState.AddModelError("PreferredDate", "请选择故障出现日期");

        if (!ModelState.IsValid)
            return View(model);

        model.CreateTime = DateTime.Now;
        model.CreatedBy = GetCurrentAdminId();
        model.CreatorName = GetCurrentRealName();
        model.Status = "待处理";

        // 合并日期和时间
        if (!string.IsNullOrWhiteSpace(PreferredDate) && DateTime.TryParse(PreferredDate, out var date))
        {
            model.PreferredTime = date;

            if (int.TryParse(Request.Form["PreferredHour"], out var hour) && hour >= 0 && hour <= 23)
            {
                model.PreferredTime = model.PreferredTime.Value.AddHours(hour);

                if (int.TryParse(Request.Form["PreferredMinute"], out var min) && min >= 0 && min <= 59)
                {
                    model.PreferredTime = model.PreferredTime.Value.AddMinutes(min);
                }
            }
        }

        _db.RepairRequests.Add(model);
        await _db.SaveChangesAsync();

        TempData["Success"] = "维修申请已提交，请等待后勤处理。";
        return RedirectToAction("MyRequests");
    }

    /// <summary>处理维修（仅后勤主任）</summary>
    [Authorize(Roles = "后勤主任")]
    public async Task<IActionResult> Process(int id)
    {
        var request = await _db.RepairRequests.FindAsync(id);
        if (request == null)
            return NotFound();

        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "后勤主任")]
    public async Task<IActionResult> Process(int id, string status, string remark)
    {
        var request = await _db.RepairRequests.FindAsync(id);
        if (request == null)
            return NotFound();

        if (!string.IsNullOrWhiteSpace(status))
            request.Status = status;

        if (!string.IsNullOrWhiteSpace(remark))
            request.Remark = remark;

        request.ProcessTime = DateTime.Now;
        request.ProcessedBy = GetCurrentAdminId();
        request.ProcessorName = GetCurrentRealName();

        await _db.SaveChangesAsync();

        TempData["Success"] = "维修单已处理。";
        return RedirectToAction("Index");
    }

    /// <summary>获取未处理数量（用于菜单徽标）</summary>
    [HttpGet]
    public async Task<IActionResult> GetPendingCount()
    {
        var count = await _db.RepairRequests.CountAsync(r => r.Status == "待处理");
        return Json(new { count });
    }
}
