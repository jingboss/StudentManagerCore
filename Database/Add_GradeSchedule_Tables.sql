-- ============================================
-- 排课管理模块 - 新增年级作息/课时配置表
-- 执行前请先备份数据库
-- ============================================

-- 1. 年级作息配置表
CREATE TABLE IF NOT EXISTS GradeScheduleConfig (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    GradeLevelId INT NOT NULL UNIQUE COMMENT '年级ID',
    DaysPerWeek INT NOT NULL DEFAULT 5 COMMENT '每周上课天数',
    PeriodsPerDay INT NOT NULL DEFAULT 8 COMMENT '每天节数',
    IsActive TINYINT(1) NOT NULL DEFAULT 1 COMMENT '是否当前生效',
    CreateTime DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (GradeLevelId) REFERENCES GradeLevel(GradeLevelID) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='年级作息配置';

-- 2. 年级时段表
CREATE TABLE IF NOT EXISTS GradePeriod (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    GradeLevelId INT NOT NULL COMMENT '年级ID',
    PeriodNumber INT NOT NULL COMMENT '第几节(从1开始)',
    StartTime VARCHAR(5) NOT NULL COMMENT '开始时间 HH:mm',
    EndTime VARCHAR(5) NOT NULL COMMENT '结束时间 HH:mm',
    SectionName VARCHAR(20) DEFAULT NULL COMMENT '所属节次分组：早晨/上午/下午/晚修',
    FOREIGN KEY (GradeLevelId) REFERENCES GradeLevel(GradeLevelID) ON DELETE CASCADE,
    UNIQUE KEY uk_grade_period (GradeLevelId, PeriodNumber)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='年级每节课时间段';

-- 3. 年级科目周课时配置表
CREATE TABLE IF NOT EXISTS GradeSubjectHour (
    Id INT AUTO_INCREMENT PRIMARY KEY,
    GradeLevelId INT NOT NULL COMMENT '年级ID',
    SubjectId INT NOT NULL COMMENT '科目ID',
    PeriodsPerWeek INT NOT NULL DEFAULT 0 COMMENT '每周课时数',
    CreateTime DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (GradeLevelId) REFERENCES GradeLevel(GradeLevelID) ON DELETE CASCADE,
    FOREIGN KEY (SubjectId) REFERENCES `Subject`(Id) ON DELETE CASCADE,
    UNIQUE KEY uk_grade_subject (GradeLevelId, SubjectId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COMMENT='年级科目周课时配置';
