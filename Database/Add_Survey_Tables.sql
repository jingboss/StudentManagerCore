-- 问卷调查系统建表脚本
-- Survey: 问卷主表
CREATE TABLE IF NOT EXISTS `Survey` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Title` varchar(200) NOT NULL,
    `Description` longtext,
    `Status` varchar(20) NOT NULL DEFAULT '草稿',
    `CreatedBy` int NOT NULL,
    `CreatorName` varchar(50) DEFAULT NULL,
    `CreateTime` datetime(6) NOT NULL,
    `UpdateTime` datetime(6) DEFAULT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- SurveyQuestion: 题目表
CREATE TABLE IF NOT EXISTS `SurveyQuestion` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SurveyId` int NOT NULL,
    `SortOrder` int NOT NULL DEFAULT 0,
    `Type` varchar(20) NOT NULL,
    `IsRequired` tinyint(1) NOT NULL DEFAULT 0,
    `Title` varchar(500) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_SurveyQuestion_SurveyId` (`SurveyId`),
    CONSTRAINT `FK_SurveyQuestion_Survey` FOREIGN KEY (`SurveyId`) REFERENCES `Survey` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- SurveyQuestionOption: 选项表（单选/多选用）
CREATE TABLE IF NOT EXISTS `SurveyQuestionOption` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `QuestionId` int NOT NULL,
    `SortOrder` int NOT NULL DEFAULT 0,
    `OptionText` varchar(200) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_SurveyQuestionOption_QuestionId` (`QuestionId`),
    CONSTRAINT `FK_SurveyQuestionOption_SurveyQuestion` FOREIGN KEY (`QuestionId`) REFERENCES `SurveyQuestion` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- SurveySubmission: 答卷表
CREATE TABLE IF NOT EXISTS `SurveySubmission` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SurveyId` int NOT NULL,
    `SubmittedBy` varchar(100) DEFAULT NULL,
    `SubmitterName` varchar(50) DEFAULT NULL,
    `SubmitTime` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_SurveySubmission_SurveyId` (`SurveyId`),
    CONSTRAINT `FK_SurveySubmission_Survey` FOREIGN KEY (`SurveyId`) REFERENCES `Survey` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- SurveyAnswer: 答案表
CREATE TABLE IF NOT EXISTS `SurveyAnswer` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `SubmissionId` int NOT NULL,
    `QuestionId` int NOT NULL,
    `AnswerText` longtext,
    `FilePath` varchar(500) DEFAULT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_SurveyAnswer_SubmissionId` (`SubmissionId`),
    CONSTRAINT `FK_SurveyAnswer_SurveySubmission` FOREIGN KEY (`SubmissionId`) REFERENCES `SurveySubmission` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
