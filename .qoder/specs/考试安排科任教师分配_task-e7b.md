# 考试安排管理 - 新增科任教师分配功能

## 背景
考试安排管理目前支持设置考试基本信息和关联科目，但缺少科任教师分配功能。管理员需要能在创建/编辑考试安排时，为每个考试科目指定哪些教师负责该科目的登分工作，且要与科目管理中的全局教师配置联动。

## 实现步骤

### Task 1: 新增 Model + DbContext 配置
- `Models.cs` 追加 `ExamSubjectTeacher` 实体 (ExamScheduleId, SubjectId, AdminId, ClassId + 外键导航)
- `AppDbContext.cs` 追加 `DbSet<ExamSubjectTeacher>` + Fluent API (唯一索引, 级联删除)

### Task 2: 数据库迁移
- `dotnet ef migrations add AddExamSubjectTeacher`
- `dotnet ef database update`

### Task 3: 控制器新增 Action
`ExamScheduleController.cs`:
- Index() 加载 AllTeachers(Admin列表) + AllClasses(ClassInfo列表) 到 ViewBag
- GetExamSubjectTeachers(examScheduleId, subjectId) - 获取考试级+全局教师分配
- SaveExamSubjectTeachers - 保存考试级教师分配(先删后插)

### Task 4: 视图更新
`Views/ExamSchedule/Index.cshtml`:
- 表格新增"科任教师"列(显示各科目教师+班级)
- 操作列新增"分配教师"按钮
- 新增分配教师模态框：选科目 → 班级×教师复选框矩阵(灰色=继承全局) → 保存刷新

### Task 5: 编译发布
- dotnet build → 修复编译错误 → publish 到 IIS