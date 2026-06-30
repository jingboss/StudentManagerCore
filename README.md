# 学生成绩与教务管理系统

基于 ASP.NET Core 8 + MySQL 的学生管理与成绩分析系统，提供学生管理、成绩录入分析、考试安排、教职工管理、后勤管理、AI 智能分析等一体化教务解决方案。

## 技术栈

| 技术 | 版本 |
|------|------|
| ASP.NET Core | 8.0 |
| Entity Framework Core | 8.0 |
| MySQL (Pomelo) | 8.0+ |
| JWT Bearer Auth | — |
| Bootstrap | 5.3 |
| ClosedXML (Excel) | 0.104 |
| Chart.js | 4.4 |

## 功能概览

### 学生管理
- 学生信息增删改查、导入导出（Excel）
- 高级筛选（年级、班级、性别、户籍等）
- 毕业/休学/转学状态管理
- 已删除学生恢复与彻底删除
- 批量转班、批量毕业
- 学生详情查看（敏感信息掩码切换）

### 成绩管理
- 成绩录入（单科/多科）、Excel 批量导入
- 成绩查询与多维度分析
- 成绩报告单、学生成绩趋势图
- **AI 智能分析** — 基于大模型的学生/班级/科目深度分析报告

### 考试安排
- 考试科目管理
- 考场分配（原班考试 / 全年级打乱）
- 座位号编排
- 科任教师分配

### 班级管理
- 年级与班级体系（小学 / 初中学段）
- 在读人数实时统计
- 班主任分配与调整

### 教职工管理
- 教职工信息管理、角色分配
- Excel 批量导入
- 所教科目分配
- 账户密码管理

### 公告管理
- 公告发布与定向推送（按角色）
- 已读跟踪

### 后勤管理
- 维修申请、申报跟踪、后勤处理
- 维修状态流转

### 系统管理
- 管理员中心与角色权限
- 学生/教师细粒度权限控制
- 网站配置（站点名称、安全码、AI 参数等）
- 学期与学年管理
- 操作审计日志
- 系统安装向导（首次自动引导）
- 数据库一键备份、下载与还原

## 快速开始

### 环境要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [MySQL 8.0+](https://dev.mysql.com/downloads/)
- Visual Studio 2022 / VS Code / Rider

### 安装步骤

**方法一：自动安装向导（推荐）**

```bash
# 1. 克隆仓库
git clone https://github.com/your-username/StudentManagerCore.git
cd StudentManagerCore

# 2. 复制配置文件并填入数据库密码
cp appsettings.template.json appsettings.json
# 编辑 appsettings.json，填入你的 MySQL 连接信息

# 3. 编译运行
dotnet run
```

首次访问 `http://localhost:5000` 自动进入安装向导，按步骤配置即可。

**方法二：手动配置**

```bash
# 设置环境变量（数据库连接 + JWT密钥）
setx ConnectionStrings__DefaultConnection "Server=localhost;Database=StudentManagerDB;User Id=root;Password=你的密码;" /M
setx JWT_SECRET_KEY "你的密钥（至少32位字符）" /M

# 编译运行
dotnet run
```

### Windows IIS 部署

```bash
# 发布
dotnet publish -c Release -o D:\wwwroot\studentmanager

# IIS 中添加网站，指向发布目录
# 应用程序池选择 "无托管代码"（No Managed Code）
# 安装 AspNetCoreModuleHosting 模块
```

### Linux 部署

```bash
dotnet publish -c Release -o /var/www/studentmanager
# 安装 MySQL，创建数据库
# 配置环境变量
# 使用 supervisor/nginx 反向代理运行
```

## 项目结构

```
├── Controllers/       # MVC 控制器
├── Data/              # EF Core 数据上下文
├── Database/          # SQL 迁移脚本
├── DataMigrator/      # 数据迁移工具
├── Middleware/        # 中间件（安全、限流、IP白名单）
├── Migrations/        # EF Core 迁移
├── Models/            # 数据模型
├── Services/          # 业务服务
├── Views/             # Razor 视图
├── wwwroot/           # 静态资源
├── appsettings.json   # 配置文件
├── Program.cs         # 应用入口
└── StudentManagerCore.csproj
```
