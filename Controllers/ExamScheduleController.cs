using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

[Authorize(Roles = "管理员")]
public class ExamScheduleController : Controller
{
    private readonly AppDbContext _db;

    public ExamScheduleController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? keyword, string? examType, string? status)
    {
        var query = _db.ExamSchedules
            .Include(e => e.Semester)
                .ThenInclude(s => s!.AcademicYear)
            .Include(e => e.ExamSubjects)
                .ThenInclude(es => es.Subject)
            .AsQueryable();

        // 搜索筛选
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(e => e.Name.Contains(keyword));
        if (!string.IsNullOrWhiteSpace(examType) && examType != "全部")
            query = query.Where(e => e.ExamType == examType);
        if (!string.IsNullOrWhiteSpace(status) && status != "全部")
            query = query.Where(e => e.Status == status);

        var schedules = await query
            .OrderByDescending(e => e.ExamDate)
            .ToListAsync();

        ViewBag.Keyword = keyword;
        ViewBag.FilterExamType = examType;
        ViewBag.FilterStatus = status;
        ViewBag.Semesters = await _db.Semesters
            .Include(s => s.AcademicYear)
            .Where(s => s.AcademicYear != null)
            .OrderByDescending(s => s.AcademicYear!.YearName)
            .ThenByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id,
                Display = s.AcademicYear!.YearName + " " + s.SemesterName
            })
            .ToListAsync();

        var gradeLevels = await _db.GradeLevels
            .OrderByDescending(g => g.EntryYear)
            .ThenBy(g => g.SchoolType)
            .ToListAsync();
        ViewBag.Grades = gradeLevels
            .Select(g => new { Value = g.CurrentGradeName, Text = g.SchoolType + " - " + g.CurrentGradeName })
            .ToList();

        // 全部科目，用于科目多选
        ViewBag.AllSubjects = await _db.Subjects
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        // 所有教师列表（用于科任教师分配）
        ViewBag.AllTeachers = await _db.Admins
            .Where(a => a.Role != null && (a.Role.Contains("班主任") || a.Role.Contains("教师")))
            .OrderBy(a => a.RealName)
            .Select(a => new { a.AdminID, a.RealName, a.Role, a.ClassName })
            .ToListAsync();

        // 所有班级（用于教师分配对话框）
        ViewBag.AllClasses = await _db.ClassInfos
            .Include(c => c.GradeLevel)
            .OrderBy(c => c.GradeLevel!.EntryYear)
            .ThenBy(c => c.ClassName)
            .ToListAsync();

        // 该页所有考试的科任教师分配数据
        var scheduleIds = schedules.Select(s => s.Id).ToList();
        var allExamTeachers = await _db.ExamSubjectTeachers
            .Where(est => scheduleIds.Contains(est.ExamScheduleId))
            .Include(est => est.Admin)
            .Include(est => est.ClassInfo)
            .ToListAsync();
        ViewBag.AllExamTeachers = allExamTeachers;

        return View(schedules);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string examType, string? grades, string examDate, string? endDate, string semesterId, string status, string? subjectIds)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "考试名称不能为空" });
            if (string.IsNullOrWhiteSpace(examDate))
                return Json(new { success = false, message = "请选择考试日期" });
            if (string.IsNullOrWhiteSpace(semesterId) || !int.TryParse(semesterId, out var semId))
                return Json(new { success = false, message = "请选择学期" });

            // 验证学期是否存在
            var semester = await _db.Semesters.FindAsync(semId);
            if (semester == null)
                return Json(new { success = false, message = "所选学期不存在，请刷新页面重试" });
            if (!DateTime.TryParse(examDate, out var date))
                return Json(new { success = false, message = "日期格式错误" });

            DateTime? endDateTime = null;
            if (!string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out var parsedEnd))
                endDateTime = parsedEnd;

            var schedule = new ExamSchedule
            {
                Name = name.Trim(),
                ExamType = examType,
                Grades = grades,
                ExamDate = date,
                EndDate = endDateTime,
                SemesterId = semId,
                Status = status
            };
            _db.ExamSchedules.Add(schedule);
            await _db.SaveChangesAsync();

            // 保存关联科目
            if (!string.IsNullOrWhiteSpace(subjectIds))
            {
                var ids = subjectIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToList();
                foreach (var sid in ids)
                {
                    _db.ExamSubjects.Add(new ExamSubject { ExamScheduleId = schedule.Id, SubjectId = sid });
                }
                await _db.SaveChangesAsync();
            }

            await LogOperation("创建考试", schedule.Id, schedule.Name);

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string name, string examType, string? grades, string examDate, string? endDate, string semesterId, string status, string? subjectIds)
    {
        try
        {
            var schedule = await _db.ExamSchedules.Include(e => e.ExamSubjects).FirstOrDefaultAsync(e => e.Id == id);
            if (schedule == null)
                return Json(new { success = false, message = "记录不存在" });
            if (string.IsNullOrWhiteSpace(examDate) || !DateTime.TryParse(examDate, out var date))
                return Json(new { success = false, message = "日期格式错误" });
            if (!int.TryParse(semesterId, out var semId))
                return Json(new { success = false, message = "请选择学期" });

            // 验证学期是否存在
            var semester = await _db.Semesters.FindAsync(semId);
            if (semester == null)
                return Json(new { success = false, message = "所选学期不存在，请刷新页面重试" });

            DateTime? endDateTime = null;
            if (!string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out var parsedEnd))
                endDateTime = parsedEnd;

            schedule.Name = name.Trim();
            schedule.ExamType = examType;
            schedule.Grades = grades;
            schedule.ExamDate = date;
            schedule.EndDate = endDateTime;
            schedule.SemesterId = semId;
            schedule.Status = status;

            // 只有传了 subjectIds 才更新科目（兼容编辑弹窗已无科目选项的情况）
            if (subjectIds != null)
            {
                // 更新关联科目（先删后加）
                if (schedule.ExamSubjects != null && schedule.ExamSubjects.Any())
                {
                    _db.ExamSubjects.RemoveRange(schedule.ExamSubjects);
                    await _db.SaveChangesAsync();
                }

                if (!string.IsNullOrWhiteSpace(subjectIds))
                {
                    var ids = subjectIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s.Trim(), out var sid) ? sid : (int?)null)
                        .Where(sid => sid.HasValue)
                        .Select(sid => sid!.Value)
                        .ToList();
                    foreach (var sid in ids)
                    {
                        _db.ExamSubjects.Add(new ExamSubject { ExamScheduleId = schedule.Id, SubjectId = sid });
                    }
                }
            }

            await _db.SaveChangesAsync();

            await LogOperation("编辑考试", schedule.Id, schedule.Name);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetSubjects()
    {
        var subjects = await _db.Subjects
            .OrderBy(s => s.SortOrder)
            .Select(s => new { s.Id, s.Name, s.Grade, s.FullScore })
            .ToListAsync();
        return Json(subjects);
    }

    [HttpGet]
    public async Task<IActionResult> GetExamSubjects(int examScheduleId)
    {
        var data = await _db.ExamSubjects
            .Where(e => e.ExamScheduleId == examScheduleId)
            .Select(e => new { e.SubjectId, e.FullScore })
            .Distinct()
            .ToListAsync();
        return Json(data);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExamSubjects(int examScheduleId, [FromBody] List<ExamSubjectDto>? subjects)
    {
        try
        {
            var old = await _db.ExamSubjects
                .Where(e => e.ExamScheduleId == examScheduleId)
                .ToListAsync();
            _db.ExamSubjects.RemoveRange(old);

            if (subjects != null && subjects.Count > 0)
            {
                // 按 SubjectId 去重，防止重复提交导致重复科目
                var distinctSubjects = subjects
                    .GroupBy(s => s.SubjectId)
                    .Select(g => g.First())
                    .ToList();
                foreach (var s in distinctSubjects)
                {
                    _db.ExamSubjects.Add(new ExamSubject
                    {
                        ExamScheduleId = examScheduleId,
                        SubjectId = s.SubjectId,
                        FullScore = s.FullScore
                    });
                }
            }

            await _db.SaveChangesAsync();
            await LogOperation("配置考试科目", examScheduleId, $"考试ID:{examScheduleId} 已配置{subjects?.Count ?? 0}个科目");
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    /// <summary>
    /// 获取某考试各科目的考试时间设置
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetExamSubjectTimes(int examScheduleId)
    {
        try
        {
            var rawData = await _db.ExamSubjects
                .Where(e => e.ExamScheduleId == examScheduleId)
                .Include(e => e.Subject)
                .ToListAsync();

            // 按科目名称去重（同名科目视为同一科，只保留第一个）
            var data = rawData
                .GroupBy(e => e.Subject?.Name ?? "")
                .Select(g => g.First())
                .Select(e => new
                {
                    e.SubjectId,
                    SubjectName = e.Subject?.Name ?? "",
                    e.StartTime,
                    e.EndTime
                })
                .ToList();
            return Json(new { success = true, data });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 保存各科考试时间设置
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExamSubjectTimes(int examScheduleId, [FromBody] List<ExamSubjectTimeDto>? times)
    {
        try
        {
            if (times == null || times.Count == 0)
                return Json(new { success = true });

            var examSubjects = await _db.ExamSubjects
                .Where(e => e.ExamScheduleId == examScheduleId)
                .Include(e => e.Subject)
                .ToListAsync();

            foreach (var t in times)
            {
                // 找到前台传来的 SubjectId 对应的科目名称
                var sourceSubject = examSubjects.FirstOrDefault(e => e.SubjectId == t.SubjectId);
                if (sourceSubject?.Subject == null) continue;
                var subjectName = sourceSubject.Subject.Name;

                // 更新该考试中所有同名科目的时间
                var matchingSubjects = examSubjects.Where(e => e.Subject != null && e.Subject.Name == subjectName).ToList();
                if (matchingSubjects.Count == 0) continue;

                DateTime? startTime = null;
                if (!string.IsNullOrWhiteSpace(t.StartTime) && DateTime.TryParse(t.StartTime, out var st))
                    startTime = st;

                DateTime? endTime = null;
                if (!string.IsNullOrWhiteSpace(t.EndTime) && DateTime.TryParse(t.EndTime, out var et))
                    endTime = et;

                foreach (var examSubject in matchingSubjects)
                {
                    examSubject.StartTime = startTime;
                    examSubject.EndTime = endTime;
                }
            }

            await _db.SaveChangesAsync();
            await LogOperation("设置考试时间", examScheduleId, $"考试ID:{examScheduleId} 已设置{times.Count}个科目时间");
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetExamSubjectTeachers(int examScheduleId, int subjectId)
    {
        // 获取考试级别的教师分配
        var examTeachers = await _db.ExamSubjectTeachers
            .Where(est => est.ExamScheduleId == examScheduleId && est.SubjectId == subjectId)
            .ToListAsync();
        var examAssigned = examTeachers
            .GroupBy(et => et.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(et => et.AdminId).ToList());

        // 获取全局教师配置（SubjectTeacher）
        var globalTeachers = await _db.SubjectTeachers
            .Where(st => st.SubjectId == subjectId)
            .ToListAsync();
        var globalAssigned = globalTeachers
            .GroupBy(gt => gt.ClassId)
            .ToDictionary(g => g.Key, g => g.Select(gt => gt.AdminId).ToList());

        // 获取该科目关联的班级
        var subjectClassIds = await _db.SubjectClasses
            .Where(sc => sc.SubjectId == subjectId)
            .Select(sc => sc.ClassId)
            .ToListAsync();

        // 所有班级
        var allClasses = await _db.ClassInfos
            .Include(c => c.GradeLevel)
            .OrderBy(c => c.GradeLevel!.EntryYear)
            .ThenBy(c => c.ClassName)
            .ToListAsync();

        var classList = allClasses
            .Where(c => subjectClassIds.Count == 0 || subjectClassIds.Contains(c.ClassInfoID))
            .Select(c => new
            {
                ClassId = c.ClassInfoID,
                ClassName = c.ClassName,
                GradeName = c.GradeLevel?.CurrentGradeName ?? "",
                ExamTeacherIds = examAssigned.ContainsKey(c.ClassInfoID) ? examAssigned[c.ClassInfoID] : new List<int>(),
                GlobalTeacherIds = globalAssigned.ContainsKey(c.ClassInfoID) ? globalAssigned[c.ClassInfoID] : new List<int>()
            })
            .ToList();

        return Json(new { success = true, classes = classList });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExamSubjectTeachers(int examScheduleId, int subjectId, [FromBody] List<ExamSubjectTeacherDto> teachers)
    {
        try
        {
            var old = await _db.ExamSubjectTeachers
                .Where(est => est.ExamScheduleId == examScheduleId && est.SubjectId == subjectId)
                .ToListAsync();
            _db.ExamSubjectTeachers.RemoveRange(old);

            if (teachers != null && teachers.Count > 0)
            {
                foreach (var t in teachers)
                {
                    _db.ExamSubjectTeachers.Add(new ExamSubjectTeacher
                    {
                        ExamScheduleId = examScheduleId,
                        SubjectId = subjectId,
                        AdminId = t.AdminId,
                        ClassId = t.ClassId
                    });
                }
            }

            await _db.SaveChangesAsync();
            await LogOperation("分配科任教师", examScheduleId, $"考试ID:{examScheduleId} 科目ID:{subjectId} 已分配教师");
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    /// <summary>
    /// 按角色/姓名/账号搜索教职工，附带当前考试的分配状态
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchAdminsForExam(int examScheduleId, string? role, string? name, string? account)
    {
        try
        {
            var query = _db.Admins.AsQueryable();

            // 角色必选
            if (!string.IsNullOrWhiteSpace(role))
                query = query.Where(a => a.Role != null && a.Role.Contains(role.Trim()));

            // 姓名模糊搜索
            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(a => a.RealName != null && a.RealName.Contains(name.Trim()));

            // 账号模糊搜索
            if (!string.IsNullOrWhiteSpace(account))
                query = query.Where(a => a.Username.Contains(account.Trim()));

            var admins = await query
                .OrderBy(a => a.RealName)
                .Select(a => new
                {
                    a.AdminID,
                    a.RealName,
                    Role = a.Role != null ? a.Role.Trim() : "",
                    a.Username,
                    a.ClassName,
                    a.Grade
                })
                .ToListAsync();

            // 获取该考试所有已分配的教师ID
            var assignedIds = await _db.ExamSubjectTeachers
                .Where(est => est.ExamScheduleId == examScheduleId)
                .Select(est => est.AdminId)
                .Distinct()
                .ToListAsync();

            // 该考试的科目列表（用于显示）
            var examSubjects = await _db.ExamSubjects
                .Where(es => es.ExamScheduleId == examScheduleId)
                .Include(es => es.Subject)
                .Select(es => new { es.SubjectId, SubjectName = es.Subject != null ? es.Subject.Name : "" })
                .ToListAsync();

            var result = admins.Select(a => new
            {
                a.AdminID,
                a.RealName,
                a.Role,
                a.Username,
                a.ClassName,
                a.Grade,
                IsAssigned = assignedIds.Contains(a.AdminID)
            }).ToList();

            return Json(new { success = true, admins = result, examSubjects });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "搜索失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 批量保存负责人分配（按考试级别，分配给所有关联科目）
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveExamTeachers(int examScheduleId, [FromBody] List<int>? adminIds)
    {
        try
        {
            // 获取该考试的所有科目+班级组合
            var examSubjectIds = await _db.ExamSubjects
                .Where(es => es.ExamScheduleId == examScheduleId)
                .Select(es => es.SubjectId)
                .ToListAsync();

            var classIds = await _db.SubjectClasses
                .Where(sc => examSubjectIds.Contains(sc.SubjectId))
                .Select(sc => sc.ClassId)
                .Distinct()
                .ToListAsync();

            // 如果找不到科目关联的班级，则使用所有班级
            if (classIds.Count == 0)
            {
                classIds = await _db.ClassInfos.Select(c => c.ClassInfoID).ToListAsync();
            }

            // 清空该考试的旧分配
            var old = await _db.ExamSubjectTeachers
                .Where(est => est.ExamScheduleId == examScheduleId)
                .ToListAsync();
            _db.ExamSubjectTeachers.RemoveRange(old);
            await _db.SaveChangesAsync();

            if (adminIds != null && adminIds.Count > 0)
            {
                foreach (var adminId in adminIds)
                {
                    foreach (var subjectId in examSubjectIds)
                    {
                        foreach (var classId in classIds)
                        {
                            _db.ExamSubjectTeachers.Add(new ExamSubjectTeacher
                            {
                                ExamScheduleId = examScheduleId,
                                SubjectId = subjectId,
                                AdminId = adminId,
                                ClassId = classId
                            });
                        }
                    }
                }
            }

            await _db.SaveChangesAsync();
            await LogOperation("分配负责人", examScheduleId, $"考试ID:{examScheduleId} 已分配{adminIds?.Count ?? 0}位负责人");
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var schedule = await _db.ExamSchedules.FindAsync(id);
        if (schedule == null)
            return Json(new { success = false, message = "记录不存在" });

        var name = schedule.Name;
        _db.ExamSchedules.Remove(schedule);
        await _db.SaveChangesAsync();

        await LogOperation("删除考试", id, name);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> SeedData()
    {
        return await SeedDefaultSubjectsInternal();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedDefaultSubjects()
    {
        return await SeedDefaultSubjectsInternal();
    }

    private async Task<IActionResult> SeedDefaultSubjectsInternal()
    {
        try
        {
            var primarySubjects = new[] { "语文", "数学", "英语", "道德与法治", "科学", "音乐", "体育", "美术", "信息技术", "劳动", "综合实践" };
            var middleSubjects = new[] { "语文", "数学", "英语", "道德与法治", "历史", "地理", "物理", "化学", "生物", "音乐", "体育", "美术", "信息技术", "劳动", "综合实践" };

            // 1. 去重：删除同名的重复科目（保留ID小的）
            var all = await _db.Subjects.OrderBy(s => s.Id).ToListAsync();
            var seen = new HashSet<string>();
            int removed = 0;
            foreach (var subj in all.ToList())
            {
                var key = $"{subj.Name}|{subj.Grade}";
                if (seen.Contains(key))
                {
                    _db.Subjects.Remove(subj);
                    removed++;
                }
                else
                {
                    seen.Add(key);
                }
            }

            // 2. 标准化现有科目：将同名旧科目统一为"小学"/"初中"
            var all2 = await _db.Subjects.ToListAsync();
            int fixedCount = 0;
            foreach (var subj in all2)
            {
                if (primarySubjects.Contains(subj.Name) && subj.Grade != "小学")
                {
                    subj.Grade = "小学";
                    fixedCount++;
                }
                else if (middleSubjects.Contains(subj.Name) && subj.Grade != "初中")
                {
                    subj.Grade = "初中";
                    fixedCount++;
                }
            }

            // 3. 补充缺少的科目
            int added = 0;
            int maxOrder = await _db.Subjects.MaxAsync(s => (int?)s.SortOrder) ?? 0;

            foreach (var name in primarySubjects)
            {
                if (!await _db.Subjects.AnyAsync(s => s.Name == name && s.Grade == "小学"))
                {
                    _db.Subjects.Add(new Subject { Name = name, Grade = "小学", SortOrder = ++maxOrder });
                    added++;
                }
            }
            foreach (var name in middleSubjects)
            {
                if (!await _db.Subjects.AnyAsync(s => s.Name == name && s.Grade == "初中"))
                {
                    _db.Subjects.Add(new Subject { Name = name, Grade = "初中", SortOrder = ++maxOrder });
                    added++;
                }
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true, message = $"已去重 {removed} 个，修复 {fixedCount} 个年级分类，新增 {added} 个科目" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "初始化失败: " + ex.Message });
        }
    }

    // ---- 考试科目管理（年级科目配置） ----

    [HttpGet]
    public async Task<IActionResult> GetGradeSubjectData()
    {
        try
        {
            var gradeLevels = await _db.GradeLevels
                .OrderByDescending(g => g.EntryYear)
                .ThenBy(g => g.SchoolType)
                .Select(g => new { g.GradeLevelID, g.DisplayName, g.CurrentGradeName })
                .ToListAsync();

            var allSubjects = await _db.Subjects
                .OrderBy(s => s.SortOrder)
                .Select(s => new { s.Id, s.Name, s.Grade })
                .ToListAsync();

            return Json(new { success = true, gradeLevels, allSubjects });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载数据失败: " + ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetGradeSubjects(int gradeLevelId)
    {
        try
        {
            var subjectIds = await _db.GradeSubjects
                .Where(gs => gs.GradeLevelId == gradeLevelId)
                .Select(gs => gs.SubjectId)
                .ToListAsync();
            return Json(subjectIds);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载失败: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGradeSubjects(int gradeLevelId, [FromBody] List<int>? subjectIds)
    {
        try
        {
            var old = await _db.GradeSubjects
                .Where(gs => gs.GradeLevelId == gradeLevelId)
                .ToListAsync();
            _db.GradeSubjects.RemoveRange(old);

            if (subjectIds != null && subjectIds.Count > 0)
            {
                foreach (var sid in subjectIds)
                {
                    _db.GradeSubjects.Add(new GradeSubject
                    {
                        GradeLevelId = gradeLevelId,
                        SubjectId = sid
                    });
                }
            }

            await _db.SaveChangesAsync();
            await LogOperation("配置年级科目", gradeLevelId, $"年级ID:{gradeLevelId} 已配置{subjectIds?.Count ?? 0}个科目");
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    private async Task LogOperation(string actionType, int targetId, string targetName)
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
            Detail = $"{actionType}: {targetName}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }
}

// 考试科任教师分配 DTO
public class ExamSubjectTeacherDto
{
    public int AdminId { get; set; }
    public int ClassId { get; set; }
}

// 考试科目 DTO（含满分）
public class ExamSubjectDto
{
    public int SubjectId { get; set; }
    public int? FullScore { get; set; }
}

// 考试科目时间 DTO
public class ExamSubjectTimeDto
{
    public int SubjectId { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
}