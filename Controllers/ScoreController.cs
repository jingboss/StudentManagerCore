using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using ClosedXML.Excel;
using System.Security.Claims;
using System.Text;

namespace StudentManagerCore.Controllers;

[Authorize]
public class ScoreController : Controller
{
    private readonly AppDbContext _db;
    private readonly AiAnalysisService _aiService;

    public ScoreController(AppDbContext db, AiAnalysisService aiService)
    {
        _db = db;
        _aiService = aiService;
    }

    private bool IsAdmin() =>
        User.FindFirst(ClaimTypes.Role)?.Value?.Trim() == "管理员";

    private int? GetAdminId()
    {
        var idStr = User.FindFirst("AdminID")?.Value ?? "";
        if (int.TryParse(idStr, out var id)) return id;
        return null;
    }

    // ========== 成绩录入（一键表格式） ==========
    [HttpGet]
    public async Task<IActionResult> Entry()
    {
        var exams = await _db.ExamSchedules
            .OrderByDescending(e => e.ExamDate)
            .Select(e => new { e.Id, e.Name, e.ExamType, e.ExamDate, e.EndDate, e.Status, e.Grades })
            .ToListAsync();
        ViewBag.ExamSchedules = exams;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetEntryData(int examScheduleId, string? grade = null, string? className = null)
    {
        try
        {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null)
            return Json(new { success = false, message = "考试安排不存在" });

       // 获取该考试的科目（按名称去重）
        var subjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .ThenBy(es => es.Subject!.Name)
            .Select(es => new { es.SubjectId, SubjectName = es.Subject!.Name ?? "", FullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();

        subjects = subjects.GroupBy(s => s.SubjectName).Select(g => g.First()).ToList();

        if (subjects.Count == 0)
            return Json(new { success = false, message = "该考试尚未关联科目，请先设置科目" });

        // 获取该考试覆盖年级的所有学生
        var gradeList = (exam.Grades ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        IQueryable<Student> studentQuery = _db.Students.Where(s => s.Status == "在读");
        if (gradeList.Count > 0)
        {
            studentQuery = studentQuery.Where(s => gradeList.Contains(s.Grade ?? ""));
        }
        // 年级筛选
        if (!string.IsNullOrEmpty(grade))
        {
            studentQuery = studentQuery.Where(s => s.Grade == grade);
        }
        // 班级筛选
        if (!string.IsNullOrEmpty(className))
        {
            studentQuery = studentQuery.Where(s => s.ClassName == className);
        }
        var students = await studentQuery
            .OrderBy(s => s.Grade).ThenBy(s => s.ClassName).ThenBy(s => s.Name)
            .Select(s => new { s.StudentID, s.StudentNo, s.Name, s.Grade, s.ClassName })
            .ToListAsync();

        // 获取可用年级列表（来自考试配置）
        var availableGrades = gradeList;
        // 获取可用班级列表（按当前年级过滤)
        var classQuery = _db.Students.Where(s => s.Status == "在读");
        if (!string.IsNullOrEmpty(grade))
            classQuery = classQuery.Where(s => s.Grade == grade);
        var availableClasses = await classQuery
            .Where(s => s.ClassName != null && s.ClassName != "")
            .Select(s => s.ClassName!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        // 获取已有成绩
        var existingScores = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId)
            .Select(sc => new { sc.StudentId, sc.SubjectId, sc.ScoreValue, sc.IsAbsent })
            .ToListAsync();

        return Json(new
        {
            success = true,
            subjects,
            students,
            existingScores,
            subjectIds = subjects.Select(s => s.SubjectId).ToList(),
            availableGrades,
            availableClasses,
            selectedGrade = grade ?? "",
            selectedClass = className ?? ""
        });
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(AppDomain.CurrentDomain.BaseDirectory + "getentry_error.txt",
                $"Time: {DateTime.Now}\nError: {ex.Message}\nStack: {ex.StackTrace}\n\n");
            return Json(new { success = false, message = "加载数据异常: " + ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveScores(int examScheduleId, [FromBody] List<ScoreItem> scores)
    {
        if (scores == null || scores.Count == 0)
            return Json(new { success = false, message = "未提交任何成绩" });

        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null)
            return Json(new { success = false, message = "考试安排不存在" });

        if (exam.Status != "进行中")
            return Json(new { success = false, message = "仅「进行中」的考试可以录入成绩" });

        var studentIds = scores.Select(s => s.StudentId).Distinct().ToList();
        var subjectIds = scores.Select(s => s.SubjectId).Distinct().ToList();

        // 加载该考试的科目满分
        var fullScoreData = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId && subjectIds.Contains(es.SubjectId))
            .Select(es => new { es.SubjectId, FullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();
        var fullScoreMap = fullScoreData.ToDictionary(fs => fs.SubjectId, fs => fs.FullScore);

        // 校验分数是否超出满分
        var errors = new List<string>();
        foreach (var item in scores)
        {
            if (fullScoreMap.TryGetValue(item.SubjectId, out var maxScore))
            {
                if (item.ScoreValue < 0 || item.ScoreValue > maxScore)
                {
                    errors.Add($"科目ID:{item.SubjectId} 分数 {item.ScoreValue} 超出满分 {maxScore}");
                }
            }
        }
        if (errors.Count > 0)
            return Json(new { success = false, message = "存在超出满分的成绩:\n" + string.Join("\n", errors) });

        // 批量加载学生班级信息
        var students = await _db.Students.Where(st => studentIds.Contains(st.StudentID)).ToListAsync();
        var studentDict = students.ToDictionary(st => st.StudentID);

        // 批量加载已存在的成绩
        var existingList = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId
                      && studentIds.Contains(sc.StudentId)
                      && subjectIds.Contains(sc.SubjectId))
            .ToListAsync();
        var existingDict = existingList.ToDictionary(sc => $"{sc.StudentId}_{sc.SubjectId}");

        int saved = 0;
        foreach (var item in scores)
        {
            var key = $"{item.StudentId}_{item.SubjectId}";
            if (existingDict.TryGetValue(key, out var existing))
            {
                // 已有成绩则更新
                existing.ScoreValue = item.ScoreValue;
                existing.IsAbsent = item.IsAbsent;
            }
            else
            {
                // 新增成绩
                studentDict.TryGetValue(item.StudentId, out var student);
                var score = new Score
                {
                    StudentId = item.StudentId,
                    SubjectId = item.SubjectId,
                    ScoreValue = item.ScoreValue,
                    IsAbsent = item.IsAbsent,
                    ExamScheduleId = examScheduleId,
                    ExamType = exam.ExamType,
                    ExamDate = exam.ExamDate,
                    CreateTime = DateTime.Now,
                };
                // 填充分级班级快照（必须同时按年级+班级精确匹配，避免不同年级同名班级串数据）
                if (student != null)
                {
                    ClassInfo? classInfo = null;

                    // 优先使用 ClassID（如果学生已有 ClassID）
                    if (student.ClassID.HasValue && student.ClassID.Value > 0)
                    {
                        classInfo = await _db.ClassInfos
                            .FirstOrDefaultAsync(c => c.ClassInfoID == student.ClassID.Value);
                    }

                    // 回退：按班级名+年级名精确匹配
                    if (classInfo == null && !string.IsNullOrEmpty(student.ClassName))
                    {
                        classInfo = await _db.ClassInfos
                            .Include(c => c.GradeLevel)
                            .FirstOrDefaultAsync(c => c.ClassName == student.ClassName
                                && c.GradeLevel != null
                                && (c.GradeLevel.CurrentGradeName == student.Grade
                                    || c.GradeLevel.DisplayName == student.Grade));
                    }

                    if (classInfo != null)
                    {
                        score.ClassInfoId = classInfo.ClassInfoID;
                        score.GradeLevelId = classInfo.GradeLevelID;
                    }
                }
                _db.Scores.Add(score);
            }
            saved++;
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = $"成功保存 {saved} 条成绩" });
    }

    // ========== 成绩查看 ==========
    [HttpGet]
    public async Task<IActionResult> ScoreView()
    {
        var exams = await _db.ExamSchedules
            .OrderByDescending(e => e.ExamDate)
            .Select(e => new { e.Id, e.Name, e.ExamType, e.ExamDate, e.Status, e.Grades })
            .ToListAsync();
        ViewBag.ExamSchedules = exams;

        // 获取当前登录老师的信息，供前端默认筛选
        var adminId = GetAdminId();
        ViewBag.TeacherGrade = "";
        ViewBag.TeacherClassName = "";
        ViewBag.TeacherGradeLevelId = "";
        ViewBag.TeacherClassInfoId = "";
        if (adminId.HasValue)
        {
            var admin = await _db.Admins.FindAsync(adminId.Value);
            if (admin != null && !IsAdmin())
            {
                ViewBag.TeacherGrade = admin.Grade ?? "";
                ViewBag.TeacherClassName = admin.ClassName ?? "";
                if (admin.PrimaryRole == "班主任" && admin.ClassID.HasValue)
                    ViewBag.TeacherClassInfoId = admin.ClassID.Value.ToString();
                // 根据年级名查找 GradeLevelId
                var grades = await _db.GradeLevels.ToListAsync();
                var matchedGl = grades.FirstOrDefault(g => g.CurrentGradeName == admin.Grade);
                if (matchedGl != null)
                    ViewBag.TeacherGradeLevelId = matchedGl.GradeLevelID.ToString();

                // 班主任只能看到与自己年级匹配的考试（ExamSchedule.Grades 存的是 CurrentGradeName，如"一年级"）
                if (!string.IsNullOrEmpty(admin.Grade))
                {
                    exams = exams.Where(e => e.Grades != null && e.Grades.Contains(admin.Grade)).ToList();
                }
                else
                {
                    exams = exams.Where(e => false).ToList(); // 没有年级，看不到任何考试
                }
                ViewBag.ExamSchedules = exams;
            }
        }

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetViewData(int examScheduleId, int? gradeLevelId, int? classInfoId)
    {
        try
        {
        // 自动修复已有错误数据：Score 的 GradeLevelId/ClassInfoId 与 Student 不一致的
        await AutoFixScoreClassReferences(examScheduleId);

        // 非管理员用户，自动默认筛选到其管理的班级
        var adminId = GetAdminId();
        if (!IsAdmin() && adminId.HasValue)
        {
            var admin = await _db.Admins.FindAsync(adminId.Value);
            if (admin != null)
            {
                var primaryRole = admin.PrimaryRole;
                if (!gradeLevelId.HasValue && !string.IsNullOrEmpty(admin.Grade) &&
                    (primaryRole == "班主任" || primaryRole == "年级级长"))
                {
                    var allGl = await _db.GradeLevels.ToListAsync();
                    var matched = allGl.FirstOrDefault(g => g.CurrentGradeName == admin.Grade);
                    if (matched != null) gradeLevelId = matched.GradeLevelID;
                }
                if (!classInfoId.HasValue && admin.ClassID.HasValue && primaryRole == "班主任")
                {
                    classInfoId = admin.ClassID.Value;
                }
            }
        }
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null)
            return Json(new { success = false, message = "考试安排不存在" });

        // 解析考试覆盖的年级ID列表（过滤不属于考试年级的成绩）
        var gradeList = (exam.Grades ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        List<int> validGradeLevelIds = new();
        if (gradeList.Count > 0)
        {
            var allGradeLevels = await _db.GradeLevels.ToListAsync();
            validGradeLevelIds = allGradeLevels
                .Where(gl => gradeList.Contains(gl.CurrentGradeName))
                .Select(gl => gl.GradeLevelID)
                .ToList();
        }

        // 判断学段：小学还是初中
        var allMatchedGradeLevels = await _db.GradeLevels.ToListAsync();
        var matchedGrades = allMatchedGradeLevels
            .Where(gl => validGradeLevelIds.Contains(gl.GradeLevelID))
            .ToList();
        var isElementary = matchedGrades.Count > 0 && matchedGrades.All(gl => gl.SchoolType == "小学");

        // 如果按 CurrentGradeName 匹配不上，尝试按显示名称匹配
        if (validGradeLevelIds.Count == 0 && gradeList.Count > 0)
        {
            var allGradeLevels2 = await _db.GradeLevels.ToListAsync();
            validGradeLevelIds = MatchGradeLevelIdsByDisplayName(gradeList, allGradeLevels2);
            // 重新判断学段
            matchedGrades = allGradeLevels2
                .Where(gl => validGradeLevelIds.Contains(gl.GradeLevelID))
                .ToList();
            isElementary = matchedGrades.Count > 0 && matchedGrades.All(gl => gl.SchoolType == "小学");
        }

        // 获取该考试的科目（按科目名去重）
        var subjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder).ThenBy(es => es.Subject!.Name)
            .Select(es => new { es.SubjectId, SubjectName = es.Subject!.Name ?? "", FullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();
        subjects = subjects.GroupBy(s => s.SubjectName).Select(g => g.First()).ToList();

        // ========== ★ 第一步：获取全部成绩数据用于排名（不受筛选影响） ==========
        var allScoresQuery = _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId)
            .Include(sc => sc.Student)
            .AsQueryable();

        // 但全部数据仍然要限制在考试覆盖的年级范围内
        if (validGradeLevelIds.Count > 0)
        {
            var allGradeLevels = await _db.GradeLevels
                .Where(g => validGradeLevelIds.Contains(g.GradeLevelID))
                .ToListAsync();
            var gradeNames = allGradeLevels.Select(g => g.CurrentGradeName).Where(n => !string.IsNullOrEmpty(n)).ToList();
            allScoresQuery = allScoresQuery.Where(sc =>
                (sc.GradeLevelId != null && validGradeLevelIds.Contains(sc.GradeLevelId.Value))
                || (sc.Student != null && sc.Student.Grade != null && gradeNames.Contains(sc.Student.Grade)));
        }

        var allScoresForRanking = await allScoresQuery.ToListAsync();

        // 构建全量学生总分数据（用于排名计算）
        var allStudentScores = allScoresForRanking
            .GroupBy(sc => new { sc.StudentId, StudentNo = sc.Student?.StudentNo ?? "", StudentName = sc.Student?.Name ?? "" })
            .Select(g => new
            {
                StudentId = g.Key.StudentId,
                TotalScore = g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue),
                ClassInfoId = g.Min(sc => sc.ClassInfoId),
                GradeLevelId = g.Min(sc => sc.GradeLevelId)
            })
            .ToList();

        // ★ 竞争排名（1224制）：同分同名次
        static Dictionary<int, int> ComputeCompetitionRanking(IEnumerable<dynamic> items, Func<dynamic, decimal> orderSelector)
        {
            var sorted = items.OrderByDescending(orderSelector).ToList();
            var result = new Dictionary<int, int>();
            int currentRank = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && orderSelector(sorted[i]) != orderSelector(sorted[i - 1]))
                    currentRank++;
                result[(int)sorted[i].StudentId] = currentRank;
            }
            return result;
        }

        // 全量年级排名（按GradeLevelId分组）
        var gradeRankings = new Dictionary<int, Dictionary<int, int>>();
        foreach (var gradeGroup in allStudentScores
            .Where(s => s.GradeLevelId != null)
            .GroupBy(s => s.GradeLevelId!.Value))
        {
            gradeRankings[gradeGroup.Key] = ComputeCompetitionRanking(gradeGroup, x => (decimal)x.TotalScore);
        }

        // 全量班级排名（按ClassInfoId分组）
        var classRankings = new Dictionary<int, Dictionary<int, int>>();
        foreach (var classGroup in allStudentScores
            .Where(s => s.ClassInfoId != null)
            .GroupBy(s => s.ClassInfoId!.Value))
        {
            classRankings[classGroup.Key] = ComputeCompetitionRanking(classGroup, x => (decimal)x.TotalScore);
        }

        // ========== ★ 第二步：按筛选条件获取显示的考试成绩 ==========
        var query = _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId)
            .Include(sc => sc.Student)
            .AsQueryable();

        // 构建筛选条件（兼容旧数据中可能因同名班级导致的错误 GradeLevelId/ClassInfoId）
        if (gradeLevelId.HasValue || classInfoId.HasValue)
        {
            string? gradeName = null;
            string? className = null;

            if (gradeLevelId.HasValue)
            {
                var gradeLevel = await _db.GradeLevels.FindAsync(gradeLevelId.Value);
                gradeName = gradeLevel?.CurrentGradeName;
            }
            if (classInfoId.HasValue)
            {
                var classInfo = await _db.ClassInfos.FindAsync(classInfoId.Value);
                className = classInfo?.ClassName;
            }

            // 主条件：Score 的 GradeLevelId/ClassInfoId 匹配
            // 兜底条件：Student 的 Grade/ClassName 匹配（修复旧数据）
            if (gradeLevelId.HasValue && classInfoId.HasValue)
            {
                var gl = gradeLevelId.Value;
                var ci = classInfoId.Value;
                if (!string.IsNullOrEmpty(gradeName) && !string.IsNullOrEmpty(className))
                {
                    query = query.Where(sc =>
                        (sc.GradeLevelId == gl && sc.ClassInfoId == ci)
                        || (sc.Student != null && sc.Student.Grade == gradeName && sc.Student.ClassName == className));
                }
                else
                {
                    query = query.Where(sc => sc.GradeLevelId == gl && sc.ClassInfoId == ci);
                }
            }
            else if (gradeLevelId.HasValue)
            {
                var gl = gradeLevelId.Value;
                if (!string.IsNullOrEmpty(gradeName))
                {
                    query = query.Where(sc =>
                        sc.GradeLevelId == gl
                        || (sc.Student != null && sc.Student.Grade == gradeName));
                }
                else
                {
                    query = query.Where(sc => sc.GradeLevelId == gl);
                }
            }
            else if (classInfoId.HasValue)
            {
                var ci = classInfoId.Value;
                if (!string.IsNullOrEmpty(className) && !string.IsNullOrEmpty(gradeName))
                {
                    query = query.Where(sc =>
                        sc.ClassInfoId == ci
                        || (sc.Student != null && sc.Student.ClassName == className && sc.Student.Grade == gradeName));
                }
                else
                {
                    query = query.Where(sc => sc.ClassInfoId == ci);
                }
            }
        }
        else if (validGradeLevelIds.Count > 0)
        {
            // 无手动筛选时，限制为考试覆盖的年级（同时用学生信息兜底）
            var gradeLevels = await _db.GradeLevels
                .Where(g => validGradeLevelIds.Contains(g.GradeLevelID))
                .ToListAsync();
            var gradeNames = gradeLevels.Select(g => g.CurrentGradeName).Where(n => !string.IsNullOrEmpty(n)).ToList();
            query = query.Where(sc =>
                (sc.GradeLevelId != null && validGradeLevelIds.Contains(sc.GradeLevelId.Value))
                || (sc.Student != null && sc.Student.Grade != null && gradeNames.Contains(sc.Student.Grade)));
        }

        var scores = await query.ToListAsync();

        // ========== ★ 第三步：构建显示的得分详情（基于筛选后的数据） ==========
        var subjectStats = subjects.Select(sub =>
        {
            var subScores = scores.Where(sc => sc.SubjectId == sub.SubjectId).ToList();
            var presentScores = subScores.Where(sc => !sc.IsAbsent).ToList();
            var absentCount = subScores.Count(sc => sc.IsAbsent);
            var count = presentScores.Count;
            var avg = count > 0 ? Math.Round(presentScores.Average(sc => (double)sc.ScoreValue), 1) : 0;
            var max = count > 0 ? presentScores.Max(sc => (double)sc.ScoreValue) : 0;
            var min = count > 0 ? presentScores.Min(sc => (double)sc.ScoreValue) : 0;
            var fs = (decimal)sub.FullScore;

            // 按排名比例分档（只对实考学生排名）
            var sortedScores = presentScores
                .OrderByDescending(sc => sc.ScoreValue)
                .ToList();
            int totalCount = sortedScores.Count;
            int excellent = 0, good = 0, pass = 0, low = 0;
            for (int si = 0; si < totalCount; si++)
            {
                double pct = (double)(si + 1) / totalCount;
                if (pct <= 0.25) excellent++;
                if (pct <= 0.95) pass++;
                if (pct > 0.25 && pct <= 0.60) good++;
                if (pct > 0.95) low++;
            }

            // 分数段（只对实考学生统计）
            var s0_59 = presentScores.Count(sc => sc.ScoreValue < fs * 0.6m);
            var s60_69 = presentScores.Count(sc => sc.ScoreValue >= fs * 0.6m && sc.ScoreValue < fs * 0.7m);
            var s70_79 = presentScores.Count(sc => sc.ScoreValue >= fs * 0.7m && sc.ScoreValue < fs * 0.8m);
            var s80_89 = presentScores.Count(sc => sc.ScoreValue >= fs * 0.8m && sc.ScoreValue < fs * 0.9m);
            var s90_100 = presentScores.Count(sc => sc.ScoreValue >= fs * 0.9m);

            // 中位数（只对实考学生）
            var sorted = presentScores.Select(sc => (double)sc.ScoreValue).OrderBy(v => v).ToList();
            var median = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0;

            // 临界生（仅初中适用，小学设为-1表示不适用）
            var criticalCount = isElementary ? -1 : presentScores.Count(sc => sc.ScoreValue >= fs * 0.55m && sc.ScoreValue <= fs * 0.65m);

            return new
            {
                sub.SubjectId,
                sub.SubjectName,
                sub.FullScore,
                AvgScore = avg,
                MaxScore = max,
                MinScore = min,
                MedianScore = median,
                StudentCount = count,
                AbsentCount = absentCount,
                TotalStudentCount = count + absentCount,
                ExcellentCount = excellent,
                ExcellentRate = count > 0 ? Math.Round((double)excellent / count * 100, 1) : 0,
                GoodCount = good,
                GoodRate = count > 0 ? Math.Round((double)good / count * 100, 1) : 0,
                PassCount = pass,
                PassRate = count > 0 ? Math.Round((double)pass / count * 100, 1) : 0,
                LowCount = low,
                LowRate = count > 0 ? Math.Round((double)low / count * 100, 1) : 0,
                CriticalCount = criticalCount,
                Seg0_59 = s0_59, Seg60_69 = s60_69, Seg70_79 = s70_79, Seg80_89 = s80_89, Seg90_100 = s90_100
            };
        }).ToList();

        // 总分统计（排除缺考科目的0分）—— 用于显示
        var studentScores = scores
            .GroupBy(sc => new { sc.StudentId, StudentNo = sc.Student?.StudentNo ?? "", StudentName = sc.Student?.Name ?? "" })
            .Select(g => new
            {
                StudentId = g.Key.StudentId,
                StudentNo = g.Key.StudentNo,
                StudentName = g.Key.StudentName,
                Scores = subjects.Select(sub =>
                {
                    var sc = g.FirstOrDefault(s => s.SubjectId == sub.SubjectId);
                    if (sc == null || sc.IsAbsent)
                        return new { SubjectId = sub.SubjectId, SubjectName = sub.SubjectName, ScoreValue = (decimal?)null, IsAbsent = sc?.IsAbsent ?? false };
                    return new { SubjectId = sub.SubjectId, SubjectName = sub.SubjectName, ScoreValue = (decimal?)sc.ScoreValue, IsAbsent = false };
                }).ToList(),
                TotalScore = g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue),
                // 实考科目的满分之和（缺考科目不计入）
                PresentFullScore = g.Where(sc => !sc.IsAbsent)
                    .Join(subjects, sc => sc.SubjectId, sub => sub.SubjectId, (sc, sub) => sub.FullScore)
                    .Sum(),
                PresentSubjectCount = g.Count(sc => !sc.IsAbsent),
                SubjectCount = subjects.Count,
                ClassInfoId = g.Min(sc => sc.ClassInfoId),
                GradeLevelId = g.Min(sc => sc.GradeLevelId)
            })
            .OrderByDescending(x => x.TotalScore)
            .ToList();

        // 学生等级（总分百分比法：与成绩报告单综合等级一致）
        // A≥90%  B≥80%  C≥60%  D<60%，单科不及格只拉低均分不一票否决
        var classLevels = new Dictionary<int, string>(); // StudentId → Level
        foreach (var stu in studentScores)
        {
            string level;
            if (stu.PresentFullScore <= 0)
                level = "缺考";
            else
            {
                var pct = (double)stu.TotalScore / (double)stu.PresentFullScore;
                if (pct >= 0.9) level = "A";
                else if (pct >= 0.8) level = "B";
                else if (pct >= 0.6) level = "C";
                else level = "D";
            }
            classLevels[stu.StudentId] = level;
        }

        // 构建最终排名结果（使用竞争排名1224制 + 引用全量数据的年级/班级排名）
        var rankedScores = studentScores
            .Select(x =>
            {
                var totalScore = x.TotalScore;
                var avgScore = x.PresentSubjectCount > 0
                    ? Math.Round((double)totalScore / x.PresentSubjectCount, 1)
                    : 0;

                int? classRank = null;
                if (x.ClassInfoId.HasValue && classRankings.ContainsKey(x.ClassInfoId.Value) &&
                    classRankings[x.ClassInfoId.Value].ContainsKey(x.StudentId))
                {
                    classRank = classRankings[x.ClassInfoId.Value][x.StudentId];
                }

                int? gradeRank = null;
                if (x.GradeLevelId.HasValue && gradeRankings.ContainsKey(x.GradeLevelId.Value) &&
                    gradeRankings[x.GradeLevelId.Value].ContainsKey(x.StudentId))
                {
                    gradeRank = gradeRankings[x.GradeLevelId.Value][x.StudentId];
                }

                return new
                {
                    x.StudentId,
                    x.StudentNo,
                    x.StudentName,
                    x.Scores,
                    x.TotalScore,
                    AvgScore = avgScore,
                    ClassRank = classRank,
                    GradeRank = gradeRank,
                    Level = classLevels.ContainsKey(x.StudentId) ? classLevels[x.StudentId] : ""
                };
            })
            .OrderByDescending(x => x.TotalScore)
            .ToList();

        // 计算竞争排名（1224制：同分同名次）
        var finalStudentScores = new List<dynamic>();
        int currentDisplayRank = 1;
        for (int i = 0; i < rankedScores.Count; i++)
        {
            if (i > 0 && rankedScores[i].TotalScore != rankedScores[i - 1].TotalScore)
                currentDisplayRank++;

            var rs = rankedScores[i];
            finalStudentScores.Add(new
            {
                Rank = currentDisplayRank,
                rs.StudentId,
                rs.StudentNo,
                rs.StudentName,
                rs.Scores,
                rs.TotalScore,
                rs.AvgScore,
                rs.ClassRank,
                rs.GradeRank,
                rs.Level
            });
        }

        // 总分临界生（仅初中适用，小学设为-1表示不适用）
        int totalCriticalStudents = -1;
        if (!isElementary)
        {
            var totalFullScore = subjects.Sum(s => (decimal)s.FullScore);
            var totalPassLine = totalFullScore * 0.6m;
            var criticalTotalMin = totalPassLine * 0.95m;
            var criticalTotalMax = totalPassLine * 1.05m;
            totalCriticalStudents = finalStudentScores.Count(s => (decimal)s.TotalScore >= criticalTotalMin && (decimal)s.TotalScore <= criticalTotalMax);
        }
        var studentCount = scores.Select(sc => sc.StudentId).Distinct().Count();

        return Json(new { success = true, subjects, studentScores = finalStudentScores, subjectStats, totalStudentCount = studentCount, totalCriticalStudents, isElementary });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "成绩分析计算异常：" + ex.Message });
        }
    }

    /// <summary>
    /// 自动修复已有错误数据：将 Score 记录的 GradeLevelId/ClassInfoId 修正为与学生实际班级一致
    /// 解决因同名班级跨年级匹配错误导致的历史脏数据问题
    /// </summary>
    private async Task AutoFixScoreClassReferences(int examScheduleId)
    {
        try
        {
            // 只取 GradeLevelId 或 ClassInfoId 为 null 的分数，以及需要校验的
            var scores = await _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId)
                .Include(sc => sc.Student)
                .ToListAsync();

            if (scores.Count == 0) return;

            // 预加载所有 ClassInfo（含 GradeLevel），避免 N+1
            var allClasses = await _db.ClassInfos
                .Include(c => c.GradeLevel)
                .ToListAsync();

            int fixedCount = 0;
            foreach (var score in scores)
            {
                var student = score.Student;
                if (student == null) continue;

                // 确定学生正确的 ClassInfoID / GradeLevelID
                int? correctClassId = null;
                int? correctGradeLevelId = null;

                // 优先用 Student.ClassID（直接外键）
                if (student.ClassID.HasValue && student.ClassID.Value > 0)
                {
                    var classInfo = allClasses.FirstOrDefault(c => c.ClassInfoID == student.ClassID.Value);
                    if (classInfo != null)
                    {
                        correctClassId = classInfo.ClassInfoID;
                        correctGradeLevelId = classInfo.GradeLevelID;
                    }
                }

                // 回退：按班级名 + 年级名精确匹配
                if (correctClassId == null && !string.IsNullOrEmpty(student.ClassName))
                {
                    var classInfo = allClasses.FirstOrDefault(c =>
                        c.ClassName == student.ClassName &&
                        c.GradeLevel != null &&
                        (c.GradeLevel.CurrentGradeName == student.Grade ||
                         c.GradeLevel.DisplayName == student.Grade));

                    if (classInfo != null)
                    {
                        correctClassId = classInfo.ClassInfoID;
                        correctGradeLevelId = classInfo.GradeLevelID;
                    }
                }

                if (correctClassId == null) continue;

                bool changed = false;
                if (score.GradeLevelId != correctGradeLevelId)
                {
                    score.GradeLevelId = correctGradeLevelId;
                    changed = true;
                }
                if (score.ClassInfoId != correctClassId)
                {
                    score.ClassInfoId = correctClassId;
                    changed = true;
                }
                if (changed) fixedCount++;
            }

            if (fixedCount > 0)
            {
                await _db.SaveChangesAsync();
                // 日志记录（静默修复，不抛异常）
                try { System.IO.File.AppendAllText(
                    AppDomain.CurrentDomain.BaseDirectory + "fix_scores.log",
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 修复考试ID={examScheduleId} 的 {fixedCount} 条成绩班级引用\n"); }
                catch { }
            }
        }
        catch
        {
            // 静默处理：修复失败不影响正常查询流程
        }
    }

    /// <summary>
    /// 成绩对比：进退分 + 名次升降
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GetComparisonData(int examScheduleId, int compareExamId, int? classInfoId)
    {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        var compareExam = await _db.ExamSchedules.FindAsync(compareExamId);
        if (exam == null || compareExam == null)
            return Json(new { success = false, message = "考试安排不存在" });

        // 获取两场考试的科目（按科目名去重）
        var subjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .Select(es => new { es.SubjectId, SubjectName = es.Subject!.Name ?? "" })
            .ToListAsync();
        subjects = subjects.GroupBy(s => s.SubjectName).Select(g => g.First()).ToList();
        var subjectIds = subjects.Select(s => s.SubjectId).ToList();

        // 获取当前考试成绩
        var query1 = _db.Scores.Where(sc => sc.ExamScheduleId == examScheduleId && subjectIds.Contains(sc.SubjectId));
        if (classInfoId.HasValue) query1 = query1.Where(sc => sc.ClassInfoId == classInfoId.Value);
        var scores1 = await query1.ToListAsync();

        // 获取对比考试成绩
        var studentIds = scores1.Select(s => s.StudentId).Distinct().ToList();
        var scores2 = await _db.Scores
            .Where(sc => sc.ExamScheduleId == compareExamId && subjectIds.Contains(sc.SubjectId) && studentIds.Contains(sc.StudentId))
            .ToListAsync();

        // 分组计算总分和排名（★ 竞争排名1224制）
        var sortedGroup1 = scores1
            .GroupBy(sc => new { sc.StudentId, StudentNo = sc.Student!.StudentNo ?? "", StudentName = sc.Student!.Name ?? "" })
            .Select(g => new
            {
                g.Key.StudentId, g.Key.StudentNo, g.Key.StudentName,
                TotalScore = g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue),
                Scores = subjects.Select(sub =>
                {
                    var sc = g.FirstOrDefault(s => s.SubjectId == sub.SubjectId);
                    if (sc == null || sc.IsAbsent)
                        return new { SubjectId = sub.SubjectId, ScoreValue = (decimal?)null, IsAbsent = sc?.IsAbsent ?? false };
                    return new { SubjectId = sub.SubjectId, ScoreValue = (decimal?)sc.ScoreValue, IsAbsent = false };
                }).ToList()
            })
            .OrderByDescending(x => x.TotalScore)
            .ToList();

        // 竞争排名（1224制）：先计算排名字典，再用 Select 构建强类型列表
        var rankDict1 = new Dictionary<int, int>();
        for (int i = 0; i < sortedGroup1.Count; i++)
        {
            if (i == 0) rankDict1[sortedGroup1[i].StudentId] = 1;
            else
            {
                int prevRank = rankDict1[sortedGroup1[i - 1].StudentId];
                rankDict1[sortedGroup1[i].StudentId] = sortedGroup1[i].TotalScore == sortedGroup1[i - 1].TotalScore ? prevRank : prevRank + 1;
            }
        }

        var group1 = sortedGroup1.Select(x => new
        {
            x.StudentId, x.StudentNo, x.StudentName, x.TotalScore,
            Rank1 = rankDict1[x.StudentId],
            x.Scores
        }).ToList();

        var sortedGroup2 = scores2
            .GroupBy(sc => sc.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                TotalScore = g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue),
                Scores = subjects.Select(sub =>
                {
                    var sc = g.FirstOrDefault(s => s.SubjectId == sub.SubjectId);
                    if (sc == null || sc.IsAbsent)
                        return new { SubjectId = sub.SubjectId, ScoreValue = (decimal?)null };
                    return new { SubjectId = sub.SubjectId, ScoreValue = (decimal?)sc.ScoreValue };
                }).ToList()
            })
            .OrderByDescending(x => x.TotalScore)
            .ToList();

        var rankDict2 = new Dictionary<int, int>();
        for (int i = 0; i < sortedGroup2.Count; i++)
        {
            if (i == 0) rankDict2[sortedGroup2[i].StudentId] = 1;
            else
            {
                int prevRank = rankDict2[sortedGroup2[i - 1].StudentId];
                rankDict2[sortedGroup2[i].StudentId] = sortedGroup2[i].TotalScore == sortedGroup2[i - 1].TotalScore ? prevRank : prevRank + 1;
            }
        }

        var group2 = sortedGroup2.Select(x => new
        {
            x.StudentId, x.TotalScore,
            Rank2 = rankDict2[x.StudentId],
            x.Scores
        }).ToList();

        var dict2 = group2.ToDictionary(g => g.StudentId);

        // 合并结果：进退分 + 名次升降
        var comparisonRows = group1.Select(g1 =>
        {
            var g2 = dict2.GetValueOrDefault(g1.StudentId);
            // 各科进退分
            var scoreDiffs = g1.Scores.Select(s1 =>
            {
                var s2 = g2?.Scores?.FirstOrDefault(s => s.SubjectId == s1.SubjectId);
                var oldVal = s2?.ScoreValue ?? 0;
                return new
                {
                    SubjectId = s1.SubjectId,
                    NewScore = s1.ScoreValue,
                    OldScore = oldVal,
                    Diff = s1.ScoreValue.HasValue ? Math.Round(s1.ScoreValue.Value - oldVal, 1) : (decimal?)null
                };
            }).ToList();

            return new
            {
                g1.StudentId, g1.StudentNo, g1.StudentName,
                g1.TotalScore,
                OldTotal = g2?.TotalScore ?? 0,
                TotalDiff = g2 != null ? Math.Round(g1.TotalScore - g2.TotalScore, 1) : 0,
                Rank1 = g1.Rank1,
                Rank2 = g2?.Rank2,
                RankChange = g2 != null ? g2.Rank2 - g1.Rank1 : (int?)null, // 负值=名次上升
                HasComparison = g2 != null,
                ScoreDiffs = scoreDiffs
            };
        }).ToList();

        return Json(new { success = true, subjects, examName = exam.Name, compareExamName = compareExam.Name, rows = comparisonRows });
    }

    [HttpPost]
    public async Task<IActionResult> GetTopStudents(int examScheduleId, int? classInfoId, int? gradeLevelId)
    {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null)
            return Json(new { success = false, message = "考试安排不存在" });

        // 解析考试覆盖的年级ID列表（过滤不属于考试年级的成绩）
        var gradeList = (exam.Grades ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        List<int> validGradeLevelIds = new();
        if (gradeList.Count > 0)
        {
            var allGradeLevels = await _db.GradeLevels.ToListAsync();
            validGradeLevelIds = allGradeLevels
                .Where(gl => gradeList.Contains(gl.CurrentGradeName))
                .Select(gl => gl.GradeLevelID)
                .ToList();
        }

        // 获取该考试的科目（按科目名去重）
        var subjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder).ThenBy(es => es.Subject!.Name)
            .Select(es => new { es.SubjectId, SubjectName = es.Subject!.Name ?? "", FullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();
        subjects = subjects.GroupBy(s => s.SubjectName).Select(g => g.First()).ToList();

        // 获取成绩（★ 年级前十取全年级数据，不含班级过滤）
        var query = _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId)
            .Include(sc => sc.Student)
            .AsQueryable();

        if (gradeLevelId.HasValue)
            query = query.Where(sc => sc.GradeLevelId == gradeLevelId.Value);
        else if (validGradeLevelIds.Count > 0)
            query = query.Where(sc => sc.GradeLevelId != null && validGradeLevelIds.Contains(sc.GradeLevelId.Value));

        var scores = await query.ToListAsync();
        if (scores.Count == 0)
            return Json(new { success = true, subjects, students = new List<object>() });

        // 按学生分组计算（排除缺考学生）
        var studentGroups = scores
            .GroupBy(sc => new { sc.StudentId, StudentNo = sc.Student?.StudentNo ?? "", StudentName = sc.Student?.Name ?? "", GradeLevelId = sc.GradeLevelId })
            .Select(g => new
            {
                g.Key.StudentId,
                g.Key.StudentNo,
                g.Key.StudentName,
                g.Key.GradeLevelId,
                SubjectCount = subjects.Count,
                PresentSubjectCount = g.Count(sc => !sc.IsAbsent),
                TotalScore = g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue),
                Scores = subjects.Select(sub => new
                {
                    SubjectId = sub.SubjectId,
                    ScoreValue = g.Where(sc => sc.SubjectId == sub.SubjectId && !sc.IsAbsent).Select(sc => (decimal?)sc.ScoreValue).FirstOrDefault(),
                    IsAbsent = g.Any(sc => sc.SubjectId == sub.SubjectId && sc.IsAbsent)
                }).ToList()
            })
            .ToList();

        // ★ 年级排名（竞争排名1224制：同分同名次）
        var gradeRanks = new Dictionary<int, Dictionary<int, int>>();
        foreach (var gradeGroup in studentGroups
            .Where(s => s.GradeLevelId.HasValue)
            .GroupBy(s => s.GradeLevelId!.Value))
        {
            var sorted = gradeGroup.OrderByDescending(s => s.TotalScore).ToList();
            var ranks = new Dictionary<int, int>();
            int currentRank = 1;
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0 && sorted[i].TotalScore != sorted[i - 1].TotalScore)
                    currentRank++;
                ranks[sorted[i].StudentId] = currentRank;
            }
            gradeRanks[gradeGroup.Key] = ranks;
        }

        // 取所有学生的年级排名，选前10
        var allWithRank = studentGroups.Select(x =>
        {
            int? gr = null;
            if (x.GradeLevelId.HasValue && gradeRanks.ContainsKey(x.GradeLevelId.Value) &&
                gradeRanks[x.GradeLevelId.Value].ContainsKey(x.StudentId))
                gr = gradeRanks[x.GradeLevelId.Value][x.StudentId];

            return new
            {
                x.StudentId,
                x.StudentNo,
                x.StudentName,
                x.TotalScore,
                AvgScore = x.PresentSubjectCount > 0 ? Math.Round((double)x.TotalScore / x.PresentSubjectCount, 1) : 0,
                GradeRank = gr,
                x.Scores
            };
        })
        .Where(x => x.GradeRank.HasValue)
        .OrderBy(x => x.GradeRank)
        .Take(10)
        .Select((x, idx) => new
        {
            Rank = idx + 1,
            x.StudentNo,
            x.StudentName,
            x.TotalScore,
            x.AvgScore,
            GradeRank = x.GradeRank,
            x.Scores
        })
        .ToList();

        return Json(new { success = true, subjects, students = allWithRank });
    }

    [HttpPost]
    public async Task<IActionResult> GetClassList(int examScheduleId)
    {
        try
        {
            var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
            if (exam == null)
                return Json(new { success = false, message = "考试安排不存在" });

            // 解析考试覆盖的年级ID列表
            var gradeList = (exam.Grades ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            List<int> validGradeLevelIds = new();
            if (gradeList.Count > 0)
            {
                var allGradeLevels = await _db.GradeLevels.ToListAsync();
                validGradeLevelIds = allGradeLevels
                    .Where(gl => gradeList.Contains(gl.CurrentGradeName))
                    .Select(gl => gl.GradeLevelID)
                    .ToList();
            }

            // 如果按 CurrentGradeName 匹配不上，尝试按显示名称匹配（如"一年级"、"七年级"等）
            if (validGradeLevelIds.Count == 0 && gradeList.Count > 0)
            {
                var allGradeLevels = await _db.GradeLevels.ToListAsync();
                validGradeLevelIds = MatchGradeLevelIdsByDisplayName(gradeList, allGradeLevels);
            }

            // 获取该考试有成绩的班级（按考试年级范围过滤）
            var rawClassesQuery = _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId && sc.ClassInfoId != null);

            if (validGradeLevelIds.Count > 0)
                rawClassesQuery = rawClassesQuery.Where(sc => sc.GradeLevelId != null && validGradeLevelIds.Contains(sc.GradeLevelId.Value));

            var rawClasses = await rawClassesQuery
                .Select(sc => new { sc.ClassInfoId, ClassName = sc.ClassInfo!.ClassName, GradeLevelId = (int?)sc.GradeLevelId, SchoolType = sc.GradeLevel != null ? sc.GradeLevel.SchoolType : "", EntryYear = sc.GradeLevel != null ? sc.GradeLevel.EntryYear : 0 })
                .Distinct()
                .OrderBy(c => c.SchoolType).ThenBy(c => c.EntryYear).ThenBy(c => c.ClassName)
                .ToListAsync();

            // 无论是否有成绩数据，都从数据库获取该考试覆盖年级的所有班级（确保所有班级都显示）
            if (gradeList.Count > 0 && validGradeLevelIds.Count > 0)
            {
                var dbClasses = await _db.ClassInfos
                    .Where(c => validGradeLevelIds.Contains(c.GradeLevelID))
                    .Select(c => new { ClassInfoId = (int?)c.ClassInfoID, ClassName = c.ClassName, GradeLevelId = (int?)c.GradeLevelID, SchoolType = c.GradeLevel != null ? c.GradeLevel.SchoolType : "", EntryYear = c.GradeLevel != null ? c.GradeLevel.EntryYear : 0 })
                    .OrderBy(c => c.SchoolType).ThenBy(c => c.EntryYear).ThenBy(c => c.ClassName)
                    .ToListAsync();

                var existingIds = new HashSet<int?>(rawClasses.Select(c => c.ClassInfoId));

                // 合并：保留已有的，添加数据库中额外存在的
                foreach (var dbc in dbClasses)
                {
                    if (!existingIds.Contains(dbc.ClassInfoId))
                        rawClasses.Add(new { dbc.ClassInfoId, dbc.ClassName, dbc.GradeLevelId, dbc.SchoolType, dbc.EntryYear });
                }
            }

            var classes = rawClasses.Select(c => new
            {
                c.ClassInfoId,
                c.ClassName,
                c.GradeLevelId,
                GradeName = (!string.IsNullOrEmpty(c.SchoolType) ? c.SchoolType + " " : "") + GetGradeDisplayName(c.SchoolType, c.EntryYear)
            }).OrderBy(c => c.GradeName).ThenBy(c => c.ClassName).ToList();

            // 如果还没有班级数据（既无成绩数据也无数据库记录），则根据考试覆盖年级生成年级占位
            if (classes.Count == 0 && gradeList.Count > 0)
            {
                var rawClasses2 = await _db.ClassInfos
                    .Where(c => validGradeLevelIds.Contains(c.GradeLevelID))
                    .Select(c => new { ClassInfoId = (int?)c.ClassInfoID, ClassName = c.ClassName, GradeLevelId = (int?)c.GradeLevelID, SchoolType = c.GradeLevel != null ? c.GradeLevel.SchoolType : "", EntryYear = c.GradeLevel != null ? c.GradeLevel.EntryYear : 0 })
                    .OrderBy(c => c.SchoolType).ThenBy(c => c.EntryYear).ThenBy(c => c.ClassName)
                    .ToListAsync();

                classes = rawClasses2.Select(c => new
                {
                    c.ClassInfoId,
                    c.ClassName,
                    c.GradeLevelId,
                    GradeName = (!string.IsNullOrEmpty(c.SchoolType) ? c.SchoolType + " " : "") + GetGradeDisplayName(c.SchoolType, c.EntryYear)
                }).ToList();
            }

            // ===== 根据当前登录用户角色过滤班级/年级 =====
            var roleType = "admin"; // admin | grade_leader | class_teacher
            var adminId = GetAdminId();
            if (!IsAdmin() && adminId.HasValue)
            {
                var admin = await _db.Admins.FindAsync(adminId.Value);
                if (admin != null)
                {
                    var primaryRole = admin.PrimaryRole;
                    if (primaryRole == "班主任" && admin.ClassID.HasValue)
                    {
                        // 班主任：只能看本班
                        roleType = "class_teacher";
                        classes = classes.Where(c => c.ClassInfoId == admin.ClassID.Value).ToList();
                    }
                    else if (primaryRole == "年级级长" && !string.IsNullOrEmpty(admin.Grade))
                    {
                        // 年级级长：只能看本年级
                        roleType = "grade_leader";
                        var allGradeLevels = await _db.GradeLevels.ToListAsync();
                        var matchedGl = allGradeLevels.FirstOrDefault(g => g.CurrentGradeName == admin.Grade);
                        if (matchedGl != null)
                        {
                            classes = classes.Where(c => c.GradeLevelId == matchedGl.GradeLevelID).ToList();
                        }
                    }
                }
            }

            // 提取年级列表（按GradeLevelId去重）
            // 优先从有成绩数据的班级提取年级
            var gradeDict = new Dictionary<int, string>();
            foreach (var c in classes)
            {
                if (c.GradeLevelId.HasValue && !gradeDict.ContainsKey(c.GradeLevelId.Value))
                    gradeDict[c.GradeLevelId.Value] = c.GradeName;
            }
            // 补充考试配置中所有年级（即使没有成绩数据也显示在下拉列表中）
            // 非管理员用户只能看到自己权限范围内的年级
            if (validGradeLevelIds.Count > 0)
            {
                var allGl = await _db.GradeLevels.ToListAsync();
                
                // 确定允许的年级ID范围
                HashSet<int> allowedGradeIds = new HashSet<int>(validGradeLevelIds);
                if (!IsAdmin())
                {
                    // 非管理员用户缩小到权限范围内的年级
                    var currentAdmin = await _db.Admins.FindAsync(adminId!.Value);
                    if (currentAdmin != null && !string.IsNullOrEmpty(currentAdmin.Grade))
                    {
                        var userGradeLevel = allGl.FirstOrDefault(g => g.CurrentGradeName == currentAdmin.Grade);
                        if (userGradeLevel != null && allowedGradeIds.Contains(userGradeLevel.GradeLevelID))
                        {
                            allowedGradeIds = new HashSet<int> { userGradeLevel.GradeLevelID };
                        }
                        else
                        {
                            allowedGradeIds.Clear();
                        }
                    }
                }
                
                foreach (var glId in validGradeLevelIds)
                {
                    if (!allowedGradeIds.Contains(glId)) continue;
                    if (!gradeDict.ContainsKey(glId))
                    {
                        var gl = allGl.FirstOrDefault(g => g.GradeLevelID == glId);
                        if (gl != null)
                        {
                            gradeDict[glId] = (!string.IsNullOrEmpty(gl.SchoolType) ? gl.SchoolType + " " : "") + GetGradeDisplayName(gl.SchoolType, gl.EntryYear);
                        }
                    }
                }
            }
            var grades = gradeDict.Select(kv => new { GradeLevelId = kv.Key, GradeName = kv.Value }).OrderBy(g => g.GradeLevelId).ToList();

            return Json(new { success = true, classes, grades, roleType });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "获取班级列表异常：" + ex.Message });
        }
    }


    /// <summary>
    /// 获取学生最近多场考试的成绩汇总（总分、平均分、班级排名、年级排名）
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetStudentScores(int id, int count = 5)
    {
        try
        {
            var student = await _db.Students.FindAsync(id);
            if (student == null) return Json(new { success = false, message = "学生不存在" });

            // 获取该学生最近参加的考试ID
            // 注意：改用Join查询，避免导航属性在EF翻译时出问题
            var examIds = await (
                from sc in _db.Scores
                join e in _db.ExamSchedules on sc.ExamScheduleId equals e.Id
                where sc.StudentId == id
                orderby e.ExamDate descending
                select sc.ExamScheduleId
            ).Distinct().Take(count).ToListAsync();

            if (examIds.Count == 0)
                return Json(new { success = true, exams = new List<object>() });

            // 批量查出所有相关考试安排
            var exams = await _db.ExamSchedules
                .Where(e => examIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            // 批量查出该学生在这几次考试中的全部成绩
            var studentScores = await _db.Scores
                .Where(sc => examIds.Contains(sc.ExamScheduleId) && sc.StudentId == id)
                .Include(sc => sc.Subject)
                .ToListAsync();

            // 批量查出这几场考试全部学生的总分（用于排名，排除缺考）
            var allTotalScores = await _db.Scores
                .Where(sc => examIds.Contains(sc.ExamScheduleId) && !sc.IsAbsent)
                .GroupBy(sc => new { sc.ExamScheduleId, sc.StudentId })
                .Select(g => new
                {
                    g.Key.ExamScheduleId,
                    g.Key.StudentId,
                    Total = g.Sum(sc => sc.ScoreValue),
                    ClassInfoId = g.Min(sc => sc.ClassInfoId),
                    GradeLevelId = g.Min(sc => sc.GradeLevelId)
                })
                .ToListAsync();

            // 批量查出这几场考试各科的极值（排除缺考）
            var allExamScoresByExam = await _db.Scores
                .Where(sc => examIds.Contains(sc.ExamScheduleId) && !sc.IsAbsent)
                .GroupBy(sc => sc.ExamScheduleId)
                .ToDictionaryAsync(g => g.Key, g => g.ToList());

            var result = new List<object>();
            foreach (var examId in examIds)
            {
                if (!exams.TryGetValue(examId, out var exam)) continue;

                var scores = studentScores.Where(sc => sc.ExamScheduleId == examId).ToList();
                if (scores.Count == 0) continue;

                var presentScores = scores.Where(sc => !sc.IsAbsent).ToList();
                var totalScore = presentScores.Sum(sc => sc.ScoreValue);
                var avgScore = presentScores.Count > 0 ? Math.Round((double)totalScore / presentScores.Count, 1) : 0;

                // 该场考试所有学生总分列表
                var examTotalScores = allTotalScores.Where(t => t.ExamScheduleId == examId).ToList();

                // 班级排名
                var studentClassInfoId = scores.FirstOrDefault()?.ClassInfoId;
                int? classRank = null;
                int? classTotal = null;
                if (studentClassInfoId.HasValue)
                {
                    var classScores = examTotalScores
                        .Where(s => s.ClassInfoId == studentClassInfoId.Value)
                        .OrderByDescending(s => s.Total)
                        .ToList();
                    classTotal = classScores.Count;
                    var pos = classScores.FindIndex(s => s.StudentId == id);
                    classRank = pos >= 0 ? pos + 1 : null;
                }

                // 年级排名（按GradeLevelId分组）
                var studentGradeLevelId = scores.FirstOrDefault()?.GradeLevelId;
                int? gradeRank = null;
                int? gradeTotal = null;
                if (studentGradeLevelId.HasValue)
                {
                    var gradeScores = examTotalScores
                        .Where(s => s.GradeLevelId == studentGradeLevelId.Value)
                        .OrderByDescending(s => s.Total)
                        .ToList();
                    gradeTotal = gradeScores.Count;
                    var gradePos = gradeScores.FindIndex(s => s.StudentId == id);
                    gradeRank = gradePos >= 0 ? gradePos + 1 : null;
                }
                else
                {
                    // 如果没有年级信息，按全部排名
                    var allOrdered = examTotalScores.OrderByDescending(s => s.Total).ToList();
                    gradeTotal = allOrdered.Count;
                    var pos = allOrdered.FindIndex(s => s.StudentId == id);
                    gradeRank = pos >= 0 ? pos + 1 : null;
                }

                // 各科极值
                var classSubjectStats = new Dictionary<int, (double Max, double Min)>();
                var gradeSubjectStats = new Dictionary<int, (double Max, double Min)>();

                allExamScoresByExam.TryGetValue(examId, out var examAllScores);
                var classScoreList = studentClassInfoId.HasValue && examAllScores != null
                    ? examAllScores.Where(sc => sc.ClassInfoId == studentClassInfoId.Value).ToList()
                    : examAllScores ?? new List<Score>();
                var gradeScoreList = studentGradeLevelId.HasValue && examAllScores != null
                    ? examAllScores.Where(sc => sc.GradeLevelId == studentGradeLevelId.Value).ToList()
                    : examAllScores ?? new List<Score>();

                foreach (var sub in scores.Select(s => s.SubjectId).Distinct())
                {
                    var classSub = classScoreList.Where(sc => sc.SubjectId == sub).ToList();
                    if (classSub.Count > 0)
                    {
                        classSubjectStats[sub] = (
                            (double)classSub.Max(sc => sc.ScoreValue),
                            (double)classSub.Min(sc => sc.ScoreValue)
                        );
                    }

                    var gradeSub = gradeScoreList.Where(sc => sc.SubjectId == sub).ToList();
                    if (gradeSub.Count > 0)
                    {
                        gradeSubjectStats[sub] = (
                            (double)gradeSub.Max(sc => sc.ScoreValue),
                            (double)gradeSub.Min(sc => sc.ScoreValue)
                        );
                    }
                }

                result.Add(new
                {
                    ExamId = examId,
                    ExamName = exam.Name,
                    ExamType = exam.ExamType,
                    ExamDate = exam.ExamDate.ToString("yyyy-MM-dd"),
                    SubjectCount = scores.Count,
                    TotalScore = totalScore,
                    AvgScore = avgScore,
                    ClassRank = classRank,
                    ClassTotal = classTotal,
                    GradeRank = gradeRank,
                    GradeTotal = gradeTotal,
                    Subjects = scores.OrderBy(sc => sc.Subject?.SortOrder).Select(sc => new
                    {
                        SubjectName = sc.Subject?.Name ?? "",
                        ScoreValue = sc.ScoreValue,
                        ClassMax = classSubjectStats.ContainsKey(sc.SubjectId) ? classSubjectStats[sc.SubjectId].Max : (double?)null,
                        ClassMin = classSubjectStats.ContainsKey(sc.SubjectId) ? classSubjectStats[sc.SubjectId].Min : (double?)null,
                        GradeMax = gradeSubjectStats.ContainsKey(sc.SubjectId) ? gradeSubjectStats[sc.SubjectId].Max : (double?)null,
                        GradeMin = gradeSubjectStats.ContainsKey(sc.SubjectId) ? gradeSubjectStats[sc.SubjectId].Min : (double?)null
                    })
                });
            }

            return Json(new { success = true, exams = result });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "查询成绩异常: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ExportExcel(int examScheduleId, int? classInfoId)
    {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null) return NotFound();

        var subjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .Select(es => es.Subject!)
            .ToListAsync();

        // 按科目名称去重（同名同分合并，同名不同分保留各自的列）
        subjects = subjects
            .GroupBy(s => new { s.Name, s.FullScore })
            .Select(g => g.First())
            .ToList();

        var query = _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId)
            .Include(sc => sc.Student)
            .AsQueryable();

        if (classInfoId.HasValue)
            query = query.Where(sc => sc.ClassInfoId == classInfoId.Value);

        var scores = await query.ToListAsync();

        var grouped = scores
            .GroupBy(sc => sc.Student)
            .OrderByDescending(g => g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue))
            .Select((g, idx) => new
            {
                Rank = idx + 1,
                StudentNo = g.Key?.StudentNo ?? "",
                StudentName = g.Key?.Name ?? "",
                Scores = subjects.Select(sub =>
                {
                    var sc = g.FirstOrDefault(s => s.SubjectId == sub.Id);
                    if (sc == null) return new { Value = (decimal?)null, IsAbsent = false };
                    return new { Value = (decimal?)sc.ScoreValue, IsAbsent = sc.IsAbsent };
                }).ToList(),
                Total = g.Where(sc => !sc.IsAbsent).Sum(sc => sc.ScoreValue)
            }).ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("成绩表");

        // 表头
        ws.Cell(1, 1).Value = "排名";
        ws.Cell(1, 2).Value = "学号";
        ws.Cell(1, 3).Value = "姓名";
        for (int i = 0; i < subjects.Count; i++)
        {
            ws.Cell(1, 4 + i).Value = subjects[i].Name;
        }
        ws.Cell(1, 4 + subjects.Count).Value = "总分";

        // 数据
        for (int r = 0; r < grouped.Count; r++)
        {
            var row = grouped[r];
            ws.Cell(r + 2, 1).Value = row.Rank;
            ws.Cell(r + 2, 2).Value = row.StudentNo;
            ws.Cell(r + 2, 3).Value = row.StudentName;
            int col = 4;
            foreach (var sc in row.Scores)
            {
                if (sc.IsAbsent)
                    ws.Cell(r + 2, col).Value = "缺考";
                else if (sc.Value.HasValue)
                    ws.Cell(r + 2, col).Value = (double)sc.Value.Value;
                else
                    ws.Cell(r + 2, col).Value = "-";
                col++;
            }
            ws.Cell(r + 2, col).Value = (double)row.Total;
        }

        ws.Columns().AdjustToContents();

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"成绩表_{exam.Name}_{DateTime.Now:yyyyMMdd}.xlsx";
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ========== 批量导入（简化版） ==========
    [HttpGet]
    public async Task<IActionResult> Import()
    {
        var exams = await _db.ExamSchedules
            .OrderByDescending(e => e.ExamDate)
            .Select(e => new { e.Id, e.Name, e.ExamType, e.ExamDate, e.Status })
            .ToListAsync();
        ViewBag.ExamSchedules = exams;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> DownloadTemplate(int examScheduleId)
    {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null) return NotFound();

        var subjectData = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .Select(es => new { es.SubjectId, es.Subject!.Name, es.Subject!.FullScore, EffectiveFullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();

        // 按科目名称去重（Name 相同视为同一科目，不同年级共用一列）
        subjectData = subjectData.GroupBy(s => s.Name).Select(g => g.First()).ToList();

        // 获取考试覆盖年级的所有学生
        var gradeList = (exam.Grades ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        IQueryable<Student> studentQuery = _db.Students.Where(s => s.Status == "在读");
        if (gradeList.Count > 0)
            studentQuery = studentQuery.Where(s => gradeList.Contains(s.Grade ?? ""));
        var students = await studentQuery
            .OrderBy(s => s.Grade).ThenBy(s => s.ClassName).ThenBy(s => s.StudentNo)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("成绩导入");

        // 标题行
        ws.Cell(1, 1).Value = "考试名称：" + exam.Name;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        ws.Cell(2, 1).Value = "创建时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

        // 空行
        ws.Row(3).Height = 10;

        // 表头（第4行）
        int headerRow = 4;
        ws.Cell(headerRow, 1).Value = "序号";
        ws.Cell(headerRow, 2).Value = "学号";
        ws.Cell(headerRow, 3).Value = "姓名";
        ws.Cell(headerRow, 4).Value = "年级";
        ws.Cell(headerRow, 5).Value = "班级";
        for (int i = 0; i < subjectData.Count; i++)
        {
            ws.Cell(headerRow, 6 + i).Value = subjectData[i].Name;
            // 添加满分备注
            ws.Cell(headerRow, 6 + i).GetComment().AddText($"满分 {subjectData[i].EffectiveFullScore}");
        }

        // 数据
        for (int i = 0; i < students.Count; i++)
        {
            var s = students[i];
            ws.Cell(headerRow + 1 + i, 1).Value = i + 1;
            ws.Cell(headerRow + 1 + i, 2).Value = s.StudentNo;
            ws.Cell(headerRow + 1 + i, 3).Value = s.Name;
            ws.Cell(headerRow + 1 + i, 4).Value = s.Grade;
            ws.Cell(headerRow + 1 + i, 5).Value = s.ClassName;
        }

        // 说明行
        int noteRow = headerRow + students.Count + 2;
        ws.Cell(noteRow, 1).Value = "填写说明：";
        ws.Cell(noteRow, 1).Style.Font.Bold = true;
        ws.Cell(noteRow + 1, 1).Value = "1. 请按学生所在科目列填写分数";
        ws.Cell(noteRow + 2, 1).Value = "2. 留空 或 填写「缺考」表示该生该科缺考";
        ws.Cell(noteRow + 3, 1).Value = "3. 缺考不计入平均分、及格率等统计";

        // 设置列宽
        ws.Column(1).Width = 6;    // 序号
        ws.Column(2).Width = 12;   // 学号
        ws.Column(3).Width = 15;   // 姓名
        ws.Column(4).Width = 8;    // 年级
        ws.Column(5).Width = 8;    // 班级
        for (int i = 0; i < subjectData.Count; i++)
        {
            ws.Column(6 + i).Width = 10;  // 科目
        }

        // 所有行高22，内容垂直居中
        int totalRows = headerRow + students.Count;
        for (int r = 1; r <= totalRows; r++)
        {
            ws.Row(r).Height = 22;
            ws.Row(r).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"导入模板_{exam.Name}_{DateTime.Now:yyyyMMdd}.xlsx");
    }

    [HttpPost]
    public async Task<IActionResult> ImportPreview(IFormFile excelFile, int examScheduleId)
    {
        if (excelFile == null || excelFile.Length == 0)
            return Json(new { success = false, message = "请上传文件" });

        if (excelFile.Length > 20 * 1024 * 1024)
            return Json(new { success = false, message = "Excel文件不能超过20MB" });

        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (exam == null)
            return Json(new { success = false, message = "考试安排不存在" });

        var subjectData = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .Select(es => new { es.SubjectId, Name = es.Subject!.Name, BaseFullScore = es.Subject!.FullScore, EffectiveFullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();

        // 按科目名称去重（Name 相同视为同一科目）
        subjectData = subjectData.GroupBy(s => s.Name).Select(g => g.First()).ToList();

        using var stream = new MemoryStream();
        await excelFile.CopyToAsync(stream);
        stream.Position = 0;

        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);

        // 动态查找表头行（查找列2包含"学号"的行）
        var rowsUsed = ws.RowsUsed().ToList();
        int headerRowIndex = 0;
        for (int r = 0; r < rowsUsed.Count; r++)
        {
            if (rowsUsed[r].Cell(2).GetString().Trim() == "学号")
            {
                headerRowIndex = r;
                break;
            }
        }
        var rows = rowsUsed.Skip(headerRowIndex + 1); // 跳过表头行，从下一行开始读取数据

        var previewRows = new List<object>();
        int successCount = 0, errorCount = 0;

        foreach (var row in rows)
        {
            var studentNo = row.Cell(2).GetString().Trim();
            var name = row.Cell(3).GetString().Trim();

            if (string.IsNullOrEmpty(studentNo) || string.IsNullOrEmpty(name))
                continue;

            // 查找学生
            var student = await _db.Students
                .FirstOrDefaultAsync(s => s.StudentNo == studentNo && s.Name == name);
            if (student == null)
            {
                previewRows.Add(new { StudentNo = studentNo, Name = name, Error = "未找到该学生", Scores = new List<object>() });
                errorCount++;
                continue;
            }

            var rowScores = new List<object>();
            bool hasError = false;
            for (int i = 0; i < subjectData.Count; i++)
            {
                var cellValue = row.Cell(6 + i).GetString().Trim();
                if (string.IsNullOrEmpty(cellValue) || cellValue == "缺考" || cellValue == "que kao")
                {
                    rowScores.Add(new { SubjectId = subjectData[i].SubjectId, SubjectName = subjectData[i].Name, Score = (decimal?)null, IsAbsent = true, Error = "" });
                    continue;
                }
                if (decimal.TryParse(cellValue, out decimal scoreVal))
                {
                    if (scoreVal >= 0 && scoreVal <= subjectData[i].EffectiveFullScore)
                    {
                        rowScores.Add(new { SubjectId = subjectData[i].SubjectId, SubjectName = subjectData[i].Name, Score = scoreVal, IsAbsent = false, Error = "" });
                    }
                    else
                    {
                        rowScores.Add(new { SubjectId = subjectData[i].SubjectId, SubjectName = subjectData[i].Name, Score = scoreVal, IsAbsent = false, Error = $"超出满分({subjectData[i].EffectiveFullScore})" });
                        hasError = true;
                    }
                }
                else
                {
                    rowScores.Add(new { SubjectId = subjectData[i].SubjectId, SubjectName = subjectData[i].Name, Score = (decimal?)null, IsAbsent = false, Error = "格式错误" });
                    hasError = true;
                }
            }

            previewRows.Add(new
            {
                StudentNo = student.StudentNo,
                Name = student.Name,
                StudentId = student.StudentID,
                Grade = student.Grade,
                ClassName = student.ClassName,
                Error = hasError ? "有错误" : "",
                Scores = rowScores
            });

            if (!hasError) successCount++;
            else errorCount++;
        }

        return Json(new
        {
            success = true,
            examScheduleId,
            rows = previewRows,
            successCount,
            errorCount,
            totalCount = previewRows.Count
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveImport([FromBody] SaveImportRequest request)
    {
        if (request?.Rows == null || request.Rows.Count == 0)
            return Json(new { success = false, message = "无数据" });

        var exam = await _db.ExamSchedules.FindAsync(request.ExamScheduleId);
        if (exam == null)
            return Json(new { success = false, message = "考试安排不存在" });

        if (exam.Status != "进行中")
            return Json(new { success = false, message = "仅「进行中」的考试可以导入成绩" });

        var studentIds = request.Rows.Select(r => r.StudentId).Distinct().ToList();
        var subjectIds = request.Rows.SelectMany(r => r.Scores).Select(s => s.SubjectId).Distinct().ToList();

        // 加载该考试的科目满分
        var fullScoreData = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == request.ExamScheduleId && subjectIds.Contains(es.SubjectId))
            .Select(es => new { es.SubjectId, FullScore = es.FullScore ?? es.Subject!.FullScore })
            .ToListAsync();
        var fullScoreMap = fullScoreData.ToDictionary(fs => fs.SubjectId, fs => fs.FullScore);

        // 校验分数是否超出满分
        var errors = new List<string>();
        foreach (var row in request.Rows)
        {
            foreach (var sc in row.Scores)
            {
                if (fullScoreMap.TryGetValue(sc.SubjectId, out var maxScore))
                {
                    if (!sc.IsAbsent && (sc.ScoreValue < 0 || sc.ScoreValue > maxScore))
                    {
                        errors.Add($"学生ID:{row.StudentId} 科目ID:{sc.SubjectId} 分数 {sc.ScoreValue} 超出满分 {maxScore}");
                    }
                }
            }
        }
        if (errors.Count > 0)
            return Json(new { success = false, message = "存在超出满分的成绩:\n" + string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? $"\n...还有{errors.Count - 20}条错误" : "") });

        // 批量加载已有成绩
        var existingList = await _db.Scores
            .Where(sc => sc.ExamScheduleId == request.ExamScheduleId
                      && studentIds.Contains(sc.StudentId)
                      && subjectIds.Contains(sc.SubjectId))
            .ToListAsync();
        var existingDict = existingList.ToDictionary(sc => $"{sc.StudentId}_{sc.SubjectId}");

        // 加载学生班级信息
        var students = await _db.Students.Where(s => studentIds.Contains(s.StudentID)).ToListAsync();
        var studentDict = students.ToDictionary(s => s.StudentID);

        int saved = 0;
        foreach (var row in request.Rows)
        {
            studentDict.TryGetValue(row.StudentId, out var student);
            foreach (var sc in row.Scores)
            {
                var key = $"{row.StudentId}_{sc.SubjectId}";
                if (existingDict.TryGetValue(key, out var existing))
                {
                    existing.ScoreValue = sc.ScoreValue;
                    existing.IsAbsent = sc.IsAbsent;
                    // 同时修正可能错误的 ClassInfoId/GradeLevelId
                    if (student != null)
                    {
                        ClassInfo? existingCi = null;
                        if (student.ClassID.HasValue && student.ClassID.Value > 0)
                        {
                            existingCi = await _db.ClassInfos
                                .FirstOrDefaultAsync(c => c.ClassInfoID == student.ClassID.Value);
                        }
                        if (existingCi == null && !string.IsNullOrEmpty(student.ClassName))
                        {
                            existingCi = await _db.ClassInfos
                                .Include(c => c.GradeLevel)
                                .FirstOrDefaultAsync(c => c.ClassName == student.ClassName
                                    && c.GradeLevel != null
                                    && (c.GradeLevel.CurrentGradeName == student.Grade
                                        || c.GradeLevel.DisplayName == student.Grade));
                        }
                        if (existingCi != null)
                        {
                            existing.ClassInfoId = existingCi.ClassInfoID;
                            existing.GradeLevelId = existingCi.GradeLevelID;
                        }
                    }
                }
                else
                {
                    var newScore = new Score
                    {
                        StudentId = row.StudentId,
                        SubjectId = sc.SubjectId,
                        ScoreValue = sc.ScoreValue,
                        IsAbsent = sc.IsAbsent,
                        ExamScheduleId = request.ExamScheduleId,
                        ExamType = exam.ExamType,
                        ExamDate = exam.ExamDate,
                        CreateTime = DateTime.Now,
                    };
                    if (student != null)
                    {
                        ClassInfo? classInfo = null;

                        if (student.ClassID.HasValue && student.ClassID.Value > 0)
                        {
                            classInfo = await _db.ClassInfos
                                .FirstOrDefaultAsync(c => c.ClassInfoID == student.ClassID.Value);
                        }

                        if (classInfo == null && !string.IsNullOrEmpty(student.ClassName))
                        {
                            classInfo = await _db.ClassInfos
                                .Include(c => c.GradeLevel)
                                .FirstOrDefaultAsync(c => c.ClassName == student.ClassName
                                    && c.GradeLevel != null
                                    && (c.GradeLevel.CurrentGradeName == student.Grade
                                        || c.GradeLevel.DisplayName == student.Grade));
                        }

                        if (classInfo != null)
                        {
                            newScore.ClassInfoId = classInfo.ClassInfoID;
                            newScore.GradeLevelId = classInfo.GradeLevelID;
                        }
                    }
                    _db.Scores.Add(newScore);
                }
                saved++;
            }
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true, message = $"成功导入 {saved} 条成绩" });
    }

    private static string GetGradeDisplayName(string schoolType, int entryYear)
    {
        int currentYear = DateTime.Now.Year;
        int offset = currentYear - entryYear;
        if (schoolType == "小学")
        {
            if (offset < 0) return "未入学";
            if (offset >= 6) return "已毕业(小学)";
            var names = new[] { "一年级", "二年级", "三年级", "四年级", "五年级", "六年级" };
            return names[offset];
        }
        else if (schoolType == "初中")
        {
            if (offset < 0) return "未入学";
            if (offset >= 3) return "已毕业(初中)";
            var names = new[] { "七年级", "八年级", "九年级" };
            return names[offset];
        }
        return "未知";
    }

    /// <summary>
    /// 获取单科等级（基于分数占比）
    /// </summary>
    private static string GetSubjectLevel(decimal scoreValue, decimal fullScore)
    {
        if (fullScore <= 0) return "C";
        double ratio = (double)(scoreValue / fullScore);
        if (ratio >= 0.9) return "A";
        if (ratio >= 0.75) return "B";
        if (ratio >= 0.6) return "C";
        return "D";
    }

    /// <summary>
    /// 根据年级显示名称匹配 GradeLevelId（用于当 exam.Grades 存储的是显示名称时）
    /// </summary>
    private static List<int> MatchGradeLevelIdsByDisplayName(List<string> gradeNames, List<GradeLevel> allGradeLevels)
    {
        var ids = new List<int>();
        if (gradeNames.Count == 0 || allGradeLevels.Count == 0) return ids;

        var currentYear = DateTime.Now.Year;
        foreach (var gradeName in gradeNames)
        {
            var trimmed = gradeName.Trim();
            var matched = allGradeLevels.FirstOrDefault(gl =>
            {
                var displayName = GetGradeDisplayName(gl.SchoolType, gl.EntryYear);
                var fullName = (gl.SchoolType + " " + displayName).Trim();
                return displayName == trimmed || fullName == trimmed;
            });
            if (matched != null && !ids.Contains(matched.GradeLevelID))
                ids.Add(matched.GradeLevelID);
        }
        return ids;
    }

    // ========== AI成绩分析 ==========

    /// <summary>
    /// 获取AI分析可用学生列表
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GetAiStudents(int examScheduleId, int? classInfoId)
    {
        try
        {
            var query = _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId)
                .Include(sc => sc.Student)
                .AsQueryable();

            if (classInfoId.HasValue)
                query = query.Where(sc => sc.ClassInfoId == classInfoId.Value);

            var students = await query
                .GroupBy(sc => new { sc.StudentId, StudentNo = sc.Student!.StudentNo ?? "", StudentName = sc.Student!.Name ?? "" })
                .Select(g => new
                {
                    StudentId = g.Key.StudentId,
                    StudentNo = g.Key.StudentNo,
                    StudentName = g.Key.StudentName
                })
                .OrderBy(s => s.StudentNo)
                .ToListAsync();

            return Json(new { success = true, students });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载学生列表失败: " + ex.Message });
        }
    }

    /// <summary>
    /// AI成绩分析 - 生成个人分析报告
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetAiAnalysis(int examScheduleId, int studentId, bool forceRegenerate = false)
    {
        try
        {
            // 0. 检查缓存
            if (!forceRegenerate)
            {
                var cached = await _db.AiAnalysisResults
                    .Where(r => r.ExamScheduleId == examScheduleId && r.StudentId == studentId)
                    .FirstOrDefaultAsync();

                if (cached != null && !string.IsNullOrEmpty(cached.AnalysisResult))
                {
                    var stu = await _db.Students.FindAsync(studentId);
                    return Json(new { success = true, analysis = cached.AnalysisResult, studentName = stu?.Name ?? "", cached = true, updatedAt = cached.UpdatedAt.ToString("yyyy-MM-dd HH:mm") });
                }
            }

            // 1. 从数据库读取AI配置
            var configs = await _db.SiteConfigs
                .Where(c => c.ConfigKey!.StartsWith("Ai"))
                .ToListAsync();
            var dict = configs.ToDictionary(c => c.ConfigKey ?? "", c => c.ConfigValue ?? "");

            var apiUrl = dict.GetValueOrDefault("AiApiUrl", "");
            var apiKey = dict.GetValueOrDefault("AiApiKey", "");
            var modelName = dict.GetValueOrDefault("AiModelName", "");

            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelName))
                return Json(new { success = false, message = "请先在「成绩管理 → AI设置」中配置API信息" });

            double.TryParse(dict.GetValueOrDefault("AiTemperature", "0.7"), out var temperature);
            int.TryParse(dict.GetValueOrDefault("AiMaxTokens", "2048"), out var maxTokens);
            int.TryParse(dict.GetValueOrDefault("AiTimeout", "60"), out var timeout);

            // 2. 获取学生和考试信息
            var student = await _db.Students.FindAsync(studentId);
            var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
            if (student == null || exam == null)
                return Json(new { success = false, message = "学生或考试不存在" });

            // 3. 获取科目列表
            var subjects = await _db.ExamSubjects
                .Where(es => es.ExamScheduleId == examScheduleId)
                .Include(es => es.Subject)
                .OrderBy(es => es.Subject!.SortOrder)
                .Select(es => new
                {
                    es.SubjectId,
                    SubjectName = es.Subject!.Name ?? "",
                    FullScore = es.FullScore ?? es.Subject!.FullScore
                })
                .ToListAsync();

            // 4. 获取该生当前考试成绩
            var currentScores = await _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId && sc.StudentId == studentId)
                .ToListAsync();
            var currentScoreMap = currentScores.ToDictionary(sc => sc.SubjectId, sc => (double)sc.ScoreValue);

            // 5. 获取该场考试全部学生数据（用于统计）
            var allScores = await _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId)
                .ToListAsync();

            var classInfoId = currentScores.FirstOrDefault()?.ClassInfoId;
            var gradeLevelId = currentScores.FirstOrDefault()?.GradeLevelId;

            // 判断学段（小学/初中），用于 Prompt 角色设定
            var schoolType = "中学";
            if (gradeLevelId.HasValue)
            {
                var gradeLevel = await _db.GradeLevels.FindAsync(gradeLevelId.Value);
                if (gradeLevel != null && gradeLevel.SchoolType == "小学")
                    schoolType = "小学";
            }

            // 6. 各科班级/年级统计
            var classStats = new Dictionary<int, (double Avg, double Max, double Min)>();
            var gradeStats = new Dictionary<int, (double Avg, double Max, double Min)>();

            foreach (var sub in subjects)
            {
                var classSub = allScores.Where(sc => sc.SubjectId == sub.SubjectId && sc.ClassInfoId == classInfoId && !sc.IsAbsent).ToList();
                if (classSub.Count > 0)
                    classStats[sub.SubjectId] = (Math.Round(classSub.Average(sc => (double)sc.ScoreValue), 1), (double)classSub.Max(sc => sc.ScoreValue), (double)classSub.Min(sc => sc.ScoreValue));

                var gradeSub = allScores.Where(sc => sc.SubjectId == sub.SubjectId && sc.GradeLevelId == gradeLevelId && !sc.IsAbsent).ToList();
                if (gradeSub.Count > 0)
                    gradeStats[sub.SubjectId] = (Math.Round(gradeSub.Average(sc => (double)sc.ScoreValue), 1), (double)gradeSub.Max(sc => sc.ScoreValue), (double)gradeSub.Min(sc => sc.ScoreValue));
            }

            // 7. 计算总分和排名
            var presentScores = currentScores.Where(sc => !sc.IsAbsent).ToList();
            var studentTotal = presentScores.Sum(sc => (double)sc.ScoreValue);

            var allTotals = allScores
                .Where(sc => !sc.IsAbsent)
                .GroupBy(sc => sc.StudentId)
                .Select(g => new { StudentId = g.Key, Total = g.Sum(sc => (double)sc.ScoreValue), ClassInfoId = g.Min(sc => sc.ClassInfoId), GradeLevelId = g.Min(sc => sc.GradeLevelId) })
                .ToList();

            int? classRank = null, classTotal = null, gradeRank = null, gradeTotal = null;
            if (classInfoId.HasValue)
            {
                var ct = allTotals.Where(t => t.ClassInfoId == classInfoId).OrderByDescending(t => t.Total).ToList();
                classTotal = ct.Count;
                var pos = ct.FindIndex(t => t.StudentId == studentId);
                classRank = pos >= 0 ? pos + 1 : null;
            }
            if (gradeLevelId.HasValue)
            {
                var gt = allTotals.Where(t => t.GradeLevelId == gradeLevelId).OrderByDescending(t => t.Total).ToList();
                gradeTotal = gt.Count;
                var pos = gt.FindIndex(t => t.StudentId == studentId);
                gradeRank = pos >= 0 ? pos + 1 : null;
            }

            // 8. 获取最近5次考试历史
            var recentExamIds = await _db.Scores
                .Where(sc => sc.StudentId == studentId)
                .GroupBy(sc => sc.ExamScheduleId)
                .Select(g => g.Key)
                .Take(5)
                .ToListAsync();

            var recentExams = await _db.ExamSchedules
                .Where(e => recentExamIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id);

            var recentScoreData = await _db.Scores
                .Where(sc => recentExamIds.Contains(sc.ExamScheduleId) && sc.StudentId == studentId)
                .ToListAsync();

            var historySummary = recentScoreData
                .GroupBy(sc => sc.ExamScheduleId)
                .OrderByDescending(g => recentExams.GetValueOrDefault(g.Key)?.ExamDate)
                .Select(g =>
                {
                    var ex = recentExams.GetValueOrDefault(g.Key);
                    var present = g.Where(sc => !sc.IsAbsent).ToList();
                    var total = present.Sum(sc => (double)sc.ScoreValue);
                    return new
                    {
                        ExamName = ex?.Name ?? "未知",
                        ExamDate = ex?.ExamDate.ToString("yyyy-MM-dd") ?? "",
                        TotalScore = total,
                        AvgScore = present.Count > 0 ? Math.Round(present.Average(sc => (double)sc.ScoreValue), 1) : 0
                    };
                })
                .ToList();

            // 9. 构建Prompt
            var prompt = BuildAnalysisPrompt(student, exam, subjects, currentScoreMap, studentTotal,
                classRank, classTotal, gradeRank, gradeTotal, classStats, gradeStats, historySummary, schoolType);

            // 10. 调用AI API
            var analysis = await _aiService.GenerateAnalysisAsync(
                prompt, apiUrl, apiKey, modelName, temperature, maxTokens, timeout);

            // 11. 缓存到数据库
            var existing = await _db.AiAnalysisResults
                .Where(r => r.ExamScheduleId == examScheduleId && r.StudentId == studentId)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                existing.AnalysisResult = analysis;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _db.AiAnalysisResults.Add(new AiAnalysisResult
                {
                    ExamScheduleId = examScheduleId,
                    StudentId = studentId,
                    AnalysisResult = analysis,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, analysis, studentName = student.Name, cached = false });
        }
        catch (OperationCanceledException)
        {
            return Json(new { success = false, message = "AI请求超时，请在「AI设置」中增大超时时间（建议120秒以上），或检查网络连接" });
        }
        catch (HttpRequestException ex)
        {
            return Json(new { success = false, message = "AI服务调用失败: " + ex.Message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "AI分析失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 批量AI分析 - 逐学生生成报告并缓存
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchAiAnalysis(int examScheduleId, int? gradeLevelId, int? classInfoId)
    {
        try
        {
            // 1. 获取该场考试的所有学生
            var query = _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId)
                .AsQueryable();

            if (gradeLevelId.HasValue)
                query = query.Where(sc => sc.GradeLevelId == gradeLevelId.Value);
            if (classInfoId.HasValue)
                query = query.Where(sc => sc.ClassInfoId == classInfoId.Value);

            var studentIds = await query
                .Select(sc => sc.StudentId)
                .Distinct()
                .ToListAsync();

            var total = studentIds.Count;

            // 2. 查已缓存的学生
            var cachedIds = await _db.AiAnalysisResults
                .Where(r => r.ExamScheduleId == examScheduleId && studentIds.Contains(r.StudentId))
                .Select(r => r.StudentId)
                .ToListAsync();

            var uncachedIds = studentIds.Except(cachedIds).ToList();
            var cachedCount = cachedIds.Count;

            // 3. 最多处理5个未缓存的
            var batchSize = Math.Min(uncachedIds.Count, 5);
            var toProcess = uncachedIds.Take(batchSize).ToList();
            var processed = 0;
            var errors = new List<string>();

            // 4. 读取AI配置
            var configs = await _db.SiteConfigs
                .Where(c => c.ConfigKey!.StartsWith("Ai"))
                .ToListAsync();
            var dict = configs.ToDictionary(c => c.ConfigKey ?? "", c => c.ConfigValue ?? "");

            var apiUrl = dict.GetValueOrDefault("AiApiUrl", "");
            var apiKey = dict.GetValueOrDefault("AiApiKey", "");
            var modelName = dict.GetValueOrDefault("AiModelName", "");

            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelName))
                return Json(new { success = false, message = "请先在「成绩管理 → AI设置」中配置API信息" });

            double.TryParse(dict.GetValueOrDefault("AiTemperature", "0.7"), out var temperature);
            int.TryParse(dict.GetValueOrDefault("AiMaxTokens", "2048"), out var maxTokens);
            int.TryParse(dict.GetValueOrDefault("AiTimeout", "60"), out var timeout);

            // 5. 逐个处理
            foreach (var sid in toProcess)
            {
                try
                {
                    // 调用已有的分析方法（会自主缓存）
                    var analysis = await GenerateSingleAnalysis(
                        sid, examScheduleId,
                        apiUrl, apiKey, modelName, temperature, maxTokens, timeout);

                    if (analysis != null)
                    {
                        // 缓存
                        var existing = await _db.AiAnalysisResults
                            .Where(r => r.ExamScheduleId == examScheduleId && r.StudentId == sid)
                            .FirstOrDefaultAsync();

                        if (existing != null)
                        {
                            existing.AnalysisResult = analysis;
                            existing.UpdatedAt = DateTime.Now;
                        }
                        else
                        {
                            _db.AiAnalysisResults.Add(new AiAnalysisResult
                            {
                                ExamScheduleId = examScheduleId,
                                StudentId = sid,
                                AnalysisResult = analysis,
                                CreatedAt = DateTime.Now,
                                UpdatedAt = DateTime.Now
                            });
                        }
                        await _db.SaveChangesAsync();
                        processed++;
                    }
                }
                catch (Exception ex)
                {
                    var stu = await _db.Students.FindAsync(sid);
                    errors.Add($"{stu?.Name ?? "#" + sid}: {ex.Message.Truncate(50)}");
                }
            }

            var remaining = uncachedIds.Count - batchSize;

            return Json(new
            {
                success = true,
                processed,
                cachedCount,
                remaining = remaining < 0 ? 0 : remaining,
                total,
                errors = errors.Count > 0 ? string.Join("; ", errors) : null,
                done = (cachedCount + processed) >= total
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "批量分析失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 获取单学生的缓存AI报告
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GetStudentAiReport(int examScheduleId, int studentId)
    {
        try
        {
            var cached = await _db.AiAnalysisResults
                .Where(r => r.ExamScheduleId == examScheduleId && r.StudentId == studentId)
                .FirstOrDefaultAsync();

             if (cached == null || string.IsNullOrEmpty(cached.AnalysisResult))
            {
                // 调试：查该学生所有已缓存的报告及对应的考试
                var studentReports = await _db.AiAnalysisResults
                    .Where(r => r.StudentId == studentId)
                    .Select(r => new { r.ExamScheduleId })
                    .ToListAsync();

                var examIdsFound = studentReports.Select(r => r.ExamScheduleId).ToList();
                var examsFound = await _db.ExamSchedules
                    .Where(e => examIdsFound.Contains(e.Id))
                    .Select(e => new { e.Id, e.Name })
                    .ToListAsync();

                var reportExams = studentReports
                    .GroupBy(r => r.ExamScheduleId)
                    .Select(g => new
                    {
                        ExamScheduleId = g.Key,
                        ExamName = examsFound.FirstOrDefault(e => e.Id == g.Key)?.Name ?? "(未知考试)"
                    })
                    .ToList();

                return Json(new
                {
                    success = false,
                    exists = false,
                    message = "暂无匹配的AI分析报告",
                    debug = new
                    {
                        receivedExamScheduleId = examScheduleId,
                        receivedStudentId = studentId,
                        cachedReportCount = reportExams.Count,
                        cachedExams = reportExams.Select(e => new { examId = e.ExamScheduleId, examName = e.ExamName }).ToList()
                    }
                });
            }

            var student = await _db.Students.FindAsync(studentId);
            return Json(new
            {
                success = true,
                exists = true,
                analysis = cached.AnalysisResult,
                studentName = student?.Name ?? "",
                updatedAt = cached.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载AI报告失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 单学生AI分析（内部批量调用）
    /// </summary>
    private async Task<string?> GenerateSingleAnalysis(
        int studentId, int examScheduleId,
        string apiUrl, string apiKey, string modelName,
        double temperature, int maxTokens, int timeout)
    {
        var student = await _db.Students.FindAsync(studentId);
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (student == null || exam == null) return null;

        var subjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId)
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .Select(es => new
            {
                es.SubjectId,
                SubjectName = es.Subject!.Name ?? "",
                FullScore = es.FullScore ?? es.Subject!.FullScore
            })
            .ToListAsync();

        var currentScores = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId && sc.StudentId == studentId)
            .ToListAsync();
        var currentScoreMap = currentScores.ToDictionary(sc => sc.SubjectId, sc => (double)sc.ScoreValue);

        var allScores = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId)
            .ToListAsync();

        var classInfoId = currentScores.FirstOrDefault()?.ClassInfoId;
        var gradeLevelId = currentScores.FirstOrDefault()?.GradeLevelId;

        // 判断学段（小学/初中），用于 Prompt 角色设定
        var schoolType = "中学";
        if (gradeLevelId.HasValue)
        {
            var gradeLevel = await _db.GradeLevels.FindAsync(gradeLevelId.Value);
            if (gradeLevel != null && gradeLevel.SchoolType == "小学")
                schoolType = "小学";
        }

        var classStats = new Dictionary<int, (double Avg, double Max, double Min)>();
        var gradeStats = new Dictionary<int, (double Avg, double Max, double Min)>();

        foreach (var sub in subjects)
        {
            var classSub = allScores.Where(sc => sc.SubjectId == sub.SubjectId && sc.ClassInfoId == classInfoId && !sc.IsAbsent).ToList();
            if (classSub.Count > 0)
                classStats[sub.SubjectId] = (Math.Round(classSub.Average(sc => (double)sc.ScoreValue), 1), (double)classSub.Max(sc => sc.ScoreValue), (double)classSub.Min(sc => sc.ScoreValue));

            var gradeSub = allScores.Where(sc => sc.SubjectId == sub.SubjectId && sc.GradeLevelId == gradeLevelId && !sc.IsAbsent).ToList();
            if (gradeSub.Count > 0)
                gradeStats[sub.SubjectId] = (Math.Round(gradeSub.Average(sc => (double)sc.ScoreValue), 1), (double)gradeSub.Max(sc => sc.ScoreValue), (double)gradeSub.Min(sc => sc.ScoreValue));
        }

        var presentScores = currentScores.Where(sc => !sc.IsAbsent).ToList();
        var studentTotal = presentScores.Sum(sc => (double)sc.ScoreValue);

        var allTotals = allScores
            .Where(sc => !sc.IsAbsent)
            .GroupBy(sc => sc.StudentId)
            .Select(g => new { StudentId = g.Key, Total = g.Sum(sc => (double)sc.ScoreValue), ClassInfoId = g.Min(sc => sc.ClassInfoId), GradeLevelId = g.Min(sc => sc.GradeLevelId) })
            .ToList();

        int? classRank = null, classTotal = null, gradeRank = null, gradeTotal = null;
        if (classInfoId.HasValue)
        {
            var ct = allTotals.Where(t => t.ClassInfoId == classInfoId).OrderByDescending(t => t.Total).ToList();
            classTotal = ct.Count;
            var pos = ct.FindIndex(t => t.StudentId == studentId);
            classRank = pos >= 0 ? pos + 1 : null;
        }
        if (gradeLevelId.HasValue)
        {
            var gt = allTotals.Where(t => t.GradeLevelId == gradeLevelId).OrderByDescending(t => t.Total).ToList();
            gradeTotal = gt.Count;
            var pos = gt.FindIndex(t => t.StudentId == studentId);
            gradeRank = pos >= 0 ? pos + 1 : null;
        }

        var recentExamIds = await _db.Scores
            .Where(sc => sc.StudentId == studentId)
            .GroupBy(sc => sc.ExamScheduleId)
            .Select(g => g.Key)
            .Take(5)
            .ToListAsync();

        var recentExams = await _db.ExamSchedules
            .Where(e => recentExamIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        var recentScoreData = await _db.Scores
            .Where(sc => recentExamIds.Contains(sc.ExamScheduleId) && sc.StudentId == studentId)
            .ToListAsync();

        var historySummary = recentScoreData
            .GroupBy(sc => sc.ExamScheduleId)
            .OrderByDescending(g => recentExams.GetValueOrDefault(g.Key)?.ExamDate)
            .Select(g =>
            {
                var ex = recentExams.GetValueOrDefault(g.Key);
                var present = g.Where(sc => !sc.IsAbsent).ToList();
                var total = present.Sum(sc => (double)sc.ScoreValue);
                return new
                {
                    ExamName = ex?.Name ?? "未知",
                    ExamDate = ex?.ExamDate.ToString("yyyy-MM-dd") ?? "",
                    TotalScore = total,
                    AvgScore = present.Count > 0 ? Math.Round(present.Average(sc => (double)sc.ScoreValue), 1) : 0
                };
            })
            .ToList();

        var prompt = BuildAnalysisPrompt(student, exam, subjects, currentScoreMap, studentTotal,
            classRank, classTotal, gradeRank, gradeTotal, classStats, gradeStats, historySummary, schoolType);

        return await _aiService.GenerateAnalysisAsync(
            prompt, apiUrl, apiKey, modelName, temperature, maxTokens, timeout);
    }

    // ========== Prompt构建 ==========

    private string BuildAnalysisPrompt(
        Student student,
        ExamSchedule exam,
        IEnumerable<dynamic> subjects,
        Dictionary<int, double> currentScoreMap,
        double totalScore,
        int? classRank, int? classTotal,
        int? gradeRank, int? gradeTotal,
        Dictionary<int, (double Avg, double Max, double Min)> classStats,
        Dictionary<int, (double Avg, double Max, double Min)> gradeStats,
        IEnumerable<dynamic> historySummary,
        string schoolType)
    {
        var sb = new StringBuilder();
        var roleName = schoolType == "小学" ? "小学教务主任和学习分析师" : "中学教务主任和学业分析师";
        sb.AppendLine($"你是一位经验丰富的{roleName}。请根据以下学生成绩数据，生成一份详细、准确、有建设性的个人成绩分析报告。");
        sb.AppendLine();
        sb.AppendLine("## 学生基本信息");
        sb.AppendLine($"- 姓名：{student.Name}");
        sb.AppendLine($"- 学号：{student.StudentNo}");
        sb.AppendLine($"- 年级：{student.Grade}");
        sb.AppendLine($"- 班级：{student.ClassName}");
        sb.AppendLine($"- 考试：{exam.Name}");
        sb.AppendLine($"- 考试类型：{exam.ExamType}");
        sb.AppendLine($"- 考试日期：{exam.ExamDate:yyyy-MM-dd}");
        sb.AppendLine();

        sb.AppendLine("## 当前考试成绩");
        sb.AppendLine($"- 总分：{totalScore} 分");
        sb.AppendLine($"- 班级排名：{classRank?.ToString() ?? "未知"}/{classTotal?.ToString() ?? "未知"}");
        sb.AppendLine($"- 年级排名：{gradeRank?.ToString() ?? "未知"}/{gradeTotal?.ToString() ?? "未知"}");
        sb.AppendLine();

        sb.AppendLine("### 各科目成绩详情");
        sb.AppendLine("| 科目 | 得分 | 满分 | 得分率 | 班级均分 | 班级最高 | 班级最低 | 年级均分 |");
        sb.AppendLine("|------|------|------|--------|----------|----------|----------|----------|");

        foreach (var sub in subjects)
        {
            double score = 0;
            currentScoreMap.TryGetValue(sub.SubjectId, out score);
            int fullScore = sub.FullScore > 0 ? sub.FullScore : 100;
            double rate = Math.Round(score / fullScore * 100, 1);

            var cs = (Avg: 0.0, Max: 0.0, Min: 0.0);
            var gs = (Avg: 0.0, Max: 0.0, Min: 0.0);
            if (classStats.ContainsKey(sub.SubjectId)) cs = classStats[sub.SubjectId];
            if (gradeStats.ContainsKey(sub.SubjectId)) gs = gradeStats[sub.SubjectId];

            sb.AppendLine($"| {sub.SubjectName} | {score} | {fullScore} | {rate}% | {cs.Avg:F1} | {cs.Max:F1} | {cs.Min:F1} | {gs.Avg:F1} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 历史成绩趋势");
        sb.AppendLine("| 考试名称 | 日期 | 总分 | 平均分 |");
        sb.AppendLine("|----------|------|------|--------|");
        foreach (var h in historySummary)
        {
            sb.AppendLine($"| {h.ExamName} | {h.ExamDate} | {h.TotalScore} | {h.AvgScore} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 分析要求");
        sb.AppendLine("请从以下维度进行分析，输出中文、条理清晰、数据准确：");
        sb.AppendLine();
        sb.AppendLine("### 一、总体评价");
        sb.AppendLine("基于总分和排名，给出该生本次考试的整体表现评价（优秀/良好/中等/待提高）。");
        sb.AppendLine();
        sb.AppendLine("### 二、优势学科分析");
        sb.AppendLine("找出得分率超过80%或明显高于班级平均分的科目，说明优势所在。");
        sb.AppendLine();
        sb.AppendLine("### 三、薄弱学科分析");
        sb.AppendLine("找出得分率低于60%或明显低于班级平均分的科目，分析薄弱程度。");
        sb.AppendLine();
        sb.AppendLine("### 四、成绩稳定性分析");
        sb.AppendLine("对比最近几场考试的总分变化趋势（上升/下降/波动/稳定）。");
        sb.AppendLine();
        sb.AppendLine("### 五、班级与年级对比");
        sb.AppendLine("分析该生在班级和年级中的相对位置，各科与班级/年级均分的差距。");
        sb.AppendLine();
        sb.AppendLine("### 六、学习建议");
        sb.AppendLine("针对薄弱学科给出具体、可操作的学习建议，建议保持优势学科的学习方法。");
        sb.AppendLine();
        sb.AppendLine("### 注意事项");
        sb.AppendLine("- 所有分析必须基于上面提供的数据，不要编造数据");
        sb.AppendLine("- 数据中缺失的信息请如实说明");
        sb.AppendLine("- 语言要积极正面，避免打击学生自信心");
        sb.AppendLine("- 给建议时要求具体，不要笼统说'多努力''加油'之类");

        return sb.ToString();
    }

    // ========== AI设置 ==========

    /// <summary>
    /// 获取AI设置
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAiSettings()
    {
        try
        {
            var configs = await _db.SiteConfigs
                .Where(c => c.ConfigKey!.StartsWith("Ai"))
                .ToListAsync();
            var dict = configs.ToDictionary(c => c.ConfigKey ?? "", c => c.ConfigValue ?? "");

            // API Key 脱敏：只返回末4位，中间用星号代替
            var rawKey = dict.GetValueOrDefault("AiApiKey", "");
            var maskedKey = MaskApiKey(rawKey);

            return Json(new
            {
                success = true,
                apiUrl = dict.GetValueOrDefault("AiApiUrl", ""),
                apiKey = maskedKey,
                hasKey = !string.IsNullOrEmpty(rawKey),
                modelName = dict.GetValueOrDefault("AiModelName", ""),
                temperature = dict.TryGetValue("AiTemperature", out var t) && double.TryParse(t, out var dv) ? dv : 0.7,
                maxTokens = dict.TryGetValue("AiMaxTokens", out var mt) && int.TryParse(mt, out var mv) ? mv : 2048,
                timeout = dict.TryGetValue("AiTimeout", out var to) && int.TryParse(to, out var ov) ? ov : 60
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载AI设置失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 保存AI设置
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAiSettings(string apiUrl, string apiKey, string modelName, string temperature, string maxTokens, string timeout)
    {
        try
        {
            var configs = new[]
            {
                ("AiApiUrl", apiUrl),
                ("AiApiKey", apiKey),
                ("AiModelName", modelName),
                ("AiTemperature", temperature),
                ("AiMaxTokens", maxTokens),
                ("AiTimeout", timeout)
            };

            foreach (var (key, value) in configs)
            {
                // API Key 如果是脱敏后的掩码值（含 ****）或为空，不覆盖数据库中的实际密钥
                if (key == "AiApiKey" && (IsMaskedApiKey(value) || string.IsNullOrEmpty(value)))
                    continue;

                var existing = await _db.SiteConfigs.FindAsync(key);
                if (existing != null)
                {
                    existing.ConfigValue = value;
                }
                else
                {
                    _db.SiteConfigs.Add(new SiteConfig { ConfigKey = key, ConfigValue = value });
                }
            }

            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "保存AI设置失败: " + ex.Message });
        }
    }

    /// <summary>
    /// API Key 脱敏：只保留末4位，中间用星号代替
    /// </summary>
    private static string MaskApiKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length < 8)
            return key.Length > 0 ? key.Substring(0, 1) + "****" : "";
        return key.Substring(0, 4) + "****" + key.Substring(key.Length - 4);
    }

    /// <summary>
    /// 判断 API Key 是否为脱敏后的掩码值
    /// </summary>
    private static bool IsMaskedApiKey(string key)
    {
        return !string.IsNullOrEmpty(key) && key.Contains("****");
    }

    /// <summary>
    /// 测试AI API连接
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestAi(string apiUrl, string apiKey, string modelName, string temperature, string maxTokens, string timeout)
    {
        try
        {
            double.TryParse(temperature, out var temp);
            int.TryParse(maxTokens, out var tokens);
            int.TryParse(timeout, out var timeoutSec);

            if (tokens <= 0) tokens = 64;
            if (timeoutSec <= 0) timeoutSec = 15;

            // 如果 API Key 是掩码值，从数据库取真实 Key
            if (IsMaskedApiKey(apiKey))
            {
                var cfg = await _db.SiteConfigs.FindAsync("AiApiKey");
                if (cfg != null)
                    apiKey = cfg.ConfigValue ?? "";
            }

            var (success, message) = await _aiService.TestConnectionAsync(
                apiUrl, apiKey, modelName, temp, tokens, timeoutSec);

            return Json(new { success, message });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "测试异常: " + ex.Message });
        }
    }

    // ========== AI配置加载（提取为公共方法） ==========

    private async Task<(string ApiUrl, string ApiKey, string ModelName, double Temperature, int MaxTokens, int Timeout)> LoadAiConfigAsync()
    {
        var configs = await _db.SiteConfigs
            .Where(c => c.ConfigKey!.StartsWith("Ai"))
            .ToListAsync();
        var dict = configs.ToDictionary(c => c.ConfigKey ?? "", c => c.ConfigValue ?? "");

        var apiUrl = dict.GetValueOrDefault("AiApiUrl", "");
        var apiKey = dict.GetValueOrDefault("AiApiKey", "");
        var modelName = dict.GetValueOrDefault("AiModelName", "");
        double.TryParse(dict.GetValueOrDefault("AiTemperature", "0.7"), out var temperature);
        int.TryParse(dict.GetValueOrDefault("AiMaxTokens", "2048"), out var maxTokens);
        int.TryParse(dict.GetValueOrDefault("AiTimeout", "120"), out var timeout);

        return (apiUrl, apiKey, modelName, temperature, maxTokens, timeout);
    }

    // ========== AI班级分析 ==========

    /// <summary>
    /// 获取可进行班级分析的班级列表
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GetAiClassList(int examScheduleId)
    {
        try
        {
            var classIds = await _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId && sc.ClassInfoId != null)
                .Select(sc => sc.ClassInfoId)
                .Distinct()
                .ToListAsync();

            var classList = await _db.ClassInfos
                .Where(c => classIds.Contains(c.ClassInfoID))
                .Include(c => c.GradeLevel)
                .OrderBy(c => c.ClassName)
                .ToListAsync();

            // 非管理员只能看到自己年级/班级的班级
            var adminId = GetAdminId();
            if (adminId.HasValue)
            {
                var admin = await _db.Admins.FindAsync(adminId.Value);
                if (admin != null && !IsAdmin())
                {
                    var filteredList = classList.ToList();

                    // 先按年级过滤（admin.Grade 存的是 GradeLevel.CurrentGradeName 格式，如 "小学2024级"）
                    if (!string.IsNullOrEmpty(admin.Grade))
                    {
                        filteredList = filteredList
                            .Where(c => c.GradeLevel != null &&
                                c.GradeLevel.CurrentGradeName == admin.Grade)
                            .ToList();
                    }
                    // 如果是班主任，进一步按班级名称过滤
                    if (!string.IsNullOrEmpty(admin.ClassName) && admin.PrimaryRole == "班主任")
                    {
                        var classFiltered = filteredList.Where(c => c.ClassName == admin.ClassName).ToList();
                        if (classFiltered.Any())
                        {
                            filteredList = classFiltered;
                        }
                    }

                    // 只有当过滤后有结果时才应用过滤，否则保留全部班级（避免"暂无班级数据"）
                    if (filteredList.Any())
                    {
                        classList = filteredList;
                    }
                }
            }

            var classes = classList.Select(c => new
            {
                classInfoId = c.ClassInfoID,
                className = c.ClassName,
                gradeName = c.GradeLevel != null ? GetGradeDisplayName(c.GradeLevel.SchoolType, c.GradeLevel.EntryYear) : ""
            }).ToList();

            return Json(new { success = true, classes });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载班级列表失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 获取/生成班级AI分析报告
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetAiClassAnalysis(int examScheduleId, int classInfoId, bool forceRegenerate = false)
    {
        try
        {
            // 1. 检查缓存
            if (!forceRegenerate)
            {
                var cached = await _db.AiClassAnalysisResults
                    .Where(r => r.ExamScheduleId == examScheduleId && r.ClassInfoId == classInfoId)
                    .FirstOrDefaultAsync();
                if (cached != null && !string.IsNullOrEmpty(cached.AnalysisResult))
                {
                    var ci = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == classInfoId);
                    return Json(new { success = true, analysis = cached.AnalysisResult,
                        typeName = "班级分析 - " + (ci?.GradeLevel != null ? GetGradeDisplayName(ci.GradeLevel.SchoolType, ci.GradeLevel.EntryYear) : "") + " " + (ci?.ClassName ?? ""),
                        cached = true, updatedAt = cached.UpdatedAt.ToString("yyyy-MM-dd HH:mm") });
                }
            }

            // 2. 读取AI配置
            var (apiUrl, apiKey, modelName, temperature, maxTokens, timeout) = await LoadAiConfigAsync();
            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelName))
                return Json(new { success = false, message = "请先在「AI设置」中配置API信息" });

            // 3. 构建Prompt
            var prompt = await BuildClassAnalysisPromptAsync(examScheduleId, classInfoId);

            // 4. 调用AI
            var result = await _aiService.GenerateAnalysisAsync(
                prompt, apiUrl, apiKey, modelName, temperature, maxTokens, timeout);

            // 5. 缓存
            var existing = await _db.AiClassAnalysisResults
                .Where(r => r.ExamScheduleId == examScheduleId && r.ClassInfoId == classInfoId)
                .FirstOrDefaultAsync();
            var gradeLevelId = (await _db.ClassInfos.FindAsync(classInfoId))?.GradeLevelID ?? 0;

            if (existing != null)
            {
                existing.AnalysisResult = result;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _db.AiClassAnalysisResults.Add(new AiClassAnalysisResult
                {
                    ExamScheduleId = examScheduleId,
                    ClassInfoId = classInfoId,
                    GradeLevelId = gradeLevelId,
                    AnalysisResult = result,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();

            return Json(new { success = true, analysis = result, cached = false });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "班级分析失败: " + ex.Message });
        }
    }

    // ========== AI科目分析 ==========

    /// <summary>
    /// 获取可进行科目分析的科目列表
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GetAiSubjectList(int examScheduleId)
    {
        try
        {
            var subjects = await _db.ExamSubjects
                .Where(es => es.ExamScheduleId == examScheduleId)
                .Include(es => es.Subject)
                .OrderBy(es => es.Subject!.SortOrder)
                .Select(es => new { subjectId = es.SubjectId, subjectName = es.Subject!.Name ?? "" })
                .ToListAsync();

            // 科目去重（同一考试可能配置了同名科目）
            subjects = subjects.GroupBy(s => s.subjectName).Select(g => g.First()).ToList();

            return Json(new { success = true, subjects });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "加载科目列表失败: " + ex.Message });
        }
    }

    /// <summary>
    /// 获取/生成科目AI分析报告
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetAiSubjectAnalysis(int examScheduleId, int classInfoId, int subjectId, bool forceRegenerate = false)
    {
        try
        {
            // 1. 检查缓存
            if (!forceRegenerate)
            {
                var cached = await _db.AiSubjectAnalysisResults
                    .Where(r => r.ExamScheduleId == examScheduleId && r.ClassInfoId == classInfoId && r.SubjectId == subjectId)
                    .FirstOrDefaultAsync();
                if (cached != null && !string.IsNullOrEmpty(cached.AnalysisResult))
                {
                    var sub = await _db.Subjects.FindAsync(subjectId);
                    var ci = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == classInfoId);
                    return Json(new { success = true, analysis = cached.AnalysisResult,
                        typeName = "科目分析 - " + (sub?.Name ?? "") + " · " + (ci?.GradeLevel != null ? GetGradeDisplayName(ci.GradeLevel.SchoolType, ci.GradeLevel.EntryYear) : "") + " " + (ci?.ClassName ?? ""),
                        cached = true, updatedAt = cached.UpdatedAt.ToString("yyyy-MM-dd HH:mm") });
                }
            }

            // 2. 读取AI配置
            var (apiUrl, apiKey, modelName, temperature, maxTokens, timeout) = await LoadAiConfigAsync();
            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(modelName))
                return Json(new { success = false, message = "请先在「AI设置」中配置API信息" });

            // 3. 构建Prompt
            var prompt = await BuildSubjectAnalysisPromptAsync(examScheduleId, classInfoId, subjectId);

            // 4. 调用AI
            var result = await _aiService.GenerateAnalysisAsync(
                prompt, apiUrl, apiKey, modelName, temperature, maxTokens, timeout);

            // 5. 缓存
            var existing = await _db.AiSubjectAnalysisResults
                .Where(r => r.ExamScheduleId == examScheduleId && r.ClassInfoId == classInfoId && r.SubjectId == subjectId)
                .FirstOrDefaultAsync();
            var gradeLevelId = (await _db.ClassInfos.FindAsync(classInfoId))?.GradeLevelID ?? 0;

            if (existing != null)
            {
                existing.AnalysisResult = result;
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _db.AiSubjectAnalysisResults.Add(new AiSubjectAnalysisResult
                {
                    ExamScheduleId = examScheduleId,
                    ClassInfoId = classInfoId,
                    SubjectId = subjectId,
                    GradeLevelId = gradeLevelId,
                    AnalysisResult = result,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });
            }
            await _db.SaveChangesAsync();

            return Json(new { success = true, analysis = result, cached = false });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "科目分析失败: " + ex.Message });
        }
    }

    // ========== Prompt构建 ==========

    /// <summary>
    /// 构建班级分析Prompt
    /// </summary>
    private async Task<string> BuildClassAnalysisPromptAsync(int examScheduleId, int classInfoId)
    {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        var classInfo = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == classInfoId);
        if (exam == null || classInfo == null) return "数据不完整，无法生成分析";

        var allScores = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId && sc.ClassInfoId == classInfoId)
            .ToListAsync();
        var studentIds = allScores.Select(sc => sc.StudentId).Distinct().ToList();
        var students = await _db.Students.Where(s => studentIds.Contains(s.StudentID)).ToListAsync();
        var subjectIds = allScores.Select(sc => sc.SubjectId).Distinct().ToList();
        var examSubjects = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId && subjectIds.Contains(es.SubjectId))
            .Include(es => es.Subject)
            .OrderBy(es => es.Subject!.SortOrder)
            .ToListAsync();

        var sb = new StringBuilder();
        var roleName = (classInfo.GradeLevel?.SchoolType == "小学") ? "小学教务主任和学习分析师" : "中学教务主任和学业分析师";
        sb.AppendLine($"你是一位经验丰富的{roleName}。请根据以下班级成绩数据，生成一份详细的班级整体成绩分析报告。");
        sb.AppendLine();
        sb.AppendLine("## 班级基本信息");
        sb.AppendLine($"- 年级：{(classInfo.GradeLevel != null ? GetGradeDisplayName(classInfo.GradeLevel.SchoolType, classInfo.GradeLevel.EntryYear) : "")}");
        sb.AppendLine($"- 班级：{classInfo.ClassName}");
        sb.AppendLine($"- 考试：{exam.Name}");
        sb.AppendLine($"- 考试类型：{exam.ExamType}");
        sb.AppendLine($"- 考试日期：{exam.ExamDate:yyyy-MM-dd}");
        sb.AppendLine($"- 参考人数：{studentIds.Count}");
        sb.AppendLine();

        // 各科统计
        sb.AppendLine("## 各科目统计数据");
        sb.AppendLine("| 科目 | 满分 | 班级均分 | 最高分 | 最低分 | 及格率 | 优秀率 | 低分率 |");
        sb.AppendLine("|------|------|---------|-------|-------|-------|-------|-------|");

        foreach (var es in examSubjects)
        {
            var subScores = allScores.Where(sc => sc.SubjectId == es.SubjectId && !sc.IsAbsent).Select(sc => (double)sc.ScoreValue).ToList();
            if (subScores.Count == 0) continue;
            var avg = subScores.Average();
            var max = subScores.Max();
            var min = subScores.Min();
            var fullScore = es.FullScore ?? es.Subject?.FullScore ?? 100;
            var passLine = fullScore * 0.6;
            var excellentLine = fullScore * 0.9;
            var lowLine = fullScore * 0.4;
            var passRate = Math.Round((double)subScores.Count(s => s >= passLine) / subScores.Count * 100, 1);
            var excellentRate = Math.Round((double)subScores.Count(s => s >= excellentLine) / subScores.Count * 100, 1);
            var lowRate = Math.Round((double)subScores.Count(s => s < lowLine) / subScores.Count * 100, 1);
            sb.AppendLine($"| {es.Subject?.Name ?? ""} | {fullScore} | {avg:F1} | {max:F1} | {min:F1} | {passRate}% | {excellentRate}% | {lowRate}% |");
        }
        sb.AppendLine();

        // 分数段分布
        sb.AppendLine("## 分数段分布（总分）");
        var totalScores = allScores.Where(sc => !sc.IsAbsent).GroupBy(sc => sc.StudentId).Select(g => g.Sum(sc => (double)sc.ScoreValue)).OrderByDescending(t => t).ToList();
        var totalFull = examSubjects.Sum(es => (double)(es.FullScore ?? es.Subject?.FullScore ?? 100));

        sb.AppendLine($"| 分数段 | 人数 | 占比 |");
        sb.AppendLine($"|--------|------|------|");
        var segments = new[] { ("优秀(≥90%)", 0.9, 1.0), ("良好(80%-89%)", 0.8, 0.9), ("中等(70%-79%)", 0.7, 0.8), ("及格(60%-69%)", 0.6, 0.7), ("不及格(<60%)", 0.0, 0.6) };
        foreach (var (name, lo, hi) in segments)
        {
            var cnt = totalScores.Count(t => t >= totalFull * lo && (hi >= 1.0 || t < totalFull * hi));
            var pct = totalScores.Count > 0 ? Math.Round((double)cnt / totalScores.Count * 100, 1) : 0;
            sb.AppendLine($"| {name} | {cnt} | {pct}% |");
        }
        sb.AppendLine();

        // 尖子生
        sb.AppendLine("## 班级前5名学生");
        sb.AppendLine("| 排名 | 姓名 | 总分 |");
        sb.AppendLine("|------|------|------|");
        var top5 = totalScores.Take(5).ToList();
        foreach (var t in totalScores.Select((total, idx) => new { total, idx }).Take(5))
        {
            var sid = allScores.Where(sc => !sc.IsAbsent).GroupBy(sc => sc.StudentId).OrderByDescending(g => g.Sum(sc => (double)sc.ScoreValue)).ElementAt(t.idx).Key;
            var stu = students.FirstOrDefault(s => s.StudentID == sid);
            sb.AppendLine($"| {t.idx + 1} | {stu?.Name ?? ""} | {t.total:F1} |");
        }
        sb.AppendLine();

        // 后进生
        sb.AppendLine("## 班级后5名学生");
        sb.AppendLine("| 排名 | 姓名 | 总分 |");
        sb.AppendLine("|------|------|------|");
        var bottom5 = totalScores.TakeLast(5).Reverse().ToList();
        var rankedAsc = totalScores.OrderBy(t => t).ToList();
        foreach (var t in rankedAsc.Take(5).Select((total, idx) => new { total, idx }))
        {
            var sid = allScores.Where(sc => !sc.IsAbsent).GroupBy(sc => sc.StudentId).OrderBy(g => g.Sum(sc => (double)sc.ScoreValue)).ElementAt(t.idx).Key;
            var stu = students.FirstOrDefault(s => s.StudentID == sid);
            sb.AppendLine($"| {studentIds.Count - t.idx} | {stu?.Name ?? ""} | {t.total:F1} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 分析要求");
        sb.AppendLine("请从以下维度进行分析，输出中文、条理清晰、数据准确：");
        sb.AppendLine();
        sb.AppendLine("### 一、班级整体表现评价");
        sb.AppendLine("基于总分平均分、及格率、优秀率等指标，评价该班级本次考试的整体表现。");
        sb.AppendLine();
        sb.AppendLine("### 二、各科强弱分析");
        sb.AppendLine("哪些科目是优势科目（均分高或及格率高），哪些是薄弱科目（均分低或低分率高）。");
        sb.AppendLine();
        sb.AppendLine("### 三、尖子生分析");
        sb.AppendLine("前5名学生的成绩特点，有无明显偏科现象。");
        sb.AppendLine();
        sb.AppendLine("### 四、后进生分析");
        sb.AppendLine("后5名学生的薄弱科目分布，是否存在共性问题。");
        sb.AppendLine();
        sb.AppendLine("### 五、成绩分布分析");
        sb.AppendLine("分析分数段分布形态，是否存在两极分化现象。");
        sb.AppendLine();
        sb.AppendLine("### 六、班级教学建议");
        sb.AppendLine("针对薄弱科目和整体情况，给出具体可操作的教学改进建议。");
        sb.AppendLine();
        sb.AppendLine("### 注意事项");
        sb.AppendLine("- 所有分析必须基于上面提供的数据，不要编造数据");
        sb.AppendLine("- 语言要专业客观，给出建设性意见");

        return sb.ToString();
    }

    /// <summary>
    /// 构建科目分析Prompt
    /// </summary>
    private async Task<string> BuildSubjectAnalysisPromptAsync(int examScheduleId, int classInfoId, int subjectId)
    {
        var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
        var classInfo = await _db.ClassInfos.Include(c => c.GradeLevel).FirstOrDefaultAsync(c => c.ClassInfoID == classInfoId);
        var subject = await _db.Subjects.FindAsync(subjectId);
        var examSubject = await _db.ExamSubjects
            .Where(es => es.ExamScheduleId == examScheduleId && es.SubjectId == subjectId)
            .FirstOrDefaultAsync();

        if (exam == null || classInfo == null || subject == null) return "数据不完整，无法生成分析";

        var fullScore = examSubject?.FullScore ?? (int?)subject.FullScore ?? 100;
        var passLine = fullScore * 0.6;
        var excellentLine = fullScore * 0.9;
        var lowLine = fullScore * 0.4;

        // 班级该科目成绩
        var classScores = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId && sc.ClassInfoId == classInfoId && sc.SubjectId == subjectId)
            .Select(sc => (double)sc.ScoreValue)
            .ToListAsync();

        // 年级该科目成绩（同年级所有班级）
        var gradeScores = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId && sc.GradeLevelId == classInfo.GradeLevelID && sc.SubjectId == subjectId)
            .Select(sc => (double)sc.ScoreValue)
            .ToListAsync();

        var count = classScores.Count;
        if (count == 0) return "该班级没有该科目的成绩数据";

        var avg = classScores.Average();
        var max = classScores.Max();
        var min = classScores.Min();
        var sorted = classScores.OrderBy(s => s).ToList();
        var median = count % 2 == 0 ? (sorted[count / 2 - 1] + sorted[count / 2]) / 2 : sorted[count / 2];
        var variance = classScores.Select(s => Math.Pow(s - avg, 2)).Average();
        var stdDev = Math.Sqrt(variance);
        var gradeAvg = gradeScores.Count > 0 ? gradeScores.Average() : 0;

        var passCount = classScores.Count(s => s >= passLine);
        var excellentCount = classScores.Count(s => s >= excellentLine);
        var lowCount = classScores.Count(s => s < lowLine);

        var sb = new StringBuilder();
        var roleName = (classInfo.GradeLevel?.SchoolType == "小学") ? "小学教务主任和学习分析师" : "中学教务主任和学科教学分析师";
        sb.AppendLine($"你是一位经验丰富的{roleName}。请根据以下数据，生成一份详细的单科成绩分析报告。");
        sb.AppendLine();
        sb.AppendLine("## 基本信息");
        sb.AppendLine($"- 年级：{(classInfo.GradeLevel != null ? GetGradeDisplayName(classInfo.GradeLevel.SchoolType, classInfo.GradeLevel.EntryYear) : "")}");
        sb.AppendLine($"- 班级：{classInfo.ClassName}");
        sb.AppendLine($"- 科目：{subject.Name}");
        sb.AppendLine($"- 满分：{fullScore}");
        sb.AppendLine($"- 考试：{exam.Name}");
        sb.AppendLine($"- 考试类型：{exam.ExamType}");
        sb.AppendLine($"- 考试日期：{exam.ExamDate:yyyy-MM-dd}");
        sb.AppendLine($"- 参考人数：{count}");
        sb.AppendLine();

        sb.AppendLine("## 整体统计");
        sb.AppendLine($"- 班级平均分：{avg:F1}");
        sb.AppendLine($"- 年级平均分：{gradeAvg:F1}");
        sb.AppendLine($"- 班级最高分：{max:F1}");
        sb.AppendLine($"- 班级最低分：{min:F1}");
        sb.AppendLine($"- 中位数：{median:F1}");
        sb.AppendLine($"- 标准差：{stdDev:F1}");
        sb.AppendLine($"- 及格人数：{passCount}/{count}（及格率 {Math.Round((double)passCount/count*100, 1)}%）");
        sb.AppendLine($"- 优秀人数：{excellentCount}/{count}（优秀率 {Math.Round((double)excellentCount/count*100, 1)}%）");
        sb.AppendLine($"- 低分人数：{lowCount}/{count}（低分率 {Math.Round((double)lowCount/count*100, 1)}%）");
        sb.AppendLine();

        sb.AppendLine("## 分数段分布");
        sb.AppendLine("| 分数段 | 人数 | 占比 |");
        sb.AppendLine("|--------|------|------|");
        var segs = new[] { ($"优秀({fullScore*0.9:F0}-{fullScore:F0})", fullScore*0.9, fullScore+1),
                           ($"良好({fullScore*0.8:F0}-{fullScore*0.9:F0})", fullScore*0.8, fullScore*0.9),
                           ($"中等({fullScore*0.7:F0}-{fullScore*0.8:F0})", fullScore*0.7, fullScore*0.8),
                           ($"及格({fullScore*0.6:F0}-{fullScore*0.7:F0})", fullScore*0.6, fullScore*0.7),
                           ($"不及格(0-{fullScore*0.6:F0})", 0, fullScore*0.6) };
        foreach (var (name, lo, hi) in segs)
        {
            var cnt = classScores.Count(s => s >= lo && s < hi);
            var pct = Math.Round((double)cnt / count * 100, 1);
            sb.AppendLine($"| {name} | {cnt} | {pct}% |");
        }
        sb.AppendLine();

        // 学生成绩列表
        var studentIds = await _db.Scores
            .Where(sc => sc.ExamScheduleId == examScheduleId && sc.ClassInfoId == classInfoId && sc.SubjectId == subjectId)
            .Select(sc => sc.StudentId)
            .ToListAsync();
        var students = await _db.Students.Where(s => studentIds.Contains(s.StudentID)).ToListAsync();
        var scoreStudentPairs = classScores.Select((score, idx) => new { Score = score, StudentId = studentIds[idx] })
            .OrderByDescending(x => x.Score).ToList();

        sb.AppendLine("## 学生成绩列表（按分数从高到低）");
        sb.AppendLine("| 姓名 | 分数 | 评价 |");
        sb.AppendLine("|------|------|------|");
        foreach (var item in scoreStudentPairs)
        {
            var stu = students.FirstOrDefault(s => s.StudentID == item.StudentId);
            var level = item.Score >= excellentLine ? "优秀" : item.Score >= passLine ? "及格" : "待提高";
            sb.AppendLine($"| {stu?.Name ?? ""} | {item.Score:F1} | {level} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 分析要求");
        sb.AppendLine("请从以下维度进行分析，输出中文、条理清晰、数据准确：");
        sb.AppendLine();
        sb.AppendLine("### 一、总体评价");
        sb.AppendLine("基于平均分、及格率、优秀率等指标，评价该班级本次考试的整体表现。");
        sb.AppendLine();
        sb.AppendLine("### 二、分数分布分析");
        sb.AppendLine("分析分数段分布形态（正态/偏态/两极分化），指出主要分数聚集区间。");
        sb.AppendLine();
        sb.AppendLine("### 三、与年级对比");
        sb.AppendLine($"班级均分与年级均分的差距分析，判断该班{subject.Name}水平在年级中的位置。");
        sb.AppendLine();
        sb.AppendLine("### 四、学生掌握程度分析");
        sb.AppendLine("- 高分学生的共同特点");
        sb.AppendLine("- 低分学生的普遍问题");
        sb.AppendLine("- 中等分数段学生的提升空间");
        sb.AppendLine();
        sb.AppendLine("### 五、教学改进建议");
        sb.AppendLine($"针对本次考试反映出的问题，给出具体的{subject.Name}教学改进建议。");
        sb.AppendLine();
        sb.AppendLine("### 注意事项");
        sb.AppendLine("- 所有分析必须基于上面提供的数据，不要编造数据");
        sb.AppendLine("- 语言要专业客观，给出建设性意见");

        return sb.ToString();
    }

    // ======================== 成绩报告单 ========================

    [HttpGet]
    public async Task<IActionResult> ReportCard()
    {
        // 获取当前登录用户信息
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        string? teacherGrade = null;
        string? teacherClass = null;
        if (int.TryParse(adminIdStr, out int adminId))
        {
            var admin = await _db.Admins.FindAsync(adminId);
            if (admin != null && role.Trim() != "管理员")
            {
                teacherGrade = admin.Grade;
                teacherClass = admin.ClassName;
            }
        }

        // 非管理员只能看到自己年级的考试
        IQueryable<ExamSchedule> examQuery = _db.ExamSchedules;
        if (!string.IsNullOrEmpty(teacherGrade))
        {
            examQuery = examQuery.Where(e => e.Grades != null && e.Grades.Contains(teacherGrade));
        }
        var exams = await examQuery
            .OrderByDescending(e => e.ExamDate)
            .Select(e => new { e.Id, e.Name, e.ExamType, e.ExamDate, e.Status, e.Grades })
            .ToListAsync();

        ViewBag.ExamSchedules = exams;
        ViewBag.TeacherGrade = teacherGrade;
        ViewBag.TeacherClass = teacherClass;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> GetReportCardData(int examScheduleId, string? grade = null, string? className = null, string? studentName = null)
    {
        try
        {
            var exam = await _db.ExamSchedules.FindAsync(examScheduleId);
            if (exam == null)
                return Json(new { success = false, message = "考试安排不存在" });

            // 获取当前登录用户信息
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
            string? teacherGrade = null;
            string? teacherClass = null;
            if (int.TryParse(adminIdStr, out int adminId))
            {
                var admin = await _db.Admins.FindAsync(adminId);
                if (admin != null && role.Trim() != "管理员")
                {
                    teacherGrade = admin.Grade;
                    teacherClass = admin.ClassName;
                }
            }

            // 非管理员强制使用自己的年级和班级
            bool isRestricted = !string.IsNullOrEmpty(teacherClass);
            if (isRestricted)
            {
                grade = teacherGrade;
                className = teacherClass;
            }

            // 获取该考试的科目
            var subjects = await _db.ExamSubjects
                .Where(es => es.ExamScheduleId == examScheduleId)
                .Include(es => es.Subject)
                .OrderBy(es => es.Subject!.SortOrder)
                .ThenBy(es => es.Subject!.Name)
                .Select(es => new { es.SubjectId, SubjectName = es.Subject!.Name ?? "", FullScore = es.FullScore ?? es.Subject!.FullScore })
                .ToListAsync();

            subjects = subjects.GroupBy(s => s.SubjectName).Select(g => g.First()).ToList();

            if (subjects.Count == 0)
                return Json(new { success = false, message = "该考试尚未关联科目" });

            // 构建学生查询
            var gradeList = (exam.Grades ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            IQueryable<Student> studentQuery = _db.Students.Where(s => s.Status == "在读");
            if (gradeList.Count > 0)
                studentQuery = studentQuery.Where(s => gradeList.Contains(s.Grade ?? ""));

            if (!string.IsNullOrEmpty(grade))
                studentQuery = studentQuery.Where(s => s.Grade == grade);
            if (!string.IsNullOrEmpty(className))
                studentQuery = studentQuery.Where(s => s.ClassName == className);
            if (!string.IsNullOrEmpty(studentName))
                studentQuery = studentQuery.Where(s => s.Name!.Contains(studentName));

            var students = await studentQuery
                .OrderBy(s => s.Grade).ThenBy(s => s.ClassName).ThenBy(s => s.Name)
                .Select(s => new { s.StudentID, s.StudentNo, s.Name, s.Grade, s.ClassName })
                .ToListAsync();

            if (students.Count == 0)
                return Json(new { success = false, message = "未找到匹配的学生" });

            // 获取成绩
            var studentIds = students.Select(s => s.StudentID).ToList();
            var scores = await _db.Scores
                .Where(sc => sc.ExamScheduleId == examScheduleId && studentIds.Contains(sc.StudentId))
                .ToListAsync();

            // 构建报告卡数据
            var subjectFullScores = subjects.ToDictionary(s => s.SubjectId, s => s.FullScore);
            var subjectNames = subjects.ToDictionary(s => s.SubjectId, s => s.SubjectName);

            var reportCards = students.Select(student =>
            {
                var studentScores = scores.Where(sc => sc.StudentId == student.StudentID).ToList();
                var subjectData = subjects.Select(sub =>
                {
                    var sc = studentScores.FirstOrDefault(s => s.SubjectId == sub.SubjectId);
                    decimal? scoreVal = null;
                    bool absent = false;
                    string subLevel = "";
                    if (sc != null)
                    {
                        scoreVal = sc.ScoreValue;
                        absent = sc.IsAbsent;
                        // 计算各科等级
                        if (!absent && sub.FullScore > 0)
                        {
                            var pct = (double)scoreVal.Value / (double)sub.FullScore;
                            if (pct >= 0.9) subLevel = "A";
                            else if (pct >= 0.8) subLevel = "B";
                            else if (pct >= 0.6) subLevel = "C";
                            else subLevel = "D";
                        }
                    }
                    return new
                    {
                        SubjectName = sub.SubjectName,
                        ScoreValue = absent ? (decimal?)null : scoreVal,
                        FullScore = sub.FullScore,
                        IsAbsent = absent,
                        Level = subLevel
                    };
                }).ToList();

                // 计算总分和总满分
                decimal totalScore = 0;
                decimal totalFullScore = 0;
                int absentCount = 0;
                int totalSubjects = subjectData.Count;
                foreach (var sd in subjectData)
                {
                    if (sd.IsAbsent) { absentCount++; continue; }
                    if (sd.ScoreValue.HasValue)
                    {
                        totalScore += sd.ScoreValue.Value;
                        totalFullScore += sd.FullScore;
                    }
                }

                // 【总分百分比法】综合等级 = 总分 ÷ 总满分
                // 单科D只拉低均分，不会一票否决；总分≥60%最低C；多科不及格总分<60%才落D
                string overallLevel = "";
                if (absentCount == totalSubjects)
                {
                    overallLevel = "缺考";
                }
                else if (totalFullScore > 0)
                {
                    var pct = (double)totalScore / (double)totalFullScore;
                    if (pct >= 0.9) overallLevel = "A (优秀)";
                    else if (pct >= 0.8) overallLevel = "B (良好)";
                    else if (pct >= 0.6) overallLevel = "C (及格)";
                    else overallLevel = "D (不及格)";
                }

                return new
                {
                    StudentId = student.StudentID,
                    StudentNo = student.StudentNo,
                    Name = student.Name,
                    Grade = student.Grade,
                    ClassName = student.ClassName,
                    Subjects = subjectData,
                    TotalScore = totalScore,
                    TotalFullScore = totalFullScore,
                    OverallLevel = overallLevel,
                    HasAbsent = absentCount > 0
                };
            }).ToList();

            return Json(new { success = true, data = reportCards });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"加载数据失败: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> GetClassesByGrade(string grade)
    {
        try
        {
            if (string.IsNullOrEmpty(grade))
                return Json(new { success = false, classes = new List<string>() });

            var classes = await _db.Students
                .Where(s => s.Status == "在读" && s.Grade == grade && s.ClassName != null)
                .Select(s => s.ClassName!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return Json(new { success = true, classes });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, classes = new List<string>() });
        }
    }
}

public class ScoreItem
{
    public int StudentId { get; set; }
    public int SubjectId { get; set; }
    public decimal ScoreValue { get; set; }
    public bool IsAbsent { get; set; }
}

public class SaveImportRequest
{
    public int ExamScheduleId { get; set; }
    public List<ImportRow> Rows { get; set; } = new();
}

public class ImportRow
{
    public int StudentId { get; set; }
    public string StudentNo { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ImportScoreItem> Scores { get; set; } = new();
}

public class ImportScoreItem
{
    public int SubjectId { get; set; }
    public decimal ScoreValue { get; set; }
    public bool IsAbsent { get; set; }
}
