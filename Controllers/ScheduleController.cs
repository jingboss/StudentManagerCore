using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;

namespace StudentManagerCore.Controllers;

[Authorize(Roles = "管理员")]
public class ScheduleController : Controller
{
    private readonly AppDbContext _db;

    public ScheduleController(AppDbContext db)
    {
        _db = db;
    }

    // ========== 主页面 ==========
    public async Task<IActionResult> Index()
    {
        ViewBag.GradeLevels = await _db.GradeLevels
            .OrderBy(g => g.EntryYear).ThenBy(g => g.SchoolType).ToListAsync();
        ViewBag.Semesters = await _db.Semesters
            .Include(s => s.AcademicYear).OrderByDescending(s => s.Id).ToListAsync();
        ViewBag.Teachers = await _db.Admins
            .Where(a => a.Role != null && !a.Role.Contains("管理员"))
            .OrderBy(a => a.RealName).ToListAsync();
        ViewBag.ClassInfos = await _db.ClassInfos
            .Include(c => c.GradeLevel)
            .OrderBy(c => c.GradeLevelID).ThenBy(c => c.ClassName).ToListAsync();
        ViewBag.Subjects = await _db.Subjects.OrderBy(s => s.SortOrder).ToListAsync();
        return View();
    }

    // ========== Tab1: 全局班级作息（全年级统一）=========

    /// <summary>获取全局作息配置（取第一个有配置的年级作为模板）</summary>
    [HttpGet]
    public async Task<IActionResult> GetGlobalSchedule()
    {
        var config = await _db.GradeScheduleConfigs
            .OrderBy(c => c.GradeLevelId).FirstOrDefaultAsync();
        if (config == null)
            return Json(new { success = true, hasConfig = false });

        var periods = await _db.GradePeriods
            .Where(p => p.GradeLevelId == config.GradeLevelId)
            .OrderBy(p => p.PeriodNumber)
            .Select(p => new { p.PeriodNumber, p.StartTime, p.EndTime, p.SectionName })
            .ToListAsync();

        return Json(new
        {
            success = true, hasConfig = true,
            config = new { config.Id, config.DaysPerWeek, config.PeriodsPerDay },
            periods
        });
    }

    /// <summary>保存全局作息配置到所有年级</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGlobalSchedule(int daysPerWeek, int periodsPerDay, int durationMinutes = 45)
    {
        try
        {
            if (daysPerWeek < 1 || daysPerWeek > 7)
                return Json(new { success = false, message = "每周天数应在1-7之间" });
            if (periodsPerDay < 1 || periodsPerDay > 12)
                return Json(new { success = false, message = "每天节数应在1-12之间" });

            var allGrades = await _db.GradeLevels.Select(g => g.GradeLevelID).ToListAsync();
            if (allGrades.Count == 0)
                return Json(new { success = false, message = "请先在班级管理中创建年级" });

            foreach (var gid in allGrades)
            {
                var cfg = await _db.GradeScheduleConfigs
                    .FirstOrDefaultAsync(c => c.GradeLevelId == gid);
                if (cfg == null)
                {
                    cfg = new GradeScheduleConfig
                    {
                        GradeLevelId = gid, DaysPerWeek = daysPerWeek,
                        PeriodsPerDay = periodsPerDay, IsActive = true,
                        CreateTime = DateTime.Now
                    };
                    _db.GradeScheduleConfigs.Add(cfg);
                }
                else
                {
                    cfg.DaysPerWeek = daysPerWeek;
                    cfg.PeriodsPerDay = periodsPerDay;
                }

                // 清除旧时段，重新生成默认时段
                var oldPeriods = await _db.GradePeriods
                    .Where(p => p.GradeLevelId == gid).ToListAsync();
                _db.GradePeriods.RemoveRange(oldPeriods);

                var defaults = GenerateDefaultGradePeriods(gid, periodsPerDay, durationMinutes);
                _db.GradePeriods.AddRange(defaults);
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true, message = $"已为 {allGrades.Count} 个年级生成默认节次" });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (msg.Contains("Table") || msg.Contains("table"))
                msg = "数据库表不存在，请先执行 Add_GradeSchedule_Tables.sql 建表脚本";
            return Json(new { success = false, message = msg });
        }
    }

    /// <summary>保存全局时段和节次分组到所有年级</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGlobalSchedulePeriods([FromBody] List<PeriodTimeItem> periods)
    {
        if (periods == null || periods.Count == 0)
            return Json(new { success = false, message = "无时段数据" });

        var allGrades = await _db.GradeLevels.Select(g => g.GradeLevelID).ToListAsync();
        foreach (var gid in allGrades)
        {
            var existing = await _db.GradePeriods
                .Where(p => p.GradeLevelId == gid).ToListAsync();
            foreach (var p in periods)
            {
                var ep = existing.FirstOrDefault(x => x.PeriodNumber == p.periodNumber);
                if (ep != null)
                {
                    ep.StartTime = p.startTime;
                    ep.EndTime = p.endTime;
                    ep.SectionName = p.sectionName;
                }
            }
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "全校作息已保存" });
    }

    /// <summary>插入公共时段（如早读、课间操）</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> InsertPeriod(int beforePeriod, string name, int durationMinutes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
                return Json(new { success = false, message = "请输入时段名称" });
            if (durationMinutes < 1)
                return Json(new { success = false, message = "时长必须大于0" });

            var allGrades = await _db.GradeLevels.Select(g => g.GradeLevelID).ToListAsync();
            if (allGrades.Count == 0)
                return Json(new { success = false, message = "请先在班级管理中创建年级" });

            foreach (var gid in allGrades)
            {
                var config = await _db.GradeScheduleConfigs
                    .FirstOrDefaultAsync(c => c.GradeLevelId == gid);
                if (config == null) continue;

                var periods = await _db.GradePeriods
                    .Where(p => p.GradeLevelId == gid)
                    .OrderBy(p => p.PeriodNumber)
                    .ToListAsync();

                // 计算新时段的起止时间
                TimeSpan newStart, newEnd;
                if (beforePeriod == 1 && periods.Count > 0)
                {
                    // 在第1节前：新时段结束时间 = 第1节开始时间，新时段开始时间往前推duration
                    var firstStart = TimeSpan.Parse(periods[0].StartTime);
                    newEnd = firstStart;
                    newStart = newEnd - TimeSpan.FromMinutes(durationMinutes);
                }
                else
                {
                    // 在N节与N+1节之间
                    var prev = periods.FirstOrDefault(p => p.PeriodNumber == beforePeriod);
                    var next = periods.FirstOrDefault(p => p.PeriodNumber == beforePeriod + 1);
                    if (prev == null || next == null) continue;
                    var prevEnd = TimeSpan.Parse(prev.EndTime);
                    var nextStart = TimeSpan.Parse(next.StartTime);
                    // 新时段在prevEnd和nextStart之间
                    newStart = prevEnd;
                    newEnd = newStart + TimeSpan.FromMinutes(durationMinutes);
                    if (newEnd > nextStart)
                    {
                        // 如果超时，自动压缩下一节开始时间
                        var diff = newEnd - nextStart;
                        foreach (var p in periods.Where(p => p.PeriodNumber >= beforePeriod + 1))
                        {
                            var st = TimeSpan.Parse(p.StartTime);
                            var et = TimeSpan.Parse(p.EndTime);
                            p.StartTime = (st + diff).ToString(@"hh\:mm");
                            p.EndTime = (et + diff).ToString(@"hh\:mm");
                        }
                    }
                }

                // 后移后续节次编号
                foreach (var p in periods.Where(p => p.PeriodNumber >= beforePeriod))
                    p.PeriodNumber++;

                // 插入新时段
                config.PeriodsPerDay++;
                _db.GradePeriods.Add(new GradePeriod
                {
                    GradeLevelId = gid,
                    PeriodNumber = beforePeriod,
                    StartTime = newStart.ToString(@"hh\:mm"),
                    EndTime = newEnd.ToString(@"hh\:mm"),
                    SectionName = name
                });
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true, message = $"已添加「{name}」时段" });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            return Json(new { success = false, message = msg });
        }
    }

    // ========== Tab1: 班级作息接口（按年级）==========

    /// <summary>获取年级作息配置+时段列表</summary>
    [HttpGet]
    public async Task<IActionResult> GetGradeConfig(int gradeLevelId)
    {
        var config = await _db.GradeScheduleConfigs
            .FirstOrDefaultAsync(c => c.GradeLevelId == gradeLevelId);
        if (config == null)
            return Json(new { success = true, hasConfig = false });

        var periods = await _db.GradePeriods
            .Where(p => p.GradeLevelId == gradeLevelId)
            .OrderBy(p => p.PeriodNumber)
            .Select(p => new { p.PeriodNumber, p.StartTime, p.EndTime })
            .ToListAsync();

        return Json(new
        {
            success = true,
            hasConfig = true,
            config = new { config.Id, config.DaysPerWeek, config.PeriodsPerDay },
            periods
        });
    }

    /// <summary>保存年级作息配置+自动生成默认时段</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGradeConfig(int gradeLevelId, int daysPerWeek, int periodsPerDay)
    {
        if (daysPerWeek < 1 || daysPerWeek > 7)
            return Json(new { success = false, message = "每周天数应在1-7之间" });
        if (periodsPerDay < 1 || periodsPerDay > 12)
            return Json(new { success = false, message = "每天节数应在1-12之间" });

        var config = await _db.GradeScheduleConfigs
            .FirstOrDefaultAsync(c => c.GradeLevelId == gradeLevelId);
        if (config == null)
        {
            config = new GradeScheduleConfig
            {
                GradeLevelId = gradeLevelId,
                DaysPerWeek = daysPerWeek,
                PeriodsPerDay = periodsPerDay,
                IsActive = true,
                CreateTime = DateTime.Now
            };
            _db.GradeScheduleConfigs.Add(config);
        }
        else
        {
            config.DaysPerWeek = daysPerWeek;
            config.PeriodsPerDay = periodsPerDay;
        }
        await _db.SaveChangesAsync();

        // 如果还没有时段记录，自动生成默认时段
        var existingPeriods = await _db.GradePeriods
            .Where(p => p.GradeLevelId == gradeLevelId).CountAsync();
        if (existingPeriods == 0)
        {
            var defaults = GenerateDefaultGradePeriods(gradeLevelId, periodsPerDay);
            _db.GradePeriods.AddRange(defaults);
            await _db.SaveChangesAsync();
        }

        return Json(new { success = true, message = "作息配置已保存", configId = config.Id });
    }

    /// <summary>保存年级自定义时段</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGradePeriods(int gradeLevelId, [FromBody] List<PeriodTimeItem> periods)
    {
        if (periods == null || periods.Count == 0)
            return Json(new { success = false, message = "无时段数据" });

        var existing = await _db.GradePeriods
            .Where(p => p.GradeLevelId == gradeLevelId).ToListAsync();

        foreach (var p in periods)
        {
            var ep = existing.FirstOrDefault(x => x.PeriodNumber == p.periodNumber);
            if (ep != null)
            {
                ep.StartTime = p.startTime;
                ep.EndTime = p.endTime;
            }
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "时段已保存" });
    }

    // ========== Tab2: 课时任课接口 ==========

    /// <summary>获取年级科目课时配置+可选教师</summary>
    [HttpGet]
    public async Task<IActionResult> GetGradeSubjectHours(int gradeLevelId)
    {
        var gradeSubjects = await _db.GradeSubjects
            .Where(gs => gs.GradeLevelId == gradeLevelId)
            .Include(gs => gs.Subject)
            .OrderBy(gs => gs.Subject!.SortOrder)
            .ToListAsync();

        var hourConfigs = await _db.GradeSubjectHours
            .Where(h => h.GradeLevelId == gradeLevelId)
            .ToDictionaryAsync(h => h.SubjectId);

        var data = gradeSubjects.Select(gs => new
        {
            subjectId = gs.Subject!.Id,
            subjectName = gs.Subject.Name,
            periodsPerWeek = hourConfigs.ContainsKey(gs.Subject.Id)
                ? hourConfigs[gs.Subject.Id].PeriodsPerWeek : 0
        }).ToList();

        var config = await _db.GradeScheduleConfigs
            .FirstOrDefaultAsync(c => c.GradeLevelId == gradeLevelId);
        var totalSlots = config != null ? config.DaysPerWeek * config.PeriodsPerDay : 0;

        var teachers = await _db.SubjectTeachers
            .Where(st => st.Subject != null
                && _db.ClassInfos.Any(c => c.ClassInfoID == st.ClassId && c.GradeLevelID == gradeLevelId))
            .Include(st => st.Admin).Include(st => st.Subject)
            .Select(st => new
            {
                subjectId = st.Subject!.Id,
                teacherId = st.Admin!.AdminID,
                teacherName = st.Admin.RealName
            })
            .Distinct()
            .ToListAsync();

        return Json(new { success = true, data, totalSlots, teachers });
    }

    /// <summary>保存单条科目周课时配置</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGradeSubjectHour(int gradeLevelId, int subjectId, int periodsPerWeek)
    {
        if (periodsPerWeek < 0 || periodsPerWeek > 30)
            return Json(new { success = false, message = "课时数应在0-30之间" });

        var existing = await _db.GradeSubjectHours
            .FirstOrDefaultAsync(h => h.GradeLevelId == gradeLevelId && h.SubjectId == subjectId);

        if (existing != null)
        {
            if (periodsPerWeek == 0)
                _db.GradeSubjectHours.Remove(existing);
            else
                existing.PeriodsPerWeek = periodsPerWeek;
        }
        else if (periodsPerWeek > 0)
        {
            _db.GradeSubjectHours.Add(new GradeSubjectHour
            {
                GradeLevelId = gradeLevelId,
                SubjectId = subjectId,
                PeriodsPerWeek = periodsPerWeek,
                CreateTime = DateTime.Now
            });
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "已保存" });
    }

    // ========== Tab3: 排课管理（课表网格）==========

    /// <summary>获取班级课表数据（增强：读年级作息时段）</summary>
    [HttpGet]
    public async Task<IActionResult> GetClassSchedule(int classId, int semesterId)
    {
        var schedules = await _db.ClassSchedules
            .Where(s => s.ClassId == classId && s.SemesterId == semesterId)
            .Include(s => s.Subject).Include(s => s.Teacher)
            .ToListAsync();

        var scheduleData = schedules.Select(s => new
        {
            s.DayOfWeek, s.Period, s.SubjectId,
            subjectName = s.Subject?.Name ?? "",
            s.TeacherId, teacherName = s.Teacher?.RealName ?? "", s.Id
        }).ToList();

        var classInfo = await _db.ClassInfos.FindAsync(classId);
        int gradeLevelId = classInfo?.GradeLevelID ?? 0;

        // 年级作息时段
        var gradePeriods = await _db.GradePeriods
            .Where(p => p.GradeLevelId == gradeLevelId)
            .OrderBy(p => p.PeriodNumber)
            .Select(p => new { p.PeriodNumber, p.StartTime, p.EndTime })
            .ToListAsync();

        var gradeConfig = await _db.GradeScheduleConfigs
            .FirstOrDefaultAsync(c => c.GradeLevelId == gradeLevelId);

        // 年级科目列表
        var subjects = await _db.GradeSubjects
            .Where(gs => gs.GradeLevelId == gradeLevelId)
            .Include(gs => gs.Subject)
            .OrderBy(gs => gs.Subject!.SortOrder)
            .Select(gs => new { id = gs.Subject!.Id, name = gs.Subject.Name })
            .ToListAsync();

        // 该班可选的科目教师
        var teachers = await _db.SubjectTeachers
            .Where(st => st.ClassId == classId)
            .Include(st => st.Admin).Include(st => st.Subject)
            .Select(st => new
            {
                teacherId = st.Admin!.AdminID,
                teacherName = st.Admin.RealName,
                subjectId = st.Subject!.Id
            })
            .Distinct().ToListAsync();

        return Json(new
        {
            success = true,
            data = new
            {
                schedules = scheduleData,
                subjects,
                teachers,
                periods = gradePeriods,
                daysPerWeek = gradeConfig?.DaysPerWeek ?? 5,
                periodsPerDay = gradeConfig?.PeriodsPerDay ?? 8
            }
        });
    }

    /// <summary>保存单个课表格子（含冲突检测）</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCell(int classId, int semesterId, int dayOfWeek, int period,
        int subjectId, int teacherId, bool force = false)
    {
        if (!force)
        {
            var conflict = await _db.ClassSchedules
                .Where(s => s.TeacherId == teacherId && s.SemesterId == semesterId
                    && s.DayOfWeek == dayOfWeek && s.Period == period && s.ClassId != classId)
                .Include(s => s.ClassInfo).Include(s => s.Subject)
                .FirstOrDefaultAsync();

            if (conflict != null)
            {
                var className = conflict.ClassInfo?.ClassName ?? "";
                var subjectName = conflict.Subject?.Name ?? "";
                var teacher = await _db.Admins.FindAsync(teacherId);
                return Json(new
                {
                    success = false,
                    message = $"教师「{teacher?.RealName}」在周{DayOfWeekChinese(dayOfWeek)}第{period}节已有课（{className}-{subjectName}），是否覆盖？",
                    conflict = new { teacherName = teacher?.RealName ?? "", className, subjectName, conflict.DayOfWeek, conflict.Period }
                });
            }
        }

        var existing = await _db.ClassSchedules
            .FirstOrDefaultAsync(s => s.ClassId == classId && s.SemesterId == semesterId
                && s.DayOfWeek == dayOfWeek && s.Period == period);

        if (existing != null)
        {
            if (force)
            {
                var conflicts = await _db.ClassSchedules
                    .Where(s => s.TeacherId == teacherId && s.SemesterId == semesterId
                        && s.DayOfWeek == dayOfWeek && s.Period == period && s.ClassId != classId)
                    .ToListAsync();
                _db.ClassSchedules.RemoveRange(conflicts);
            }
            existing.SubjectId = subjectId;
            existing.TeacherId = teacherId;
            existing.UpdateTime = DateTime.Now;
        }
        else
        {
            if (force)
            {
                var conflicts = await _db.ClassSchedules
                    .Where(s => s.TeacherId == teacherId && s.SemesterId == semesterId
                        && s.DayOfWeek == dayOfWeek && s.Period == period && s.ClassId != classId)
                    .ToListAsync();
                _db.ClassSchedules.RemoveRange(conflicts);
            }
            _db.ClassSchedules.Add(new ClassSchedule
            {
                ClassId = classId, SemesterId = semesterId, DayOfWeek = dayOfWeek,
                Period = period, SubjectId = subjectId, TeacherId = teacherId,
                CreateTime = DateTime.Now
            });
        }
        await _db.SaveChangesAsync();
        return Json(new { success = true, message = "已保存" });
    }

    /// <summary>清空单个格子</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearCell(int classId, int semesterId, int dayOfWeek, int period)
    {
        var existing = await _db.ClassSchedules
            .FirstOrDefaultAsync(s => s.ClassId == classId && s.SemesterId == semesterId
                && s.DayOfWeek == dayOfWeek && s.Period == period);
        if (existing != null)
        {
            _db.ClassSchedules.Remove(existing);
            await _db.SaveChangesAsync();
        }
        return Json(new { success = true, message = "已清空" });
    }

    /// <summary>获取教师课表</summary>
    [HttpGet]
    public async Task<IActionResult> GetTeacherSchedule(int teacherId, int semesterId)
    {
        var teacher = await _db.Admins.FindAsync(teacherId);
        if (teacher == null)
            return Json(new { success = false, message = "教师不存在" });

        var schedules = await _db.ClassSchedules
            .Where(s => s.TeacherId == teacherId && s.SemesterId == semesterId)
            .Include(s => s.Subject).Include(s => s.ClassInfo)
            .ToListAsync();

        var data = schedules.Select(s => new
        {
            s.DayOfWeek, s.Period, subjectName = s.Subject?.Name ?? "",
            className = s.ClassInfo?.ClassName ?? "", classId = s.ClassId
        }).ToList();

        return Json(new { success = true, data = new { teacherName = teacher.RealName, schedules = data } });
    }

    /// <summary>获取年级课表</summary>
    [HttpGet]
    public async Task<IActionResult> GetGradeSchedule(int gradeLevelId, int semesterId)
    {
        var grade = await _db.GradeLevels.FindAsync(gradeLevelId);
        if (grade == null)
            return Json(new { success = false, message = "年级不存在" });

        var classes = await _db.ClassInfos
            .Where(c => c.GradeLevelID == gradeLevelId).OrderBy(c => c.ClassName).ToListAsync();

        var classIds = classes.Select(c => c.ClassInfoID).ToList();
        var schedules = await _db.ClassSchedules
            .Where(s => classIds.Contains(s.ClassId) && s.SemesterId == semesterId)
            .Include(s => s.Subject).Include(s => s.Teacher).Include(s => s.ClassInfo)
            .ToListAsync();

        var classData = classes.Select(c => new
        {
            classId = c.ClassInfoID, className = c.ClassName,
            schedules = schedules.Where(s => s.ClassId == c.ClassInfoID).Select(s => new
            {
                s.DayOfWeek, s.Period, subjectName = s.Subject?.Name ?? "",
                teacherName = s.Teacher?.RealName ?? ""
            })
        }).ToList();

        return Json(new { success = true, data = new { gradeName = grade.DisplayName, classes = classData } });
    }

    // ========== Tab4: 自动排课 ==========

    /// <summary>自动排课（贪心均匀分布算法）</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AutoSchedule(int gradeLevelId, int semesterId)
    {
        // 1. 验证前置条件
        var config = await _db.GradeScheduleConfigs
            .FirstOrDefaultAsync(c => c.GradeLevelId == gradeLevelId);
        if (config == null)
            return Json(new { success = false, message = "请先在「班级作息」中配置该年级的作息" });

        var hourConfigs = await _db.GradeSubjectHours
            .Where(h => h.GradeLevelId == gradeLevelId && h.PeriodsPerWeek > 0)
            .Include(h => h.Subject)
            .ToListAsync();
        if (hourConfigs.Count == 0)
            return Json(new { success = false, message = "请先在「课时任课」中配置科目周课时数" });

        int totalSlots = config.DaysPerWeek * config.PeriodsPerDay;
        int totalHours = hourConfigs.Sum(h => h.PeriodsPerWeek);
        if (totalHours > totalSlots)
            return Json(new { success = false, message = $"总课时({totalHours})超过可用时段({totalSlots})，请减少课时" });

        // 2. 获取该年级的所有班级
        var classes = await _db.ClassInfos
            .Where(c => c.GradeLevelID == gradeLevelId).OrderBy(c => c.ClassName).ToListAsync();
        if (classes.Count == 0)
            return Json(new { success = false, message = "该年级下没有班级" });

        // 3. 运行贪心排课算法
        var result = GreedySchedule(config, hourConfigs, classes, semesterId);

        // 4. 清除旧课表 + 写入新课表
        var classIds = classes.Select(c => c.ClassInfoID).ToList();
        var oldSchedules = await _db.ClassSchedules
            .Where(s => classIds.Contains(s.ClassId) && s.SemesterId == semesterId)
            .ToListAsync();
        _db.ClassSchedules.RemoveRange(oldSchedules);
        await _db.SaveChangesAsync();

        _db.ClassSchedules.AddRange(result.Schedules);
        await _db.SaveChangesAsync();

        return Json(new
        {
            success = true,
            message = $"自动排课完成，共为 {classes.Count} 个班级安排了 {totalHours} 节/周",
            stats = result.Stats
        });
    }

    // ========== 贪心排课算法 ==========
    private static (List<ClassSchedule> Schedules, object Stats) GreedySchedule(
        GradeScheduleConfig config,
        List<GradeSubjectHour> hourConfigs,
        List<ClassInfo> classes,
        int semesterId)
    {
        var result = new List<ClassSchedule>();
        var now = DateTime.Now;

        // 生成所有时段 [(day, period), ...]
        var slots = new List<(int Day, int Period)>();
        for (int d = 1; d <= config.DaysPerWeek; d++)
            for (int p = 1; p <= config.PeriodsPerDay; p++)
                slots.Add((d, p));

        // 按周课时从高到低排序科目
        var orderedSubjects = hourConfigs
            .OrderByDescending(h => h.PeriodsPerWeek)
            .ThenBy(h => h.Subject?.SortOrder ?? 0)
            .ToList();

        foreach (var cls in classes)
        {
            var usedSlots = new HashSet<(int, int)>();

            foreach (var hs in orderedSubjects)
            {
                int count = hs.PeriodsPerWeek;
                var available = slots.Where(s => !usedSlots.Contains(s)).ToList();
                if (available.Count == 0) break;

                // 按天分组，每组内按节次排序
                var dayGroups = available
                    .GroupBy(s => s.Day)
                    .OrderBy(g => g.Count())
                    .ToList();

                int perDay = Math.Max(1, count / config.DaysPerWeek);
                int remaining = count;

                foreach (var dayGroup in dayGroups)
                {
                    if (remaining <= 0) break;
                    int take = Math.Min(remaining, Math.Min(perDay, dayGroup.Count()));
                    var candidates = dayGroup
                        .OrderBy(s => s.Period)
                        .Take(take);
                    foreach (var slot in candidates)
                    {
                        usedSlots.Add(slot);
                        result.Add(new ClassSchedule
                        {
                            ClassId = cls.ClassInfoID,
                            SemesterId = semesterId,
                            DayOfWeek = slot.Day,
                            Period = slot.Period,
                            SubjectId = hs.SubjectId,
                            TeacherId = 0,
                            CreateTime = now
                        });
                        remaining--;
                    }
                }

                // 剩余课时补到有空位的时段
                if (remaining > 0)
                {
                    var remainingSlots = slots.Where(s => !usedSlots.Contains(s))
                        .OrderBy(s => s.Day).ThenBy(s => s.Period);
                    foreach (var slot in remainingSlots)
                    {
                        if (remaining <= 0) break;
                        usedSlots.Add(slot);
                        result.Add(new ClassSchedule
                        {
                            ClassId = cls.ClassInfoID,
                            SemesterId = semesterId,
                            DayOfWeek = slot.Day,
                            Period = slot.Period,
                            SubjectId = hs.SubjectId,
                            TeacherId = 0,
                            CreateTime = now
                        });
                        remaining--;
                    }
                }
            }
        }

        var stats = new
        {
            classCount = classes.Count,
            slotsPerClass = hourConfigs.Sum(h => h.PeriodsPerWeek),
            subjectCount = hourConfigs.Count
        };

        return (result, stats);
    }

    // ========== 辅助方法 ==========

    private static List<GradePeriod> GenerateDefaultGradePeriods(int gradeLevelId, int totalPeriods, int durationMinutes = 45)
    {
        var periods = new List<GradePeriod>();
        var start = TimeSpan.FromHours(8);
        int duration = durationMinutes, breakMin = 10;
        var sectionNames = new[] { "", "上午", "上午", "上午", "上午", "下午", "下午", "下午", "晚修", "晚修", "晚修" };
        for (int i = 0; i < totalPeriods && i < sectionNames.Length; i++)
        {
            var ps = start.Add(TimeSpan.FromMinutes(i * (duration + breakMin)));
            var pe = ps.Add(TimeSpan.FromMinutes(duration));
            periods.Add(new GradePeriod
            {
                GradeLevelId = gradeLevelId,
                PeriodNumber = i + 1,
                StartTime = ps.ToString(@"hh\:mm"),
                EndTime = pe.ToString(@"hh\:mm"),
                SectionName = sectionNames[i]
            });
        }
        return periods;
    }

    private static string DayOfWeekChinese(int day)
    {
        return day switch
        {
            1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五",
            6 => "六", 7 => "日", _ => day.ToString()
        };
    }
}

public class PeriodTimeItem
{
    public int periodNumber { get; set; }
    public string startTime { get; set; } = "";
    public string endTime { get; set; } = "";
    public string? sectionName { get; set; }
}
