using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
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

        // 年级列表
        var grades = await _db.GradeLevels
            .OrderByDescending(g => g.EntryYear)
            .ThenBy(g => g.SchoolType)
            .ToListAsync();
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
            // 删除该考试+该年级的旧安排
            var oldRooms = await _db.ExamRooms
                .Include(r => r.Students)
                .Where(r => r.ExamScheduleId == examScheduleId && r.Grade == grade)
                .ToListAsync();
            _db.ExamRooms.RemoveRange(oldRooms);
            await _db.SaveChangesAsync();

            // 查找该年级所有在校学生
            var students = await _db.Students
                .Where(s => s.Grade == grade && s.Status != "已毕业" && s.Status != "已删除")
                .OrderBy(s => s.ClassID)
                .ThenBy(s => s.StudentID)
                .ToListAsync();

            if (students.Count == 0)
                return Json(new { success = false, message = "该年级没有在校学生" });

            // 按模式处理
            List<List<Student>> groups;

            if (mode == "Shuffle")
            {
                // 全年级打乱
                var shuffled = students.OrderBy(_ => Random.Shared.Next()).ToList();
                groups = new List<List<Student>>();
                for (int i = 0; i < shuffled.Count; i += studentsPerRoom)
                {
                    groups.Add(shuffled.Skip(i).Take(studentsPerRoom).ToList());
                }
            }
            else if (mode == "InClass")
            {
                // 按原班级分组
                groups = students
                    .GroupBy(s => s.ClassID ?? 0)
                    .OrderBy(g => g.Key)
                    .Select(g => g.OrderBy(s => s.StudentID).ToList())
                    .ToList();
            }
            else
            {
                return Json(new { success = false, message = "无效的安排模式" });
            }

            // 创建考场记录
            int roomIndex = 1;
            foreach (var group in groups)
            {
                string roomName = mode == "Shuffle"
                    ? $"第{roomIndex}考场"
                    : group.FirstOrDefault()?.ClassName ?? $"第{roomIndex}考场";

                var room = new ExamRoom
                {
                    ExamScheduleId = examScheduleId,
                    Grade = grade,
                    ArrangeMode = mode,
                    RoomName = roomName,
                    SeatCount = group.Count,
                    CreateTime = DateTime.Now
                };
                _db.ExamRooms.Add(room);
                await _db.SaveChangesAsync();

                // 分配学生座位
                int seatNum = 1;
                foreach (var student in group)
                {
                    _db.ExamRoomStudents.Add(new ExamRoomStudent
                    {
                        ExamRoomId = room.Id,
                        StudentId = student.StudentID,
                        SeatNumber = seatNum++
                    });
                }
                await _db.SaveChangesAsync();

                roomIndex++;
            }

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
