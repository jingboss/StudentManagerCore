# 成绩管理模块重新开发方案

## 背景
现有成绩管理模块存在以下问题：
- **录入太复杂**：在线输分、批量导入、旧版Entry 三套入口并行，操作步骤多
- **代码臃肿**：ScoreController 1347行，职责混杂
- **功能冗余**：分析、趋势、成绩单等功能实际使用率低

用户决定**从零开发**，删除现有所有成绩管理代码。

---

## 影响范围分析

### 需要删除的代码
| 文件 | 行数 | 说明 |
|------|------|------|
| `Controllers\ScoreController.cs` | 1347行 | 20个Action，全部删除 |
| `Controllers\SubjectScoreController.cs` | 249行 | 3个Action，全部删除 |
| `Services\ScoreService.cs` | 207行 | 包含 DTO 类，全部删除 |
| `Views\Score\Entry.cshtml` | 13.4KB | 已废弃 |
| `Views\Score\Query.cshtml` | 12.3KB | 成绩查询 |
| `Views\Score\Analysis.cshtml` | 18.0KB | 成绩分析 |
| `Views\Score\Import.cshtml` | 18.1KB | 批量导入 |
| `Views\Score\ReportCard.cshtml` | 13.1KB | 成绩单 |
| `Views\Score\StudentTrends.cshtml` | 13.0KB | 成绩趋势 |
| `Views\SubjectScore\OnlineEntry.cshtml` | 19.2KB | 在线输分 |

### 需要修改的文件
| 文件 | 修改内容 |
|------|----------|
| `Views\Shared\_Layout.cshtml` | 删除"成绩管理"下拉菜单（8个菜单项） |
| `Program.cs` | 删除 `ScoreService` 注册代码（第14行） |
| `Controllers\HomeController.cs` | 删除4处 `_db.Scores` 引用（第162/186/192/213行） |
| `Controllers\StudentController.cs` | 删除1处 `_db.Scores` 引用（第79行），改用其他统计数据 |
| `Controllers\SubjectController.cs` | 删除1处 `_db.Scores` 引用（科目删除前检查成绩使用） |
| `Views\Home\TeacherDashboard.cshtml` | 删除3处 Score/SubjectScore 链接 |
| `Views\Home\Index.cshtml` | 删除3处 Score 链接 |

### 保留的数据库表（数据不丢失）
| 表 | 说明 |
|------|------|
| `Score` | 保留，新系统继续使用 |
| `ExamSchedule` | 保留 |
| `ExamSubject` | 保留 |
| `Subject` | 保留（学生管理也在用） |
| `GradeSubject` | 保留（刚建的表） |
| `SubjectTeacher` | 保留（权限用） |
| `SubjectClass` | 保留 |

---

## 新系统设计方案

### 核心原则：极简
只保留3个核心功能，去除非必需的复杂功能：

### 功能一：考试安排（保留现有）
- 沿用现有 `ExamScheduleController` 和视图
- 创建考试 → 关联科目 → 设定状态
- **不删除**，已有功能已基本满足需求

### 功能二：成绩录入（全新设计）⭐ 核心
**现有问题**：选考试→选科目→选年级→选班级→填分，步骤太多

**新方案——「一键录入」**：
1. 选择考试安排（下拉框）
2. 自动加载该考试的所有科目 + 所有参考班级的学生
3. **直接在表格中填分**（类似 Excel 体验）：
   - 行 = 学生，列 = 科目
   - 点中单元格直接输入数字
   - Tab 键跳到下一个单元格
   - 输完自动保存（或一键批量保存）
4. **无需** 先选科目再选班级再选学生

**输入方式二**：保留 Excel 导入（简单版）
- 下载模板（含学生名单）
- 填分后上传
- 一键导入

### 功能三：成绩查看（简化为一个页面）
合并现有查询、分析、成绩单为一个页面：
- 选考试 → 选班级 → 显示成绩表（含排名）
- 可导出 Excel

> 去掉了独立的**成绩分析**、**成绩趋势图**、**成绩单**页面——这些功能在统一查看页面中都能覆盖

---

## 实施步骤

### 第一阶段：清理旧代码
1. 删除 ScoreController.cs、SubjectScoreController.cs
2. 删除 ScoreService.cs
3. 删除 7 个视图文件
4. 修改 _Layout.cshtm 删除菜单
5. 修改 HomeController、StudentController、SubjectController 清除 Scores 引用
6. 修改 Program.cs 删除 ScoreService 注册
7. 修改 Home/Index.cshtml、TeacherDashboard.cshtml 清除链接
8. 编译验证

### 第二阶段：设计数据库简化（可选）
- 考虑 Score 表去掉 ExamType、ExamDate 冗余字段（需迁移）
- 如果简化，则生成新迁移

### 第三阶段：构建新成绩录入
1. 新建 `ScoreEntryController`（或直接用 `ScoreController` 新写）
2. 新建录入视图（一键表格式录入）
3. 实现 Excel 导入（简化版）
4. 实现成绩查看页面

### 第四阶段：编译 + 发布部署
1. 编译验证
2. 发布到 E:\wwwroot\0008_qu4cz8\web
3. 验证录入流程

---

## 待确认问题

1. **要不要保留成绩录入中的 Excel 导入功能？** 还是全部手工录入？
2. **新录入界面**：表格式（行=学生，列=科目），所有分数一个页面搞定，这种布局可以吗？
3. **成绩查看**：简单显示 考试+班级 → 成绩表（含排名），不再有独立的分析/趋势/成绩单页面，可以吗？
4. **Score 表**：现有 ExamType/ExamDate 是冗余字段（可从 ExamSchedule 查到），要不要去掉它们？
5. **菜单**：新系统只放「成绩录入」和「成绩查看」两个菜单项，可以吗？

---

## 验证方式
1. 编译通过（0 错误）
2. 创建考试 → 关联科目 → 录入成绩 → 查看成绩 全流程走通
3. 成绩数据能正确写入数据库
4. 旧链接全部404检查（确保无残留引用）