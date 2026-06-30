using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

[Authorize]
public class AnnounceController : Controller
{
    private readonly AppDbContext _db;

    public AnnounceController(AppDbContext db)
    {
        _db = db;
    }

    // GET: /Announce
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> Index()
    {
        var list = await _db.Announcements
            .OrderByDescending(a => a.CreateTime)
            .ToListAsync();

        // 已读统计
        var readCounts = await _db.AnnouncementReads
            .GroupBy(r => r.AnnouncementId)
            .Select(g => new { AnnouncementId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.AnnouncementId, g => g.Count);

        var totalTeachers = await _db.Admins
            .CountAsync(a => a.Role != null && !a.Role.Contains("管理员")
                && (a.Status == null || a.Status != "已删除"));

        ViewBag.ReadCounts = readCounts;
        ViewBag.TotalTeachers = totalTeachers;
        return View(list);
    }

    // GET: /Announce/Create
    [Authorize(Roles = "管理员")]
    public IActionResult Create()
    {
        return View();
    }

    // POST: /Announce/Create
    [HttpPost]
    [Authorize(Roles = "管理员")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Announcement model)
    {
        if (string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.Content))
        {
            ModelState.AddModelError("", "标题和内容不能为空");
            return View(model);
        }

        model.CreateTime = DateTime.Now;
        model.CreatedBy = User.FindFirst("RealName")?.Value ?? User.Identity?.Name ?? "";

        _db.Announcements.Add(model);
        await _db.SaveChangesAsync();

        await LogOperation("发布公告", model.Id, model.Title, $"发布公告: {model.Title}");

        TempData["Success"] = "公告已发布";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Announce/Edit/5
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> Edit(int id)
    {
        var ann = await _db.Announcements.FindAsync(id);
        if (ann == null) return NotFound();
        return View(ann);
    }

    // POST: /Announce/Edit/5
    [HttpPost]
    [Authorize(Roles = "管理员")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Announcement model)
    {
        if (id != model.Id) return NotFound();

        if (string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.Content))
        {
            ModelState.AddModelError("", "标题和内容不能为空");
            return View(model);
        }

        var existing = await _db.Announcements.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Title = model.Title;
        existing.TargetRole = model.TargetRole;
        existing.Content = model.Content;

        await _db.SaveChangesAsync();

        await LogOperation("编辑公告", id, model.Title, $"编辑公告: {model.Title}");

        TempData["Success"] = "公告已保存";
        return RedirectToAction(nameof(Index));
    }

    // GET: /Announce/Delete/5
    [Authorize(Roles = "管理员")]
    public async Task<IActionResult> Delete(int id)
    {
        var ann = await _db.Announcements.FindAsync(id);
        if (ann == null) return NotFound();

        var title = ann.Title;
        _db.Announcements.Remove(ann);
        // 也删除相关的已读记录
        var reads = await _db.AnnouncementReads.Where(r => r.AnnouncementId == id).ToListAsync();
        _db.AnnouncementReads.RemoveRange(reads);
        await _db.SaveChangesAsync();

        await LogOperation("删除公告", id, title, $"删除公告: {title}");

        TempData["Success"] = "公告已删除";
        return RedirectToAction(nameof(Index));
    }

    // POST: /Announce/MarkAsRead
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAsRead(int announcementId)
    {
        var phone = User.FindFirst(ClaimTypes.MobilePhone)?.Value
                    ?? User.FindFirst("Phone")?.Value
                    ?? User.Identity?.Name ?? "";
        if (string.IsNullOrEmpty(phone)) return BadRequest("无法获取用户手机号");

        // 防止重复
        var exists = await _db.AnnouncementReads
            .AnyAsync(r => r.AnnouncementId == announcementId && r.TeacherPhone == phone);
        if (!exists)
        {
            _db.AnnouncementReads.Add(new AnnouncementRead
            {
                AnnouncementId = announcementId,
                TeacherPhone = phone,
                ReadTime = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        return Ok();
    }

    private async Task LogOperation(string actionType, int targetId, string targetName, string detail)
    {
        var operatorName = User.FindFirst("RealName")?.Value ?? User.Identity?.Name ?? "未知";
        var operatorRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "管理员";

        _db.OperationLogs.Add(new OperationLog
        {
            OperatorName = operatorName,
            OperatorRole = operatorRole,
            ActionType = actionType,
            TargetNo = targetId.ToString(),
            TargetName = targetName,
            Detail = detail,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }
}
