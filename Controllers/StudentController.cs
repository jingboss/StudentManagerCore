using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;
using StudentManagerCore.Services;
using System.Security.Claims;
using ClosedXML.Excel;

namespace StudentManagerCore.Controllers;

[Authorize]
public class StudentController : Controller
{
    private readonly AppDbContext _db;

    public StudentController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? keyword, string? status, string? gender, string? grade, string? className, string? isNonLocal, string? nation, string? householdType, int page = 1, string tab = "student", int[]? examIds = null)
    {
        // 后勤主任不能访问学生管理
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role == "后勤主任")
        {
            return RedirectToAction("Index", "Home");
        }

        int pageSize = 35;
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        int.TryParse(adminIdStr, out int currentAdminId);
        Admin? currentAdmin = null;
        if (currentAdminId > 0)
            currentAdmin = await _db.Admins.FindAsync(currentAdminId);

        var isAdmin = role == "管理员";
        var isBanZhuren = currentAdmin?.HasRole("班主任") ?? false;
        var teacherGrade = currentAdmin?.Grade ?? "";
        var teacherClassName = currentAdmin?.ClassName ?? "";

        // ========== 确定可用 Tab ==========
        var availableTabs = new List<string> { "student" };
        // Tab1「管理年级」：仅年级级长可以查看
        if (!string.IsNullOrWhiteSpace(teacherGrade) && !isAdmin && (currentAdmin?.HasRole("年级级长") ?? false))
            availableTabs.Add("grade");
        // Tab2「所教班级」：有科目任教记录 (科任教师 + 班主任兼科任)
        if (currentAdminId > 0 && !isAdmin && await _db.SubjectTeachers.AnyAsync(st => st.AdminId == currentAdminId))
            availableTabs.Add("teaching");

        ViewBag.AvailableTabs = availableTabs;
        ViewBag.ActiveTab = tab;
        ViewBag.CurrentRole = role.Trim();
        ViewBag.TeacherGrade = teacherGrade;

        // ========== 根据 Tab 获取数据 ==========
        // Tab "teaching" 走单独查询（所教班级成绩）
        if (tab == "teaching" && currentAdminId > 0)
        {
            var subjectIds = await _db.SubjectTeachers
                .Where(st => st.AdminId == currentAdminId)
                .Select(st => st.SubjectId)
                .ToListAsync();

            var classIds = await _db.SubjectTeachers
                .Where(st => st.AdminId == currentAdminId)
                .Select(st => st.ClassId)
                .Distinct()
                .ToListAsync();

            // 获取所教班级的学生
            var teachingStudents = await _db.Students
                .Where(s => s.ClassID != null && classIds.Contains(s.ClassID.Value) && s.Status != "已删除" && s.Status != "已毕业")
                .OrderBy(s => s.Grade).ThenBy(s => s.ClassName).ThenBy(s => s.Name)
                .ToListAsync();

            // 获取科目名称
            var subjects = await _db.Subjects.Where(s => subjectIds.Contains(s.Id)).ToListAsync();

            // 获取该老师所教年级的所有考试（供选择对比）
            var teacherGrades = teachingStudents.Select(s => s.Grade).Distinct().ToList();
            var allExams = (await _db.ExamSchedules
                .OrderByDescending(e => e.ExamDate)
                .ToListAsync())
                .Where(e => e.Grades != null && teacherGrades.Any(g => ("," + e.Grades + ",").Contains("," + g + ",")))
                .ToList();
            ViewBag.AllTeachingExams = allExams;

            ViewBag.SelectedExamIds = examIds?.ToList() ?? new List<int>();

            // 按学生分组
            var teachingViewData = teachingStudents.Select(stu => new
            {
                Student = stu,
                Scores = new List<Score>(),
                Subjects = subjects
            }).ToList();

            ViewBag.TeachingData = teachingViewData;
            ViewBag.TeachingSubjects = subjects;
            ViewBag.TeachingExams = allExams;
            ViewBag.CurrentPage = 1;
            ViewBag.TotalPages = 1;
            ViewBag.IsRestrictedView = false;

            // 只返回 tab 的局部视图
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView("_TeachingTab", teachingStudents);
            return View(teachingStudents);
        }

        // ========== 现有学生查询逻辑 ==========
        var query = _db.Students.AsQueryable();

        // 管理学生：排除已删除和已毕业
        if (string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(s => s.Status != "已删除" && s.Status != "已毕业");
        }
        else if (status == "已删除")
        {
            query = query.Where(s => s.Status == "已删除");
        }
        else if (status == "已毕业")
        {
            query = query.Where(s => s.Status == "已毕业");
        }

        // 性别筛选
        if (!string.IsNullOrWhiteSpace(gender) && gender != "全部")
        {
            query = query.Where(s => s.Gender == gender);
        }

        // 年级筛选
        if (!string.IsNullOrWhiteSpace(grade) && grade != "全部")
        {
            query = query.Where(s => s.Grade == grade);
        }

        // 班级筛选
        if (!string.IsNullOrWhiteSpace(className) && className != "全部")
        {
            query = query.Where(s => s.ClassName == className);
        }

        // 是否非本地户籍筛选
        if (!string.IsNullOrWhiteSpace(isNonLocal) && isNonLocal != "全部")
        {
            query = query.Where(s => s.IsNonLocalHousehold == isNonLocal);
        }

        // 民族筛选
        if (!string.IsNullOrWhiteSpace(nation) && nation != "全部")
        {
            query = query.Where(s => s.Nation != null && s.Nation.Contains(nation));
        }

        // 户口性质筛选
        if (!string.IsNullOrWhiteSpace(householdType) && householdType != "全部")
        {
            query = query.Where(s => s.HouseholdType == householdType);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(s =>
                (s.Name != null && s.Name.Contains(keyword)) ||
                (s.StudentNo != null && s.StudentNo.Contains(keyword)) ||
                (s.ClassName != null && s.ClassName.Contains(keyword)) ||
                (s.Grade != null && s.Grade.Contains(keyword)) ||
                (s.Nation != null && s.Nation.Contains(keyword)) ||
                (s.FatherPhone != null && s.FatherPhone.Contains(keyword)) ||
                (s.MotherPhone != null && s.MotherPhone.Contains(keyword)));
        }

        // 班主任只能看到本班学生（默认学生管理 tab 适用）
        if (tab == "student" && isBanZhuren)
        {
            if (currentAdmin?.ClassID != null)
            {
                var classInfo = await _db.ClassInfos
                    .Include(c => c.GradeLevel)
                    .FirstOrDefaultAsync(c => c.ClassInfoID == currentAdmin.ClassID);
                if (classInfo?.GradeLevel != null)
                {
                    var gradeName = classInfo.GradeLevel.CurrentGradeName ?? "";
                    var clsName = classInfo.ClassName ?? "";
                    query = query.Where(s => s.Grade == gradeName && s.ClassName == clsName);
                }
                else
                {
                    query = query.Where(s => false);
                }
            }
            else if (!string.IsNullOrWhiteSpace(teacherGrade) && !string.IsNullOrWhiteSpace(teacherClassName))
            {
                // ClassID 为空时，降级使用 Grade+ClassName 字符串过滤
                query = query.Where(s => s.Grade == teacherGrade && s.ClassName == teacherClassName);
            }
            else
            {
                // 未分配班级，看不到任何学生
                query = query.Where(s => false);
            }
        }
        // 管理年级 tab：按年级过滤
        else if (tab == "grade" && !string.IsNullOrWhiteSpace(teacherGrade))
        {
            query = query.Where(s => s.Grade == teacherGrade);
        }

        // 非班主任/非管理员角色受限查看（仅显示基本信息）
        var isRestricted = !isAdmin && !isBanZhuren;
        ViewBag.IsRestrictedView = isRestricted;
        ViewBag.CurrentRole = role.Trim();
        ViewBag.IsTeacher = isBanZhuren;

        // 加载权限设置
        ViewBag.Permissions = currentAdmin?.Permissions ?? "";

        var total = await query.CountAsync();
        var students = await query
            .OrderBy(s => s.StudentNo)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewBag.Keyword = keyword;
        ViewBag.Status = status;
        ViewBag.Gender = gender;
        ViewBag.Grade = grade;
        ViewBag.ClassName = className;
        ViewBag.IsNonLocal = isNonLocal;
        ViewBag.Nation = nation;
        ViewBag.HouseholdType = householdType;
        // 供下拉框使用的可选列表 — 从年级管理/班级管理同步数据
        var gradeLevels = await _db.GradeLevels
            .OrderByDescending(g => g.EntryYear)
            .ThenBy(g => g.SchoolType)
            .ToListAsync();
        ViewBag.GradeList = gradeLevels
            .Select(g => new { Value = g.CurrentGradeName, Text = g.CurrentGradeName })
            .ToList();
        ViewBag.GradeDisplayMap = gradeLevels
            .ToDictionary(g => g.CurrentGradeName, g => g.CurrentGradeName);
        // 班级下拉 — 不选年级则不显示任何班级（必须先选年级）
        if (string.IsNullOrWhiteSpace(grade))
        {
            ViewBag.ClassList = new List<dynamic>();
        }
        else
        {
            // CurrentGradeName 是 [NotMapped] 计算属性，不能用于 EF Core 查询
            // 先从内存数据中查出匹配的 GradeLevelID
            var matchedGrade = gradeLevels.FirstOrDefault(g =>
                string.Equals(g.CurrentGradeName, grade, StringComparison.OrdinalIgnoreCase));
            if (matchedGrade != null)
            {
                ViewBag.ClassList = await _db.ClassInfos
                    .Where(c => c.GradeLevelID == matchedGrade.GradeLevelID)
                    .OrderBy(c => c.ClassName)
                    .Select(c => new { GradeDisplay = grade, ClassName = c.ClassName })
                    .ToListAsync();
            }
            else
            {
                ViewBag.ClassList = new List<dynamic>();
            }
        }
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling((double)total / pageSize);
        ViewBag.Total = total;
        ViewBag.IsTeacher = currentAdmin?.HasRole("班主任") ?? false;
        ViewBag.CanGraduate = isAdmin || (isBanZhuren && (teacherGrade == "六年级" || teacherGrade == "九年级"));

        return View(students);
    }

    public async Task<IActionResult> Add()
    {
        try
        {
            ViewBag.Grades = await _db.GradeLevels
                .OrderByDescending(g => g.EntryYear)
                .ThenBy(g => g.SchoolType)
                .ToListAsync();
            var allClasses = await _db.ClassInfos
                .Include(c => c.GradeLevel)
                .ToListAsync();
            ViewBag.AllClasses = allClasses
                .Where(c => c.GradeLevel != null)
                .Select(c => new { c.GradeLevelID, c.ClassName, GradeDisplayName = c.GradeLevel!.CurrentGradeName })
                .ToList();

            // 班主任只能选择自己的年级和班级
            var (gradeName, className) = await GetTeacherGradeClassNameAsync();
            if (gradeName != null && className != null)
            {
                var grades = ViewBag.Grades as IEnumerable<GradeLevel>;
                if (grades != null)
                    ViewBag.Grades = grades.Where(g => g.CurrentGradeName == gradeName).ToList();

                var allClassesFilter = ViewBag.AllClasses as IEnumerable<object>;
                if (allClassesFilter != null)
                    ViewBag.AllClasses = allClassesFilter
                        .Cast<dynamic>()
                        .Where(c => (string)c.GradeDisplayName == gradeName && (string)c.ClassName == className)
                        .ToList();
            }
        }
        catch
        {
            ViewBag.Grades = new List<GradeLevel>();
            ViewBag.AllClasses = new List<object>();
        }

        return View(new Student());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Student student)
    {
        // 身份证末位x自动转为大写X
        if (!string.IsNullOrWhiteSpace(student.IDCardNumber))
            student.IDCardNumber = student.IDCardNumber.ToUpperInvariant();

        // 学号为空时自动生成（按年级班级）
        if (string.IsNullOrWhiteSpace(student.StudentNo))
        {
            try { student.StudentNo = await GenerateStudentNoAsync(student.Grade, student.ClassName); } catch { }
        }

        if (string.IsNullOrWhiteSpace(student.Name))
        {
            ModelState.AddModelError("Name", "学生姓名不能为空");
        }

        if (ModelState.IsValid)
        {
            // 班主任只能添加本班学生
            var addAdminIdStr = User.FindFirst("AdminID")?.Value ?? "";
            if (int.TryParse(addAdminIdStr, out int addAdminId))
            {
                var addUser = await _db.Admins.FindAsync(addAdminId);
                if (addUser?.HasRole("班主任") == true)
                {
                    var (addGrade, addClass) = await GetTeacherGradeClassNameAsync();
                    if (addGrade != null && addClass != null)
                    {
                        if (student.Grade != addGrade || student.ClassName != addClass)
                        {
                            ModelState.AddModelError("", "班主任只能添加本班学生");
                            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                                return Json(new { success = false, message = "班主任只能添加本班学生" });
                            return View(student);
                        }
                    }
                    else
                    {
                        // 班主任未分配班级
                        ModelState.AddModelError("", "未分配班级，无法添加学生");
                        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                            return Json(new { success = false, message = "未分配班级，无法添加学生" });
                        return View(student);
                    }
                }
            }

            student.CreateTime = DateTime.Now;
            student.UpdateTime = DateTime.Now;
            if (string.IsNullOrWhiteSpace(student.Status))
                student.Status = "在读";

            // 检查学号和身份证唯一性
            if (!string.IsNullOrWhiteSpace(student.StudentNo) &&
                await _db.Students.AnyAsync(s => s.StudentNo == student.StudentNo))
            {
                ModelState.AddModelError("StudentNo", $"学号「{student.StudentNo}」已存在");
            }
            if (!string.IsNullOrWhiteSpace(student.IDCardNumber) &&
                await _db.Students.AnyAsync(s => s.IDCardNumber == student.IDCardNumber))
            {
                ModelState.AddModelError("IDCardNumber", $"身份证号「{student.IDCardNumber}」已存在");
            }
            if (!ModelState.IsValid)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { success = false, message = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)) });
                return View(student);
            }

            _db.Students.Add(student);
            await _db.SaveChangesAsync();
            await LogOperation("添加", student);
            TempData["Success"] = "添加学生成功！";
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { success = true });
            return RedirectToAction("Index");
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var errors = string.Join("; ", ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage));
            return Json(new { success = false, message = errors });
        }
        return View(student);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var student = await _db.Students.FindAsync(id);
        if (student == null)
            return NotFound();

        try
        {
            ViewBag.Grades = await _db.GradeLevels
                .OrderByDescending(g => g.EntryYear)
                .ThenBy(g => g.SchoolType)
                .ToListAsync();
            var allClasses = await _db.ClassInfos
                .Include(c => c.GradeLevel)
                .ToListAsync();
            ViewBag.AllClasses = allClasses
                .Where(c => c.GradeLevel != null)
                .Select(c => new { c.GradeLevelID, c.ClassName, GradeDisplayName = c.GradeLevel!.CurrentGradeName })
                .ToList();

            // 计算学生年级对应的 GradeLevelID（用于编辑时预选年级）
            int currentYear = DateTime.Now.Year;
            if (student.Grade != null)
            {
                int entryYear = GradeHelper.GradeToEntryYear(student.Grade, currentYear);
                string schoolType = GradeHelper.GetSchoolType(student.Grade);
                var matchingGrade = await _db.GradeLevels
                    .FirstOrDefaultAsync(g => g.EntryYear == entryYear && g.SchoolType == schoolType);
                ViewBag.StudentGradeLevelId = matchingGrade?.GradeLevelID;
            }
            else
            {
                ViewBag.StudentGradeLevelId = null;
            }

            // 班主任只能选择自己的年级和班级
            var (gradeName, className) = await GetTeacherGradeClassNameAsync();
            if (gradeName != null && className != null)
            {
                var grades = ViewBag.Grades as IEnumerable<GradeLevel>;
                if (grades != null)
                    ViewBag.Grades = grades.Where(g => g.CurrentGradeName == gradeName).ToList();

                var allClassesList = ViewBag.AllClasses as IEnumerable<object>;
                if (allClassesList != null)
                    ViewBag.AllClasses = allClassesList
                        .Cast<dynamic>()
                        .Where(c => (string)c.GradeDisplayName == gradeName && (string)c.ClassName == className)
                        .ToList();
            }
        }
        catch
        {
            // 班级管理相关表尚未创建，不影响学生编辑
            ViewBag.Grades = new List<GradeLevel>();
            ViewBag.AllClasses = new List<object>();
        }
        return View(student);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Student student)
    {
        if (id != student.StudentID)
        {
            if (IsAjaxRequest()) return Json(new { success = false, message = "参数错误" });
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(student.Name))
        {
            ModelState.AddModelError("Name", "学生姓名不能为空");
        }

        if (ModelState.IsValid)
        {
            var existing = await _db.Students.FindAsync(id);
            if (existing == null)
            {
                if (IsAjaxRequest()) return Json(new { success = false, message = "学生不存在" });
                return NotFound();
            }

            // 非管理员不能修改学号
            var editRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (editRole == "管理员")
                existing.StudentNo = student.StudentNo;

            // 身份证末位x自动转为大写X
            if (!string.IsNullOrWhiteSpace(student.IDCardNumber))
                student.IDCardNumber = student.IDCardNumber.ToUpperInvariant();
            existing.Grade = student.Grade;
            existing.ClassName = student.ClassName;
            existing.Name = student.Name;
            existing.Gender = student.Gender;
            existing.IDCardNumber = student.IDCardNumber;
            existing.Nation = student.Nation;
            existing.HouseholdCity = student.HouseholdCity;
            existing.HouseholdAddress = student.HouseholdAddress;
            existing.HouseholdType = student.HouseholdType;
            existing.IsNonLocalHousehold = student.IsNonLocalHousehold;
            existing.IsMigrantChild = student.IsMigrantChild;
            existing.IsMigrantWorkerChild = student.IsMigrantWorkerChild;
            existing.CurrentResidence = student.CurrentResidence;
            existing.FatherName = student.FatherName;
            existing.FatherPhone = student.FatherPhone;
            existing.MotherName = student.MotherName;
            existing.MotherPhone = student.MotherPhone;
            existing.ClassID = student.ClassID;
            existing.Status = student.Status;
            existing.Remark = student.Remark;
            existing.UpdateTime = DateTime.Now;

            // 检查学号和身份证唯一性（排除自身）
            if (editRole == "管理员" && !string.IsNullOrWhiteSpace(student.StudentNo) &&
                await _db.Students.AnyAsync(s => s.StudentNo == student.StudentNo && s.StudentID != id))
            {
                ModelState.AddModelError("StudentNo", $"学号「{student.StudentNo}」已被其他学生使用");
            }
            if (!string.IsNullOrWhiteSpace(student.IDCardNumber) &&
                await _db.Students.AnyAsync(s => s.IDCardNumber == student.IDCardNumber && s.StudentID != id))
            {
                ModelState.AddModelError("IDCardNumber", $"身份证号「{student.IDCardNumber}」已被其他学生使用");
            }
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest())
                    return Json(new { success = false, message = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)) });
                return View(student);
            }

            await _db.SaveChangesAsync();

            await LogOperation("编辑", existing, $"编辑学生信息");

            if (IsAjaxRequest())
                return Json(new { success = true, message = "保存成功" });

            TempData["Success"] = "修改学生信息成功！";
            return RedirectToAction("Index");
        }

        if (IsAjaxRequest())
            return Json(new { success = false, message = "表单验证失败，请检查输入" });

        return View(student);
    }

    [HttpGet]
    public async Task<IActionResult> GetClassesByGrade(int gradeLevelId)
    {
        var classes = await _db.ClassInfos
            .Where(c => c.GradeLevelID == gradeLevelId)
            .OrderBy(c => c.ClassName)
            .Select(c => new { c.ClassInfoID, c.ClassName })
            .ToListAsync();

        return Json(classes);
    }

    [HttpGet]
    public async Task<IActionResult> GetClassesByGradeName(string gradeName)
    {
        var gradeLevels = await _db.GradeLevels.ToListAsync();
        var matchedGrade = gradeLevels.FirstOrDefault(g =>
            string.Equals(g.CurrentGradeName, gradeName, StringComparison.OrdinalIgnoreCase));
        if (matchedGrade == null) return Json(new List<object>());
        var classes = await _db.ClassInfos
            .Where(c => c.GradeLevelID == matchedGrade.GradeLevelID)
            .OrderBy(c => c.ClassName)
            .Select(c => new { c.ClassInfoID, c.ClassName })
            .ToListAsync();

        return Json(classes);
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest";
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, DateTime? transferOutTime)
    {
        var student = await _db.Students.FindAsync(id);
        if (student == null)
            return Json(new { success = false, message = "学生不存在" });

        // 转出：标记为"已删除"，记录转出时间
        student.Status = "已删除";
        student.TransferOutTime = transferOutTime ?? DateTime.Now;
        student.UpdateTime = DateTime.Now;
        await _db.SaveChangesAsync();
        await LogOperation("转出", student);
        return Json(new { success = true, message = "已转出" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id)
    {
        var student = await _db.Students.FindAsync(id);
        if (student == null)
            return Json(new { success = false, message = "学生不存在" });

        // 恢复到"在读"
        student.Status = "在读";
        student.UpdateTime = DateTime.Now;
        await _db.SaveChangesAsync();
        await LogOperation("恢复", student);
        return Json(new { success = true, message = "恢复成功" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HardDelete(int id, string securityCode)
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        if (role != "管理员")
            return Json(new { success = false, message = "仅管理员可彻底删除" });

        var sc = await _db.SiteConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "SecurityCode");
        var requiredCode = sc?.ConfigValue ?? PasswordHelper.Hash("320324");
        bool codeValid;
        if (PasswordHelper.IsHashed(requiredCode))
            codeValid = PasswordHelper.Verify(securityCode, requiredCode);
        else
            codeValid = (securityCode == requiredCode); // 兼容旧明文
        if (!codeValid)
            return Json(new { success = false, message = "安全码错误，彻底删除失败" });

        var student = await _db.Students.FindAsync(id);
        if (student == null)
            return Json(new { success = false, message = "学生不存在" });

        _db.Students.Remove(student);
        await _db.SaveChangesAsync();
        await LogOperation("彻底删除", student);
        return Json(new { success = true, message = "已彻底删除" });
    }

    private async Task<(string? gradeName, string? className)> GetTeacherGradeClassNameAsync()
    {
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        if (!int.TryParse(adminIdStr, out int adminId))
            return (null, null);
        var teacher = await _db.Admins.FindAsync(adminId);
        if (teacher == null || !teacher.HasRole("班主任"))
            return (null, null);
        if (teacher.ClassID != null)
        {
            var classInfo = await _db.ClassInfos
                .Include(c => c.GradeLevel)
                .FirstOrDefaultAsync(c => c.ClassInfoID == teacher.ClassID);
            if (classInfo?.GradeLevel != null)
                return (classInfo.GradeLevel.CurrentGradeName, classInfo.ClassName);
        }
        // ClassID 为空时，降级使用 Grade+ClassName 字符串字段
        if (!string.IsNullOrWhiteSpace(teacher.Grade) && !string.IsNullOrWhiteSpace(teacher.ClassName))
            return (teacher.Grade, teacher.ClassName);
        return (null, null);
    }

    public async Task<IActionResult> Details(int id)
    {
        var student = await _db.Students.FindAsync(id);
        if (student == null)
            return NotFound();
        var gradeLevels = await _db.GradeLevels.ToListAsync();
        ViewBag.GradeDisplayMap = gradeLevels
            .ToDictionary(g => g.CurrentGradeName, g => g.CurrentGradeName);

        // 判断当前用户角色
        var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        int.TryParse(adminIdStr, out int curAdminId);
        var curUser = curAdminId > 0 ? await _db.Admins.FindAsync(curAdminId) : null;
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var isAdmin = role == "管理员";
        var isBanZhuren = curUser?.HasRole("班主任") ?? false;
        ViewBag.IsRestrictedView = !isAdmin && !isBanZhuren;

        // 获取该学生最近5次考试的成绩
        try
        {
            var examIds = await (
                from sc in _db.Scores
                join e in _db.ExamSchedules on sc.ExamScheduleId equals e.Id
                where sc.StudentId == id
                orderby e.ExamDate descending
                select sc.ExamScheduleId
            ).Distinct().Take(5).ToListAsync();

            var recentExams = new List<object>();
            if (examIds.Count > 0)
            {
                var exams = await _db.ExamSchedules
                    .Where(e => examIds.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id);

                var studentScores = await _db.Scores
                    .Where(sc => examIds.Contains(sc.ExamScheduleId) && sc.StudentId == id)
                    .Include(sc => sc.Subject)
                    .ToListAsync();

                var allTotalScores = await _db.Scores
                    .Where(sc => examIds.Contains(sc.ExamScheduleId))
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

                foreach (var examId in examIds)
                {
                    if (!exams.TryGetValue(examId, out var exam)) continue;
                    var scores = studentScores.Where(sc => sc.ExamScheduleId == examId).ToList();
                    if (scores.Count == 0) continue;

                    var totalScore = scores.Sum(sc => sc.ScoreValue);
                    var avgScore = Math.Round((double)totalScore / scores.Count, 1);
                    var examTotalScores = allTotalScores.Where(t => t.ExamScheduleId == examId).ToList();
                    var studentClassInfoId = scores.FirstOrDefault()?.ClassInfoId;

                    int? classRank = null, classTotal = null;
                    if (studentClassInfoId.HasValue)
                    {
                        var classScores = examTotalScores
                            .Where(s => s.ClassInfoId == studentClassInfoId.Value)
                            .OrderByDescending(s => s.Total).ToList();
                        classTotal = classScores.Count;
                        var pos = classScores.FindIndex(s => s.StudentId == id);
                        classRank = pos >= 0 ? pos + 1 : null;
                    }

                    // 年级排名
                    var studentGradeLevelId = scores.FirstOrDefault()?.GradeLevelId;
                    int? gradeRank = null, gradeTotal = null;
                    if (studentGradeLevelId.HasValue)
                    {
                        var gradeScores = examTotalScores
                            .Where(s => s.GradeLevelId == studentGradeLevelId.Value)
                            .OrderByDescending(s => s.Total).ToList();
                        gradeTotal = gradeScores.Count;
                        var pos = gradeScores.FindIndex(s => s.StudentId == id);
                        gradeRank = pos >= 0 ? pos + 1 : null;
                    }

                    recentExams.Add(new
                    {
                        ExamId = exam.Id,
                        exam.Name,
                        exam.ExamType,
                        ExamDate = exam.ExamDate.ToString("yyyy-MM-dd"),
                        totalScore,
                        avgScore,
                        classRank,
                        classTotal,
                        gradeRank,
                        gradeTotal,
                        Subjects = scores.OrderBy(sc => sc.Subject?.SortOrder).Select(sc => new
                        {
                            SubjectName = sc.Subject?.Name ?? "",
                            ScoreValue = sc.ScoreValue
                        })
                    });
                }
            }
            ViewBag.RecentExams = recentExams;
        }
        catch
        {
            ViewBag.RecentExams = new List<object>();
        }

        return View(student);
    }

    /// <summary>
    /// 生成8位学号：取同年级/班级最大学号的同前缀，后2位顺序递增（不补空缺）
    /// </summary>
    private async Task<string> GenerateStudentNoAsync(string? grade = null, string? className = null, HashSet<string>? pendingNos = null)
    {
        var allPending = pendingNos ?? new HashSet<string>();

        // 按年级班级查询最大学号
        var query = _db.Students.Where(s => s.StudentNo != null && s.StudentNo.Length == 8);
        if (!string.IsNullOrEmpty(grade))
            query = query.Where(s => s.Grade == grade);
        if (!string.IsNullOrEmpty(className))
            query = query.Where(s => s.ClassName == className);

        var maxNo = await query
            .OrderByDescending(s => s.StudentNo)
            .Select(s => s.StudentNo!)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(maxNo) && maxNo.Length == 8)
        {
            var prefix = maxNo.Substring(0, 6);
            if (int.TryParse(maxNo.Substring(6, 2), out var maxSeq))
            {
                // 查询该前缀下所有学号
                var existingQuery = _db.Students
                    .Where(s => s.StudentNo != null && s.StudentNo.StartsWith(prefix));
                if (!string.IsNullOrEmpty(grade))
                    existingQuery = existingQuery.Where(s => s.Grade == grade);
                if (!string.IsNullOrEmpty(className))
                    existingQuery = existingQuery.Where(s => s.ClassName == className);

                var existing = await existingQuery
                    .Select(s => s.StudentNo!)
                    .ToListAsync();
                var usedSet = new HashSet<string>(existing);
                foreach (var n in allPending) usedSet.Add(n);

                // 从max+1向后找第一个未使用的
                for (int i = maxSeq + 1; i <= 99; i++)
                {
                    var no = prefix + i.ToString("D2");
                    if (!usedSet.Contains(no))
                        return no;
                }
            }
        }

        // 无数据或前缀已用完 → 新建前缀
        var newPrefix = DateTime.Now.ToString("yyyyMM");
        return newPrefix + "01";
    }

    [HttpGet]
    public async Task<JsonResult> GetNextStudentNo(string? grade, string? className)
    {
        var no = await GenerateStudentNoAsync(grade, className);
        return Json(new { success = true, studentNo = no });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file)
    {
        // ------- 1. 权限检查 -------
        var curAdminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        int.TryParse(curAdminIdStr, out int curAdminId);
        var curUser = curAdminId > 0 ? await _db.Admins.FindAsync(curAdminId) : null;
        var isBanZhuren = curUser?.HasRole("班主任") ?? false;
        var isAdmin = curUser?.HasRole("管理员") ?? false;
        if (isBanZhuren)
            return Json(new { success = false, message = "班主任无导入权限" });
        var perms = await GetCurrentUserPermissions();
        if (!isAdmin && !perms.Contains("student_add"))
            return Json(new { success = false, message = "无导入权限" });

        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "请选择文件" });

        if (file.Length > 20 * 1024 * 1024)
            return Json(new { success = false, message = "Excel文件不能超过20MB" });

        if (!file.FileName.EndsWith(".xlsx") && !file.FileName.EndsWith(".xls"))
            return Json(new { success = false, message = "仅支持 .xlsx / .xls 文件" });

        try
        {
            int successCount = 0, skipCount = 0;
            var errors = new List<string>();
            var existingNos = await _db.Students
                .Where(s => s.Status == null || s.Status == "在读")
                .Select(s => s.StudentNo!)
                .ToListAsync();
            var existingSet = new HashSet<string>(existingNos.Where(n => !string.IsNullOrEmpty(n))!);

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheet(1);
            var range = ws.RangeUsed();
            if (range == null)
                return Json(new { success = false, message = "Excel 文件中没有数据" });
            var rows = range.RowsUsed().Skip(1); // 跳过标题行

            int rowIdx = 1;
            var importedNos = new HashSet<string>(); // 跟踪本次导入已生成的学号，避免同批重复
            foreach (var row in rows)
            {
                rowIdx++;
                try
                {
                    // 按列号读取（从1开始）
                    var studentNo = row.Cell(1).GetString().Trim();
                    var grade = row.Cell(2).GetString().Trim();
                    var className = row.Cell(3).GetString().Trim();
                    var name = row.Cell(4).GetString().Trim();
                    var gender = row.Cell(5).GetString().Trim();
                    var nation = row.Cell(6).GetString().Trim();
                    var idCard = row.Cell(7).GetString().Trim().ToUpperInvariant();
                    var status = row.Cell(8).GetString().Trim();
                    var householdType = row.Cell(9).GetString().Trim();
                    var householdCity = row.Cell(10).GetString().Trim();
                    var householdAddress = row.Cell(11).GetString().Trim();
                    var isNonLocal = row.Cell(12).GetString().Trim();
                    var isMigrant = row.Cell(13).GetString().Trim();
                    var isMigrantWorker = row.Cell(14).GetString().Trim();
                    var currentResidence = row.Cell(15).GetString().Trim();
                    var fatherName = row.Cell(16).GetString().Trim();
                    var fatherPhone = row.Cell(17).GetString().Trim();
                    var motherName = row.Cell(18).GetString().Trim();
                    var motherPhone = row.Cell(19).GetString().Trim();
                    var remark = row.Cell(20).GetString().Trim();

                    if (string.IsNullOrWhiteSpace(name))
                        continue; // 跳过空行

                    // 学号为空时自动生成
                    if (string.IsNullOrWhiteSpace(studentNo))
                    {
                        studentNo = await GenerateStudentNoAsync(grade, className, importedNos);
                        if (string.IsNullOrWhiteSpace(studentNo))
                        {
                            errors.Add($"第 {rowIdx} 行学号生成失败，已跳过");
                            continue;
                        }
                    }
                    else if (studentNo.Length != 8)
                    {
                        errors.Add($"第 {rowIdx} 行学号「{studentNo}」不是8位，已跳过");
                        continue;
                    }
                    if (!string.IsNullOrWhiteSpace(idCard) && idCard.Length != 18)
                    {
                        errors.Add($"第 {rowIdx} 行身份证号不是18位，已跳过");
                        continue;
                    }

                    if (existingSet.Contains(studentNo) && !importedNos.Contains(studentNo))
                    {
                        skipCount++;
                        continue;
                    }
                    importedNos.Add(studentNo);

                    var student = new Student
                    {
                        StudentNo = studentNo,
                        Name = name,
                        Gender = gender,
                        Nation = nation,
                        IDCardNumber = idCard,
                        Grade = grade,
                        ClassName = className,
                        Status = string.IsNullOrWhiteSpace(status) ? "在读" : status,
                        HouseholdType = householdType,
                        HouseholdCity = householdCity,
                        HouseholdAddress = householdAddress,
                        IsNonLocalHousehold = isNonLocal,
                        IsMigrantChild = isMigrant,
                        IsMigrantWorkerChild = isMigrantWorker,
                        CurrentResidence = currentResidence,
                        FatherName = fatherName,
                        FatherPhone = fatherPhone,
                        MotherName = motherName,
                        MotherPhone = motherPhone,
                        Remark = remark,
                        CreateTime = DateTime.Now,
                        UpdateTime = DateTime.Now
                    };

                    _db.Students.Add(student);
                    successCount++;
                }
                catch (Exception ex)
                {
                    errors.Add($"第 {rowIdx} 行解析失败: {ex.Message}");
                }
            }

            if (successCount > 0)
            {
                await _db.SaveChangesAsync();
                await LogOperation("导入", null, $"批量导入学生 {successCount} 人，跳过 {skipCount} 人");
            }

            var msg = $"导入完成：成功 {successCount} 条";
            if (skipCount > 0) msg += $"，跳过（已存在）{skipCount} 条";
            if (errors.Count > 0) msg += $"，{errors.Count} 行有错误";
            return Json(new { success = true, message = msg, imported = successCount, skipped = skipCount, errors = string.Join("; ", errors) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "导入失败: " + ex.Message });
        }
    }

    public async Task<IActionResult> DownloadTemplate()
    {
        var adminIdStr2 = User.FindFirst("AdminID")?.Value ?? "";
        int.TryParse(adminIdStr2, out int curAdminId2);
        var curUser2 = curAdminId2 > 0 ? await _db.Admins.FindAsync(curAdminId2) : null;
        if (curUser2?.HasRole("班主任") == true)
            return Content("班主任无此权限");
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("学生导入模板");
        var headers = new[] {
            "学号", "年级", "班级", "姓名", "性别", "民族", "身份证号",
            "就读状态", "户口性质", "户口所在地", "户口簿地址",
            "是否非本地户籍", "是否随迁子女", "是否进城务工子女",
            "现居住地址", "父亲姓名", "父亲电话", "母亲姓名", "母亲电话", "备注"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        ws.Columns().Width = 20;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "学生导入模板.xlsx");
    }

    public async Task<IActionResult> Export(string? keyword, string? status, string? gender, string? grade, string? className, string? isNonLocal, string? nation, string? householdType)
    {
        var query = _db.Students.AsQueryable();

        // 状态筛选（与 Index 一致）
        if (string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status != "已删除" && s.Status != "已毕业");
        else if (status == "已删除")
            query = query.Where(s => s.Status == "已删除");
        else if (status == "已毕业")
            query = query.Where(s => s.Status == "已毕业");

        // 性别筛选
        if (!string.IsNullOrWhiteSpace(gender) && gender != "全部")
            query = query.Where(s => s.Gender == gender);

        // 年级筛选
        if (!string.IsNullOrWhiteSpace(grade) && grade != "全部")
            query = query.Where(s => s.Grade == grade);

        // 班级筛选
        if (!string.IsNullOrWhiteSpace(className) && className != "全部")
            query = query.Where(s => s.ClassName == className);

        // 是否非本地户籍筛选
        if (!string.IsNullOrWhiteSpace(isNonLocal) && isNonLocal != "全部")
            query = query.Where(s => s.IsNonLocalHousehold == isNonLocal);

        // 民族筛选
        if (!string.IsNullOrWhiteSpace(nation) && nation != "全部")
            query = query.Where(s => s.Nation != null && s.Nation.Contains(nation));

        // 户口性质筛选
        if (!string.IsNullOrWhiteSpace(householdType) && householdType != "全部")
            query = query.Where(s => s.HouseholdType == householdType);

        // 关键词搜索
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(s =>
                (s.Name != null && s.Name.Contains(keyword)) ||
                (s.StudentNo != null && s.StudentNo.Contains(keyword)) ||
                (s.ClassName != null && s.ClassName.Contains(keyword)) ||
                (s.Grade != null && s.Grade.Contains(keyword)));
        }

        // 班主任只能导出本班
        var exportAdminIdStr = User.FindFirst("AdminID")?.Value ?? "";
        if (int.TryParse(exportAdminIdStr, out int exportAdminId))
        {
            var teacher = await _db.Admins.FindAsync(exportAdminId);
            if (teacher?.HasRole("班主任") == true)
            {
                if (teacher.ClassID != null)
                {
                    var classInfo = await _db.ClassInfos
                        .Include(c => c.GradeLevel)
                        .FirstOrDefaultAsync(c => c.ClassInfoID == teacher.ClassID);
                    if (classInfo?.GradeLevel != null)
                    {
                        var gradeName = classInfo.GradeLevel.CurrentGradeName ?? "";
                        var clsName = classInfo.ClassName ?? "";
                        query = query.Where(s => s.Grade == gradeName && s.ClassName == clsName);
                    }
                    else { query = query.Where(s => false); }
                }
                else if (!string.IsNullOrWhiteSpace(teacher.Grade) && !string.IsNullOrWhiteSpace(teacher.ClassName))
                {
                    query = query.Where(s => s.Grade == teacher.Grade && s.ClassName == teacher.ClassName);
                }
                else
                {
                    // 班主任未分配班级，无导出权限
                    TempData["Error"] = "未分配班级，无法导出";
                    return RedirectToAction("Index");
                }
            }
            else if (teacher?.HasRole("年级级长") == true)
            {
                // 年级级长无导出权限
                TempData["Error"] = "无导出权限";
                return RedirectToAction("Index");
            }
        }

        var students = await query.OrderBy(s => s.StudentID).ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("学生数据");

        // 表头（与编辑页面字段一致）
        var headers = new[] { "学号", "姓名", "性别", "民族", "身份证号",
            "年级", "班级", "就读状态",
            "户口性质", "户口所在地（省市）", "户口簿中首页家庭地址",
            "是否非本地户籍", "是否随迁子女", "是否进城务工人员子女",
            "现居住家庭地址",
            "父亲姓名", "父亲电话", "母亲姓名", "母亲电话",
            "备注" };

        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        // 数据行
        int row = 2;
        foreach (var s in students)
        {
            ws.Cell(row, 1).Value = s.StudentNo ?? "";
            ws.Cell(row, 2).Value = s.Name ?? "";
            ws.Cell(row, 3).Value = s.Gender ?? "";
            ws.Cell(row, 4).Value = s.Nation ?? "";
            ws.Cell(row, 5).Value = s.IDCardNumber ?? "";
            ws.Cell(row, 6).Value = s.Grade ?? "";
            ws.Cell(row, 7).Value = s.ClassName ?? "";
            ws.Cell(row, 8).Value = s.Status ?? "";
            ws.Cell(row, 9).Value = s.HouseholdType ?? "";
            ws.Cell(row, 10).Value = s.HouseholdCity ?? "";
            ws.Cell(row, 11).Value = s.HouseholdAddress ?? "";
            ws.Cell(row, 12).Value = s.IsNonLocalHousehold ?? "";
            ws.Cell(row, 13).Value = s.IsMigrantChild ?? "";
            ws.Cell(row, 14).Value = s.IsMigrantWorkerChild ?? "";
            ws.Cell(row, 15).Value = s.CurrentResidence ?? "";
            ws.Cell(row, 16).Value = s.FatherName ?? "";
            ws.Cell(row, 17).Value = s.FatherPhone ?? "";
            ws.Cell(row, 18).Value = s.MotherName ?? "";
            ws.Cell(row, 19).Value = s.MotherPhone ?? "";
            ws.Cell(row, 20).Value = s.Remark ?? "";
            row++;
        }

        // 按列内容设置固定列宽，打开即清晰可读
        var colWidths = new double[] {
            12,   // 学号
            10,   // 姓名
            6,    // 性别
            8,    // 民族
            22,   // 身份证号
            10,   // 年级
            10,   // 班级
            10,   // 就读状态
            12,   // 户口性质
            18,   // 户口所在地（省市）
            30,   // 户口簿中首页家庭地址
            14,   // 是否非本地户籍
            14,   // 是否随迁子女
            16,   // 是否进城务工人员子女
            30,   // 现居住家庭地址
            10,   // 父亲姓名
            15,   // 父亲电话
            10,   // 母亲姓名
            15,   // 母亲电话
            20    // 备注
        };
        for (int col = 0; col < colWidths.Length; col++)
            ws.Column(col + 1).Width = colWidths[col];

        // 文件名包含筛选信息
        var fileName = $"学生数据_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ========== 批量操作 ==========

    /// <summary>批量转班</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchTransfer(List<int> ids, string targetGrade, string targetClass)
    {
        if (ids == null || ids.Count == 0)
            return Json(new { success = false, message = "请选择学生" });
        if (string.IsNullOrWhiteSpace(targetGrade) || string.IsNullOrWhiteSpace(targetClass))
            return Json(new { success = false, message = "请选择目标年级和班级" });

        var students = await _db.Students.Where(s => ids.Contains(s.StudentID)).ToListAsync();
        foreach (var s in students)
        {
            s.Grade = targetGrade;
            s.ClassName = targetClass;
            s.UpdateTime = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        await LogOperation("批量转班", null, $"将 {students.Count} 名学生转入 {targetGrade}{targetClass}");
        return Json(new { success = true, message = $"成功将 {students.Count} 名学生转入 {targetGrade}{targetClass}" });
    }

    /// <summary>批量毕业</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchGraduate(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return Json(new { success = false, message = "请选择学生" });

        // 权限检查：仅管理员或六年级/九年级班主任可操作毕业
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
        var isAdmin = role == "管理员";
        if (!isAdmin)
        {
            var adminIdStr = User.FindFirst("AdminID")?.Value ?? "";
            int.TryParse(adminIdStr, out int curAdminId);
            var curUser = curAdminId > 0 ? await _db.Admins.FindAsync(curAdminId) : null;
            var curGrade = curUser?.Grade ?? "";
            if (!(curUser?.HasRole("班主任") == true && (curGrade == "六年级" || curGrade == "九年级")))
                return Json(new { success = false, message = "仅六年级/九年级班主任可操作毕业" });
        }

        var students = await _db.Students.Where(s => ids.Contains(s.StudentID)).ToListAsync();
        foreach (var s in students)
        {
            s.Status = "已毕业";
            s.UpdateTime = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        await LogOperation("批量毕业", null, $"将 {students.Count} 名学生标记为已毕业");
        return Json(new { success = true, message = $"成功将 {students.Count} 名学生标记为已毕业" });
    }

    /// <summary>批量删除</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchDelete(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return Json(new { success = false, message = "请选择学生" });

        var students = await _db.Students.Where(s => ids.Contains(s.StudentID)).ToListAsync();
        foreach (var s in students)
        {
            s.Status = "已删除";
            s.UpdateTime = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        await LogOperation("批量删除", null, $"将 {students.Count} 名学生移入已删除");
        return Json(new { success = true, message = $"成功将 {students.Count} 名学生移入已删除" });
    }

    /// <summary>批量恢复</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchRestore(List<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return Json(new { success = false, message = "请选择学生" });

        var students = await _db.Students.Where(s => ids.Contains(s.StudentID)).ToListAsync();
        foreach (var s in students)
        {
            s.Status = "在读";
            s.UpdateTime = DateTime.Now;
        }
        await _db.SaveChangesAsync();

        await LogOperation("批量恢复", null, $"恢复 {students.Count} 名学生在读");
        return Json(new { success = true, message = $"成功恢复 {students.Count} 名学生在读" });
    }

    private async Task<List<string>> GetCurrentUserPermissions()
    {
        var phone = User.FindFirst(ClaimTypes.MobilePhone)?.Value
                    ?? User.FindFirst("Phone")?.Value
                    ?? User.Identity?.Name ?? "";
        var admin = await _db.Admins.FirstOrDefaultAsync(a => a.Phone != null && a.Phone.Trim() == phone.Trim());
        return (admin?.Permissions ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private async Task LogOperation(string actionType, Student? student, string? detail = null)
    {
        var operatorName = User.FindFirst("RealName")?.Value ?? User.Identity?.Name ?? "未知";
        var operatorRole = User.FindFirst(ClaimTypes.Role)?.Value?.Trim() ?? "未知";

        var log = new OperationLog
        {
            OperatorName = operatorName,
            OperatorRole = operatorRole,
            ActionType = actionType,
            TargetNo = student?.StudentNo,
            TargetName = student?.Name,
            Detail = detail,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            CreateTime = DateTime.Now
        };
        _db.OperationLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
