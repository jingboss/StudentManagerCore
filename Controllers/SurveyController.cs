using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;
using System.Text.Json;

namespace StudentManagerCore.Controllers;

[Authorize]
public class SurveyController : Controller
{
    private readonly AppDbContext _db;

    public SurveyController(AppDbContext db)
    {
        _db = db;
    }

    // ========== 问卷列表 ==========
    public async Task<IActionResult> Index(string? status)
    {
        var query = _db.Surveys.AsQueryable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(s => s.Status == status);
        var list = await query.OrderByDescending(s => s.CreateTime).ToListAsync();
        ViewBag.Status = status;
        return View(list);
    }

    // ========== 创建问卷 ==========
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Survey model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError("Title", "请输入问卷标题");
            return View(model);
        }

        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        int.TryParse(adminIdStr, out var adminId);
        model.CreatedBy = adminId;
        model.CreatorName = User.FindFirst("RealName")?.Value ?? User.Identity?.Name ?? "";
        model.Status = "草稿";
        model.CreateTime = DateTime.Now;

        _db.Surveys.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "问卷创建成功";
        return RedirectToAction("Edit", new { id = model.Id });
    }

    // ========== 编辑问卷 ==========
    public async Task<IActionResult> Edit(int id)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
                .ThenInclude(q => q.Options.OrderBy(o => o.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (survey == null) return NotFound();
        return View(survey);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Survey model)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey == null) return NotFound();

        survey.Title = model.Title;
        survey.Description = model.Description;
        survey.UpdateTime = DateTime.Now;
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "已保存" });
    }

    // ========== 发布/关闭问卷 ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, string status)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey == null) return Json(new { success = false, message = "问卷不存在" });

        if (status == "发布" && survey.Status == "草稿")
        {
            var hasQuestions = await _db.SurveyQuestions.AnyAsync(q => q.SurveyId == id);
            if (!hasQuestions)
                return Json(new { success = false, message = "请先添加题目后再发布" });
        }

        survey.Status = status;
        survey.UpdateTime = DateTime.Now;
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = $"问卷已{status}" });
    }

    // ========== 删除问卷 ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var survey = await _db.Surveys.FindAsync(id);
        if (survey == null) return Json(new { success = false, message = "问卷不存在" });
        _db.Surveys.Remove(survey);
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "已删除" });
    }

    // ========== 题目管理 ==========
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(int surveyId, string type, string title, bool isRequired, List<string>? options)
    {
        var maxOrder = await _db.SurveyQuestions
            .Where(q => q.SurveyId == surveyId)
            .MaxAsync(q => (int?)q.SortOrder) ?? 0;

        var question = new SurveyQuestion
        {
            SurveyId = surveyId,
            SortOrder = maxOrder + 1,
            Type = type,
            Title = title,
            IsRequired = isRequired
        };
        _db.SurveyQuestions.Add(question);
        await _db.SaveChangesAsync();

        // 添加选项
        if ((type == "单选" || type == "多选") && options != null && options.Count > 0)
        {
            for (int i = 0; i < options.Count; i++)
            {
                _db.SurveyQuestionOptions.Add(new SurveyQuestionOption
                {
                    QuestionId = question.Id,
                    SortOrder = i + 1,
                    OptionText = options[i]
                });
            }
            await _db.SaveChangesAsync();
        }

        return Json(new { success = true, id = question.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuestion(int id, string title, bool isRequired)
    {
        var q = await _db.SurveyQuestions.FindAsync(id);
        if (q == null) return Json(new { success = false, message = "题目不存在" });
        q.Title = title;
        q.IsRequired = isRequired;
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteQuestion(int id)
    {
        var q = await _db.SurveyQuestions.FindAsync(id);
        if (q == null) return Json(new { success = false, message = "题目不存在" });
        _db.SurveyQuestions.Remove(q);
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReorderQuestions(int surveyId, List<int> questionIds)
    {
        for (int i = 0; i < questionIds.Count; i++)
        {
            var q = await _db.SurveyQuestions.FindAsync(questionIds[i]);
            if (q != null) q.SortOrder = i + 1;
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    // ========== 答卷查看 ==========
    public async Task<IActionResult> Responses(int id)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (survey == null) return NotFound();

        var submissions = await _db.SurveySubmissions
            .Where(s => s.SurveyId == id)
            .OrderByDescending(s => s.SubmitTime)
            .ToListAsync();

        ViewBag.Survey = survey;
        return View(submissions);
    }

    public async Task<IActionResult> ResponseDetail(int submissionId)
    {
        var submission = await _db.SurveySubmissions
            .Include(s => s.Survey).ThenInclude(s => s.Questions.OrderBy(q => q.SortOrder)).ThenInclude(q => q.Options)
            .Include(s => s.Answers)
            .FirstOrDefaultAsync(s => s.Id == submissionId);
        if (submission == null) return NotFound();
        return View(submission);
    }

    // ========== 导出Excel ==========
    public async Task<IActionResult> Export(int id)
    {
        var survey = await _db.Surveys
            .Include(s => s.Questions.OrderBy(q => q.SortOrder))
            .FirstOrDefaultAsync(s => s.Id == id);
        if (survey == null) return NotFound();

        var submissions = await _db.SurveySubmissions
            .Where(s => s.SurveyId == id)
            .Include(s => s.Answers)
            .OrderBy(s => s.SubmitTime)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("答卷数据");

        // 表头
        ws.Cell(1, 1).Value = "提交时间";
        ws.Cell(1, 2).Value = "提交人";
        for (int i = 0; i < survey.Questions.Count; i++)
            ws.Cell(1, i + 3).Value = survey.Questions[i].Title;

        // 数据行
        for (int r = 0; r < submissions.Count; r++)
        {
            var sub = submissions[r];
            ws.Cell(r + 2, 1).Value = sub.SubmitTime.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(r + 2, 2).Value = sub.SubmitterName ?? sub.SubmittedBy ?? "";
            for (int c = 0; c < survey.Questions.Count; c++)
            {
                var q = survey.Questions[c];
                var answer = sub.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
                ws.Cell(r + 2, c + 3).Value = answer?.AnswerText ?? "";
            }
        }

        ws.Columns().AdjustToContents();

        var fileName = $"问卷_{survey.Title}_{DateTime.Now:yyyyMMdd}.xlsx";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
}
