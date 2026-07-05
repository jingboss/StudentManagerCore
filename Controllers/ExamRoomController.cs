using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using System.Security.Claims;

namespace StudentManagerCore.Controllers;

[Authorize(Roles = "管理员")]
public class ExamRoomController : Controller
{
    private readonly AppDbContext _db;

    public ExamRoomController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>考场安排首页</summary>
    public async Task<IActionResult> Index(int examScheduleId)
    {
        var schedule = await _db.ExamSchedules
            .Include(s => s.Semester)
                .ThenInclude(sem => sem!.AcademicYear)
            .FirstOrDefaultAsync(s => s.Id == examScheduleId);
        if (schedule == null) return RedirectToAction("Index", "ExamSchedule");

        ViewBag.Schedule = schedule;

        // 该考试已有的考场安排
        var rooms = await _db.ExamRooms
            .Include(r => r.Students!)
                .ThenInclude(rs => rs.Student)
            .Where(r => r.ExamScheduleId == examScheduleId)
            .OrderBy(r => r.RoomName)
            .ToListAsync();

        // 年级列表（只显示该考试设定好的年级）
        var scheduleGrades = (schedule.Grades ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        var grades = await _db.GradeLevels
            .OrderByDescending(g => g.EntryYear)
            .ThenBy(g => g.SchoolType)
            .ToListAsync();

        if (scheduleGrades.Count > 0)
            grades = grades.Where(g => scheduleGrades.Contains(g.CurrentGradeName)).ToList();

        ViewBag.Grades = grades;

        // 已经有安排的年级
        var arrangedGrades = rooms.Select(r => r.Grade).Distinct().ToList();
        ViewBag.ArrangedGrades = arrangedGrades;

        return View(rooms);
    }

    /// <summary>生成考场安排</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(int examScheduleId, string grade, string mode, int studentsPerRoom = 30)
    {
        try
        {
            // 1) 删除该考试+该年级的旧安排
            var oldRooms = await _db.ExamRooms
                .Include(r => r.Students)
                .Where(r => r.ExamScheduleId == examScheduleId && r.Grade == grade)
                .ToListAsync();
            _db.ExamRooms.RemoveRange(oldRooms);

            // 2) 查找该年级所有在校学生
            var students = await _db.Students
                .Where(s => s.Grade == grade && s.Status != "已毕业" && s.Status != "已删除")
                .OrderBy(s => s.ClassID)
                .ThenBy(s => s.StudentID)
                .ToListAsync();

            if (students.Count == 0)
                return Json(new { success = false, message = "该年级没有在校学生" });

            // 3) 按模式分组
            List<List<Student>> groups;

            if (mode == "Shuffle")
            {
                // 全年级打乱，均匀分配考场人数
                var shuffled = students.OrderBy(_ => Random.Shared.Next()).ToList();

                // 计算最优考场数，使每个考场人数尽量平均
                int total = shuffled.Count;
                int roomCount = (int)Math.Ceiling((double)total / studentsPerRoom);
                int baseCount = total / roomCount;
                int remainder = total % roomCount;

                groups = new List<List<Student>>();
                int pos = 0;
                for (int i = 0; i < roomCount; i++)
                {
                    int count = baseCount + (i < remainder ? 1 : 0);
                    groups.Add(shuffled.Skip(pos).Take(count).ToList());
                    pos += count;
                }
            }
            else if (mode == "InClass")
            {
                // 按原班级分组，每个班级内再均匀分配考场
                var classGroups = students
                    .GroupBy(s => s.ClassName ?? "未分班")
                    .OrderBy(g => g.Key)
                    .Select(g => g.OrderBy(s => s.StudentID).ToList())
                    .ToList();

                groups = new List<List<Student>>();
                foreach (var classGroup in classGroups)
                {
                    int total = classGroup.Count;
                    int roomCount = (int)Math.Ceiling((double)total / studentsPerRoom);
                    int baseCount = total / roomCount;
                    int remainder = total % roomCount;
                    int pos = 0;
                    for (int i = 0; i < roomCount; i++)
                    {
                        int count = baseCount + (i < remainder ? 1 : 0);
                        groups.Add(classGroup.Skip(pos).Take(count).ToList());
                        pos += count;
                    }
                }
            }
            else
            {
                return Json(new { success = false, message = "无效的安排模式" });
            }

            // 4) 批量创建考场（一次 SaveChanges）
            var newRooms = new List<ExamRoom>();
            int roomIndex = 1;
            foreach (var group in groups)
            {
                string roomName = mode == "Shuffle"
                    ? $"第{roomIndex}考场"
                    : group.FirstOrDefault()?.ClassName ?? $"第{roomIndex}考场";

                newRooms.Add(new ExamRoom
                {
                    ExamScheduleId = examScheduleId,
                    Grade = grade,
                    ArrangeMode = mode,
                    RoomName = roomName,
                    SeatCount = group.Count,
                    CreateTime = DateTime.Now
                });
                roomIndex++;
            }
            _db.ExamRooms.AddRange(newRooms);
            await _db.SaveChangesAsync();   // 一次提交：删除旧考场 + 创建新考场

            // 5) 批量分配学生座位（一次 SaveChanges）
            var studentAssignments = new List<ExamRoomStudent>();
            for (int i = 0; i < groups.Count; i++)
            {
                var room = newRooms[i];
                int seatNum = 1;
                foreach (var student in groups[i])
                {
                    studentAssignments.Add(new ExamRoomStudent
                    {
                        ExamRoomId = room.Id,
                        StudentId = student.StudentID,
                        SeatNumber = seatNum++
                    });
                }
            }
            _db.ExamRoomStudents.AddRange(studentAssignments);
            await _db.SaveChangesAsync();   // 一次提交：所有学生座位

            await LogOperation("生成考场安排", examScheduleId, $"考试#{examScheduleId} 年级:{grade} 模式:{mode}");
            return Json(new { success = true, message = $"共生成 {groups.Count} 个考场，{students.Count} 名学生" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "生成失败: " + (ex.InnerException?.Message ?? ex.Message) });
        }
    }

    /// <summary>删除某场考试+某个年级的考场安排</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(int examScheduleId, string grade)
    {
        var rooms = await _db.ExamRooms
            .Include(r => r.Students)
            .Where(r => r.ExamScheduleId == examScheduleId && r.Grade == grade)
            .ToListAsync();
        _db.ExamRooms.RemoveRange(rooms);
        await _db.SaveChangesAsync();

        await LogOperation("清除考场安排", examScheduleId, $"考试#{examScheduleId} 年级:{grade}");
        return Json(new { success = true });
    }

    /// <summary>打印视图</summary>
    public async Task<IActionResult> Print(int examScheduleId, string grade)
    {
        var schedule = await _db.ExamSchedules.FindAsync(examScheduleId);
        if (schedule == null) return NotFound();

        var rooms = await _db.ExamRooms
            .Include(r => r.Students!)
                .ThenInclude(rs => rs.Student)
            .Where(r => r.ExamScheduleId == examScheduleId && r.Grade == grade)
            .OrderBy(r => r.RoomName)
            .ToListAsync();

        ViewBag.Schedule = schedule;
        ViewBag.Grade = grade;
        return View(rooms);
    }

    /// <summary>导出Word考场安排表</summary>
    public async Task<IActionResult> ExportWord(int examScheduleId, string grade)
    {
        try
        {
            var schedule = await _db.ExamSchedules.FindAsync(examScheduleId);
            if (schedule == null) return NotFound();

            var rooms = await _db.ExamRooms
                .Include(r => r.Students!)
                    .ThenInclude(rs => rs.Student)
                .Where(r => r.ExamScheduleId == examScheduleId && r.Grade == grade)
                .OrderBy(r => r.RoomName)
                .ToListAsync();

            if (rooms.Count == 0)
                return Content("<script>alert('没有考场数据可供导出');history.back();</script>", "text/html");

            var wordService = HttpContext.RequestServices.GetRequiredService<WordExportService>();
            var wordBytes = wordService.GenerateSeatingChart(rooms, schedule, grade);

            string fileName = $"考场安排表_{schedule.Name}_{grade}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";

            return File(wordBytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
        }
        catch (Exception ex)
        {
            return Content($"<div style='padding:40px;text-align:center;font-size:18px;color:#666;margin-top:100px;'>⚠️ 导出失败：{ex.Message}</div>", "text/html");
        }
    }

    private async Task LogOperation(string actionType, int targetId, string detail)
    {
        var operatorName = User.FindFirst("RealName")?.Value ?? User.Identity?.Name ?? "未知";
        _db.OperationLogs.Add(new OperationLog
        {
            OperatorName = operatorName,
            OperatorRole = "管理员",
            ActionType = actionType,
            TargetNo = targetId.ToString(),
            TargetName = detail,
            Detail = detail,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreateTime = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }
}
