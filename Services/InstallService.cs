using MySqlConnector;
using System.Text;

namespace StudentManagerCore.Services;

/// <summary>
/// 安装服务 — 使用原始 MySqlConnector 操作数据库（不依赖 EF Core）
/// </summary>
public class InstallService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;

    public InstallService(IWebHostEnvironment env, IConfiguration configuration)
    {
        _env = env;
        _configuration = configuration;
    }

    /// <summary>嵌入的完整建表 SQL（编译到 DLL 中，不依赖外部文件）</summary>
    private const string EmbeddedCreateTableSql = @"
SET FOREIGN_KEY_CHECKS = 0;

CREATE DATABASE IF NOT EXISTS `StudentManagerDB` DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Admin` (
    `AdminID` int NOT NULL AUTO_INCREMENT,
    `Username` varchar(50) NOT NULL,
    `Password` varchar(200) NOT NULL,
    `RealName` varchar(50) DEFAULT NULL,
    `Gender` varchar(10) DEFAULT NULL,
    `Nation` varchar(20) DEFAULT NULL,
    `BirthDate` date DEFAULT NULL,
    `RegisteredDomicile` varchar(200) DEFAULT NULL,
    `HighestEducation` varchar(50) DEFAULT NULL,
    `CertSubject` varchar(100) DEFAULT NULL,
    `CertNumber` varchar(100) DEFAULT NULL,
    `CertAuthority` varchar(200) DEFAULT NULL,
    `Permissions` varchar(200) DEFAULT NULL,
    `Status` varchar(20) DEFAULT NULL,
    `Role` char(10) DEFAULT NULL,
    `Phone` varchar(20) DEFAULT NULL,
    `ClassID` int DEFAULT NULL,
    `ClassName` varchar(50) DEFAULT NULL,
    `Grade` varchar(50) DEFAULT NULL,
    `Position` varchar(50) DEFAULT NULL,
    `SchoolType` varchar(10) DEFAULT NULL,
    `EndStage` varchar(20) DEFAULT NULL,
    `DingTalkUnionId` varchar(100) DEFAULT NULL,
    `CreateTime` datetime(6) DEFAULT NULL,
    PRIMARY KEY (`AdminID`),
    UNIQUE KEY `IX_Admin_DingTalkUnionId` (`DingTalkUnionId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Student` (
    `StudentID` int NOT NULL AUTO_INCREMENT,
    `StudentNo` varchar(8) DEFAULT NULL,
    `Grade` varchar(50) DEFAULT NULL,
    `ClassName` varchar(50) DEFAULT NULL,
    `Name` varchar(50) DEFAULT NULL,
    `Gender` varchar(10) DEFAULT NULL,
    `IDCardNumber` varchar(18) DEFAULT NULL,
    `Nation` varchar(20) DEFAULT NULL,
    `HouseholdCity` varchar(200) DEFAULT NULL,
    `HouseholdAddress` varchar(200) DEFAULT NULL,
    `HouseholdType` varchar(20) DEFAULT NULL,
    `IsNonLocalHousehold` varchar(10) DEFAULT NULL,
    `IsMigrantChild` varchar(10) DEFAULT NULL,
    `IsMigrantWorkerChild` varchar(10) DEFAULT NULL,
    `CurrentResidence` varchar(200) DEFAULT NULL,
    `FatherName` varchar(50) DEFAULT NULL,
    `FatherPhone` varchar(20) DEFAULT NULL,
    `MotherName` varchar(50) DEFAULT NULL,
    `MotherPhone` varchar(20) DEFAULT NULL,
    `ClassID` int DEFAULT NULL,
    `Status` varchar(50) DEFAULT NULL,
    `Remark` varchar(500) DEFAULT NULL,
    `CreateTime` datetime(6) DEFAULT NULL,
    `UpdateTime` datetime(6) DEFAULT NULL,
    PRIMARY KEY (`StudentID`),
    UNIQUE KEY `IX_Student_StudentNo` (`StudentNo`),
    UNIQUE KEY `IX_Student_IDCardNumber` (`IDCardNumber`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `SiteConfig` (
    `ConfigKey` varchar(100) NOT NULL,
    `ConfigValue` varchar(500) DEFAULT NULL,
    PRIMARY KEY (`ConfigKey`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `GradeLevel` (
    `GradeLevelID` int NOT NULL AUTO_INCREMENT,
    `EntryYear` int NOT NULL,
    `SchoolType` varchar(10) NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`GradeLevelID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ClassInfo` (
    `ClassInfoID` int NOT NULL AUTO_INCREMENT,
    `GradeLevelID` int NOT NULL,
    `ClassName` varchar(20) NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`ClassInfoID`),
    KEY `IX_ClassInfo_GradeLevelID` (`GradeLevelID`),
    CONSTRAINT `FK_ClassInfo_GradeLevel` FOREIGN KEY (`GradeLevelID`) REFERENCES `GradeLevel` (`GradeLevelID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Announcement` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(200) NOT NULL,
    `TargetRole` varchar(20) NOT NULL,
    `Content` longtext NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    `CreatedBy` varchar(50) DEFAULT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `AnnouncementRead` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AnnouncementId` int NOT NULL,
    `TeacherPhone` varchar(20) DEFAULT NULL,
    `ReadTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `OperationLog` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `OperatorName` varchar(50) DEFAULT NULL,
    `OperatorRole` varchar(20) DEFAULT NULL,
    `ActionType` varchar(30) DEFAULT NULL,
    `TargetNo` varchar(20) DEFAULT NULL,
    `TargetName` varchar(50) DEFAULT NULL,
    `Detail` longtext,
    `IpAddress` varchar(50) DEFAULT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `AcademicYear` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `YearName` varchar(20) NOT NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Semester` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AcademicYearId` int NOT NULL,
    `SemesterName` varchar(20) NOT NULL,
    `IsCurrent` tinyint(1) NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Semester_AcademicYearId` (`AcademicYearId`),
    CONSTRAINT `FK_Semester_AcademicYear` FOREIGN KEY (`AcademicYearId`) REFERENCES `AcademicYear` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Subject` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(50) NOT NULL,
    `Grade` varchar(50) DEFAULT NULL,
    `SortOrder` int NOT NULL DEFAULT 0,
    `FullScore` int NOT NULL DEFAULT 100,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `SubjectTeacher` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SubjectId` int NOT NULL,
    `AdminId` int NOT NULL,
    `ClassId` int NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_SubjectTeacher_AdminId` (`AdminId`),
    KEY `IX_SubjectTeacher_ClassId` (`ClassId`),
    UNIQUE KEY `IX_SubjectTeacher_SubjectId_AdminId_ClassId` (`SubjectId`, `AdminId`, `ClassId`),
    CONSTRAINT `FK_SubjectTeacher_Admin` FOREIGN KEY (`AdminId`) REFERENCES `Admin` (`AdminID`) ON DELETE CASCADE,
    CONSTRAINT `FK_SubjectTeacher_ClassInfo` FOREIGN KEY (`ClassId`) REFERENCES `ClassInfo` (`ClassInfoID`) ON DELETE CASCADE,
    CONSTRAINT `FK_SubjectTeacher_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `SubjectClass` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SubjectId` int NOT NULL,
    `ClassId` int NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_SubjectClass_SubjectId_ClassId` (`SubjectId`, `ClassId`),
    CONSTRAINT `FK_SubjectClass_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `Score` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StudentId` int NOT NULL,
    `SubjectId` int NOT NULL,
    `ScoreValue` decimal(5,1) NOT NULL,
    `ExamType` varchar(30) DEFAULT NULL,
    `ExamDate` datetime(6) NOT NULL,
    `ExamScheduleId` int NOT NULL,
    `GradeLevelId` int DEFAULT NULL,
    `ClassInfoId` int DEFAULT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_Score_StudentId` (`StudentId`),
    KEY `IX_Score_SubjectId` (`SubjectId`),
    KEY `IX_Score_ExamScheduleId` (`ExamScheduleId`),
    KEY `IX_Score_GradeLevelId` (`GradeLevelId`),
    KEY `IX_Score_ClassInfoId` (`ClassInfoId`),
    UNIQUE KEY `IX_Score_StudentId_SubjectId_ExamScheduleId` (`StudentId`, `SubjectId`, `ExamScheduleId`),
    CONSTRAINT `FK_Score_Student` FOREIGN KEY (`StudentId`) REFERENCES `Student` (`StudentID`) ON DELETE CASCADE,
    CONSTRAINT `FK_Score_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_Score_ExamSchedule` FOREIGN KEY (`ExamScheduleId`) REFERENCES `ExamSchedule` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_Score_GradeLevel` FOREIGN KEY (`GradeLevelId`) REFERENCES `GradeLevel` (`GradeLevelID`),
    CONSTRAINT `FK_Score_ClassInfo` FOREIGN KEY (`ClassInfoId`) REFERENCES `ClassInfo` (`ClassInfoID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ExamSchedule` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(100) NOT NULL,
    `ExamType` varchar(30) NOT NULL,
    `Grades` varchar(500) DEFAULT NULL,
    `ExamDate` datetime(6) NOT NULL,
    `EndDate` datetime(6) DEFAULT NULL,
    `SemesterId` int NOT NULL,
    `Status` varchar(20) NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ExamSchedule_SemesterId` (`SemesterId`),
    CONSTRAINT `FK_ExamSchedule_Semester` FOREIGN KEY (`SemesterId`) REFERENCES `Semester` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ExamSubject` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamScheduleId` int NOT NULL,
    `SubjectId` int NOT NULL,
    `FullScore` int DEFAULT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ExamSubject_SubjectId` (`SubjectId`),
    UNIQUE KEY `IX_ExamSubject_ExamScheduleId_SubjectId` (`ExamScheduleId`, `SubjectId`),
    CONSTRAINT `FK_ExamSubject_ExamSchedule` FOREIGN KEY (`ExamScheduleId`) REFERENCES `ExamSchedule` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ExamSubject_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ExamRoom` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamScheduleId` int NOT NULL,
    `Grade` varchar(50) NOT NULL,
    `ArrangeMode` varchar(20) NOT NULL,
    `RoomName` varchar(100) NOT NULL,
    `SeatCount` int NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ExamRoom_ExamScheduleId` (`ExamScheduleId`),
    CONSTRAINT `FK_ExamRoom_ExamSchedule` FOREIGN KEY (`ExamScheduleId`) REFERENCES `ExamSchedule` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ExamRoomStudent` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamRoomId` int NOT NULL,
    `StudentId` int NOT NULL,
    `SeatNumber` int NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ExamRoomStudent_ExamRoomId` (`ExamRoomId`),
    KEY `IX_ExamRoomStudent_StudentId` (`StudentId`),
    CONSTRAINT `FK_ExamRoomStudent_ExamRoom` FOREIGN KEY (`ExamRoomId`) REFERENCES `ExamRoom` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ExamRoomStudent_Student` FOREIGN KEY (`StudentId`) REFERENCES `Student` (`StudentID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `GradeSubject` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `GradeLevelId` int NOT NULL,
    `SubjectId` int NOT NULL,
    `FullScore` int DEFAULT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_GradeSubject_SubjectId` (`SubjectId`),
    UNIQUE KEY `IX_GradeSubject_GradeLevelId_SubjectId` (`GradeLevelId`, `SubjectId`),
    CONSTRAINT `FK_GradeSubject_GradeLevel` FOREIGN KEY (`GradeLevelId`) REFERENCES `GradeLevel` (`GradeLevelID`) ON DELETE CASCADE,
    CONSTRAINT `FK_GradeSubject_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ExamSubjectTeacher` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamScheduleId` int NOT NULL,
    `SubjectId` int NOT NULL,
    `AdminId` int NOT NULL,
    `ClassId` int NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ExamSubjectTeacher_AdminId` (`AdminId`),
    KEY `IX_ExamSubjectTeacher_ClassId` (`ClassId`),
    KEY `IX_ExamSubjectTeacher_SubjectId` (`SubjectId`),
    UNIQUE KEY `IX_ExamSubjectTeacher_ExamScheduleId_SubjectId_AdminId_ClassId` (`ExamScheduleId`, `SubjectId`, `AdminId`, `ClassId`),
    CONSTRAINT `FK_ExamSubjectTeacher_Admin` FOREIGN KEY (`AdminId`) REFERENCES `Admin` (`AdminID`) ON DELETE CASCADE,
    CONSTRAINT `FK_ExamSubjectTeacher_ClassInfo` FOREIGN KEY (`ClassId`) REFERENCES `ClassInfo` (`ClassInfoID`) ON DELETE CASCADE,
    CONSTRAINT `FK_ExamSubjectTeacher_ExamSchedule` FOREIGN KEY (`ExamScheduleId`) REFERENCES `ExamSchedule` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ExamSubjectTeacher_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `TeacherSubject` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AdminId` int NOT NULL,
    `SubjectId` int NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_TeacherSubject_SubjectId` (`SubjectId`),
    UNIQUE KEY `IX_TeacherSubject_AdminId_SubjectId` (`AdminId`, `SubjectId`),
    CONSTRAINT `FK_TeacherSubject_Admin` FOREIGN KEY (`AdminId`) REFERENCES `Admin` (`AdminID`) ON DELETE CASCADE,
    CONSTRAINT `FK_TeacherSubject_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `AiAnalysisResult` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamScheduleId` int NOT NULL,
    `StudentId` int NOT NULL,
    `AnalysisResult` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_AiAnalysisResult_ExamScheduleId_StudentId` (`ExamScheduleId`, `StudentId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `AiClassAnalysisResult` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamScheduleId` int NOT NULL,
    `ClassInfoId` int NOT NULL,
    `GradeLevelId` int NOT NULL,
    `AnalysisResult` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_AiClassAnalysisResult_ExamScheduleId_ClassInfoId` (`ExamScheduleId`, `ClassInfoId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `AiSubjectAnalysisResult` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ExamScheduleId` int NOT NULL,
    `ClassInfoId` int NOT NULL,
    `SubjectId` int NOT NULL,
    `GradeLevelId` int NOT NULL,
    `AnalysisResult` longtext NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_AiSubjectAnalysisResult_ExamScheduleId_ClassInfoId_SubjectId` (`ExamScheduleId`, `ClassInfoId`, `SubjectId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ScheduleSetting` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(50) NOT NULL,
    `DaysPerWeek` int NOT NULL,
    `PeriodsPerDay` int NOT NULL,
    `PeriodDurationMinutes` int NOT NULL,
    `StartTime` varchar(5) NOT NULL,
    `BreakMinutes` int NOT NULL,
    `IsActive` tinyint(1) NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `SchedulePeriod` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SettingId` int NOT NULL,
    `PeriodNumber` int NOT NULL,
    `StartTime` varchar(5) NOT NULL,
    `EndTime` varchar(5) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_SchedulePeriod_SettingId_PeriodNumber` (`SettingId`, `PeriodNumber`),
    CONSTRAINT `FK_SchedulePeriod_ScheduleSetting` FOREIGN KEY (`SettingId`) REFERENCES `ScheduleSetting` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `ClassSchedule` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `ClassId` int NOT NULL,
    `SemesterId` int NOT NULL,
    `DayOfWeek` int NOT NULL,
    `Period` int NOT NULL,
    `SubjectId` int NOT NULL,
    `TeacherId` int NOT NULL,
    `CreateTime` datetime(6) NOT NULL,
    `UpdateTime` datetime(6) DEFAULT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ClassSchedule_SemesterId` (`SemesterId`),
    KEY `IX_ClassSchedule_SubjectId` (`SubjectId`),
    UNIQUE KEY `IX_ClassSchedule_ClassId_SemesterId_DayOfWeek_Period` (`ClassId`, `SemesterId`, `DayOfWeek`, `Period`),
    KEY `IX_ClassSchedule_TeacherId_SemesterId_DayOfWeek_Period` (`TeacherId`, `SemesterId`, `DayOfWeek`, `Period`),
    CONSTRAINT `FK_ClassSchedule_ClassInfo` FOREIGN KEY (`ClassId`) REFERENCES `ClassInfo` (`ClassInfoID`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClassSchedule_Semester` FOREIGN KEY (`SemesterId`) REFERENCES `Semester` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClassSchedule_Subject` FOREIGN KEY (`SubjectId`) REFERENCES `Subject` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ClassSchedule_Admin` FOREIGN KEY (`TeacherId`) REFERENCES `Admin` (`AdminID`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `RepairRequest` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(200) NOT NULL,
    `Description` longtext,
    `Location` varchar(200) DEFAULT NULL,
    `ContactPhone` varchar(20) DEFAULT NULL,
    `Status` varchar(20) NOT NULL DEFAULT '待处理',
    `CreateTime` datetime(6) NOT NULL,
    `PreferredTime` datetime(6) DEFAULT NULL,
    `CreatedBy` int NOT NULL,
    `CreatorName` varchar(50) DEFAULT NULL,
    `ProcessTime` datetime(6) DEFAULT NULL,
    `ProcessedBy` int DEFAULT NULL,
    `ProcessorName` varchar(50) DEFAULT NULL,
    `Remark` longtext,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET FOREIGN_KEY_CHECKS = 1;";

    /// <summary>测试 MySQL 连接是否有效</summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(string connectionString)
    {
        try
        {
            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();
            return (true, "连接成功");
        }
        catch (Exception ex)
        {
            return (false, $"连接失败：{ex.Message}");
        }
    }

    /// <summary>安装：执行全部建表 SQL（直接使用嵌入代码中的 SQL）</summary>
    public async Task<(bool Success, string Message)> CreateTablesAsync(string connectionString)
    {
        try
        {
            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            // 按分号分割逐条执行
            var statements = EmbeddedCreateTableSql.Split(';', StringSplitOptions.RemoveEmptyEntries);
            int executed = 0;
            foreach (var stmt in statements)
            {
                var trimmed = stmt.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                using var cmd = new MySqlCommand(trimmed, conn);
                await cmd.ExecuteNonQueryAsync();
                executed++;
            }

            return (true, $"成功执行 {executed} 条 SQL 语句，所有数据表已创建");
        }
        catch (Exception ex)
        {
            return (false, $"建表失败：{ex.Message}");
        }
    }

    /// <summary>安装：创建管理员账号</summary>
    public async Task<(bool Success, string Message)> CreateAdminAsync(
        string connectionString, string username, string password, string realName)
    {
        try
        {
            // 密码哈希
            var hashedPassword = PasswordHelper.Hash(password);

            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            // 检查是否已存在管理员
            using var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM Admin WHERE Role = '管理员'", conn);
            var existCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            if (existCount > 0)
                return (false, "系统中已存在管理员账号，不能重复创建");

            // 插入管理员
            using var cmd = new MySqlCommand(@"
                INSERT INTO Admin (Username, Password, RealName, Role, Permissions, Status, CreateTime)
                VALUES (@Username, @Password, @RealName, '管理员', 'all', '正常', NOW())", conn);

            cmd.Parameters.AddWithValue("@Username", username);
            cmd.Parameters.AddWithValue("@Password", hashedPassword);
            cmd.Parameters.AddWithValue("@RealName", realName);

            await cmd.ExecuteNonQueryAsync();
            return (true, "管理员账号创建成功");
        }
        catch (Exception ex)
        {
            return (false, $"创建管理员失败：{ex.Message}");
        }
    }

    /// <summary>安装：保存站点配置</summary>
    public async Task<(bool Success, string Message)> SaveSiteConfigAsync(
        string connectionString, Dictionary<string, string> configs)
    {
        try
        {
            using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync();

            foreach (var kv in configs)
            {
                using var cmd = new MySqlCommand(@"
                    INSERT INTO SiteConfig (ConfigKey, ConfigValue)
                    VALUES (@Key, @Value)
                    ON DUPLICATE KEY UPDATE ConfigValue = @Value2", conn);

                cmd.Parameters.AddWithValue("@Key", kv.Key);
                cmd.Parameters.AddWithValue("@Value", kv.Value);
                cmd.Parameters.AddWithValue("@Value2", kv.Value);
                await cmd.ExecuteNonQueryAsync();
            }

            return (true, "网站配置保存成功");
        }
        catch (Exception ex)
        {
            return (false, $"保存配置失败：{ex.Message}");
        }
    }

    /// <summary>写入锁定文件，标记安装完成</summary>
    public void WriteLockFile()
    {
        var lockPath = Path.Combine(_env.ContentRootPath, "app_installed.lock");
        File.WriteAllText(lockPath,
            $"Installed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nVersion: 1.0");
    }

    /// <summary>检查是否已安装</summary>
    public static bool IsInstalled(string contentRootPath)
    {
        var lockPath = Path.Combine(contentRootPath, "app_installed.lock");
        return File.Exists(lockPath);
    }

    /// <summary>将连接字符串写入 appsettings.json</summary>
    public void SaveConnectionString(string connectionString)
    {
        var jsonPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        if (!File.Exists(jsonPath))
            return;

        var json = File.ReadAllText(jsonPath, Encoding.UTF8);

        // 替换连接字符串
        var oldConnStr = _configuration.GetConnectionString("DefaultConnection") ?? "";
        if (!string.IsNullOrEmpty(oldConnStr) && json.Contains(oldConnStr))
        {
            json = json.Replace(oldConnStr, connectionString);
        }
        else
        {
            // 尝试正则替换
            var pattern = "\"DefaultConnection\"\\s*:\\s*\"[^\"]*\"";
            var replacement = $"\"DefaultConnection\": \"{connectionString}\"";
            json = System.Text.RegularExpressions.Regex.Replace(json, pattern, replacement);
        }

        File.WriteAllText(jsonPath, json, Encoding.UTF8);
    }

    /// <summary>将JWT密钥写入 appsettings.json</summary>
    public void SaveJwtSecret(string jwtSecret)
    {
        var jsonPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
        if (!File.Exists(jsonPath))
            return;

        var json = File.ReadAllText(jsonPath, Encoding.UTF8);

        // 检查 Jwt:SecretKey 是否已存在
        var secretPattern = @"""SecretKey""\s*:\s*""[^""]*""";
        if (System.Text.RegularExpressions.Regex.IsMatch(json, secretPattern))
        {
            // 替换已有的 SecretKey
            json = System.Text.RegularExpressions.Regex.Replace(json, secretPattern, $"\"SecretKey\": \"{jwtSecret}\"");
        }
        else
        {
            // 在 Jwt 节中插入 SecretKey（在 Issuer 行之后插入）
            var issuerPattern = @"(""Issuer""\s*:\s*""[^""]*"")";
            var replacement = $"$1,\n    \"SecretKey\": \"{jwtSecret}\"";
            json = System.Text.RegularExpressions.Regex.Replace(json, issuerPattern, replacement);
        }

        File.WriteAllText(jsonPath, json, Encoding.UTF8);
    }
}
