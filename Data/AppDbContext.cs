using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Models;

namespace StudentManagerCore.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Admin> Admins { get; set; }
    public DbSet<Student> Students { get; set; }
    public DbSet<SiteConfig> SiteConfigs { get; set; }
    public DbSet<GradeLevel> GradeLevels { get; set; }
    public DbSet<ClassInfo> ClassInfos { get; set; }
    public DbSet<Announcement> Announcements { get; set; }
    public DbSet<AnnouncementRead> AnnouncementReads { get; set; }
    public DbSet<OperationLog> OperationLogs { get; set; }
    public DbSet<AcademicYear> AcademicYears { get; set; }
    public DbSet<Semester> Semesters { get; set; }
    public DbSet<SubjectTeacher> SubjectTeachers { get; set; }
    public DbSet<SubjectClass> SubjectClasses { get; set; }
    public DbSet<Subject> Subjects { get; set; }
    public DbSet<Score> Scores { get; set; }
    public DbSet<ExamSchedule> ExamSchedules { get; set; }
    public DbSet<ExamSubject> ExamSubjects { get; set; }
    public DbSet<ExamRoom> ExamRooms { get; set; }
    public DbSet<ExamRoomStudent> ExamRoomStudents { get; set; }
    public DbSet<GradeSubject> GradeSubjects { get; set; }
    public DbSet<ExamSubjectTeacher> ExamSubjectTeachers { get; set; }
    public DbSet<TeacherSubject> TeacherSubjects { get; set; }
    public DbSet<AiAnalysisResult> AiAnalysisResults { get; set; }
    public DbSet<AiClassAnalysisResult> AiClassAnalysisResults { get; set; }
    public DbSet<AiSubjectAnalysisResult> AiSubjectAnalysisResults { get; set; }
    public DbSet<ScheduleSetting> ScheduleSettings { get; set; }
    public DbSet<SchedulePeriod> SchedulePeriods { get; set; }
    public DbSet<GradeScheduleConfig> GradeScheduleConfigs { get; set; }
    public DbSet<GradePeriod> GradePeriods { get; set; }
    public DbSet<GradeSubjectHour> GradeSubjectHours { get; set; }
    public DbSet<ClassSchedule> ClassSchedules { get; set; }
    public DbSet<RepairRequest> RepairRequests { get; set; }
    public DbSet<Survey> Surveys { get; set; }
    public DbSet<SurveyQuestion> SurveyQuestions { get; set; }
    public DbSet<SurveyQuestionOption> SurveyQuestionOptions { get; set; }
    public DbSet<SurveySubmission> SurveySubmissions { get; set; }
    public DbSet<SurveyAnswer> SurveyAnswers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Admin table mapping
        modelBuilder.Entity<Admin>(entity =>
        {
            entity.ToTable("Admin");
            entity.Property(e => e.AdminID).HasColumnName("AdminID");
            entity.Property(e => e.Username).HasColumnName("Username");
            entity.Property(e => e.Password).HasColumnName("Password");
            entity.Property(e => e.RealName).HasColumnName("RealName");
            entity.Property(e => e.Role).HasColumnName("Role").HasColumnType("varchar(100)");
            entity.Property(e => e.Phone).HasColumnName("Phone");
            entity.Property(e => e.ClassID).HasColumnName("ClassID");
            entity.Property(e => e.ClassName).HasColumnName("ClassName");
            entity.Property(e => e.Grade).HasColumnName("Grade");
            entity.Property(e => e.Position).HasColumnName("Position");
            entity.Property(e => e.SchoolType).HasColumnName("SchoolType").HasMaxLength(10);
            entity.Property(e => e.EndStage).HasColumnName("EndStage").HasMaxLength(20);
            entity.Property(e => e.DingTalkUnionId).HasColumnName("DingTalkUnionId").HasMaxLength(100);
            entity.HasIndex(e => e.DingTalkUnionId).IsUnique();
        });

        // Student table mapping
        modelBuilder.Entity<Student>(entity =>
        {
            entity.ToTable("Student");
            entity.Property(e => e.StudentID).HasColumnName("StudentID");
            entity.Property(e => e.StudentNo).HasColumnName("StudentNo");
            entity.Property(e => e.Grade).HasColumnName("Grade");
            entity.Property(e => e.ClassName).HasColumnName("ClassName");
            entity.Property(e => e.Name).HasColumnName("Name");
            entity.Property(e => e.Gender).HasColumnName("Gender");
            entity.Property(e => e.IDCardNumber).HasColumnName("IDCardNumber");
            entity.Property(e => e.Nation).HasColumnName("Nation");
            entity.Property(e => e.HouseholdCity).HasColumnName("HouseholdCity");
            entity.Property(e => e.HouseholdAddress).HasColumnName("HouseholdAddress");
            entity.Property(e => e.HouseholdType).HasColumnName("HouseholdType");
            entity.Property(e => e.IsNonLocalHousehold).HasColumnName("IsNonLocalHousehold");
            entity.Property(e => e.IsMigrantChild).HasColumnName("IsMigrantChild");
            entity.Property(e => e.IsMigrantWorkerChild).HasColumnName("IsMigrantWorkerChild");
            entity.Property(e => e.CurrentResidence).HasColumnName("CurrentResidence");
            entity.Property(e => e.FatherName).HasColumnName("FatherName");
            entity.Property(e => e.FatherPhone).HasColumnName("FatherPhone");
            entity.Property(e => e.MotherName).HasColumnName("MotherName");
            entity.Property(e => e.MotherPhone).HasColumnName("MotherPhone");
            entity.Property(e => e.ClassID).HasColumnName("ClassID");
            entity.Property(e => e.Status).HasColumnName("Status");
            entity.Property(e => e.Remark).HasColumnName("Remark");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.Property(e => e.UpdateTime).HasColumnName("UpdateTime");
            entity.HasIndex(e => e.StudentNo).IsUnique();
            entity.HasIndex(e => e.IDCardNumber).IsUnique();
        });

        // SiteConfig table mapping
        modelBuilder.Entity<SiteConfig>(entity =>
        {
            entity.ToTable("SiteConfig");
            entity.HasKey(e => e.ConfigKey);
            entity.Property(e => e.ConfigKey).HasColumnName("ConfigKey").HasMaxLength(100);
            entity.Property(e => e.ConfigValue).HasColumnName("ConfigValue").HasMaxLength(500);
        });

        // GradeLevel table mapping
        modelBuilder.Entity<GradeLevel>(entity =>
        {
            entity.ToTable("GradeLevel");
            entity.HasKey(e => e.GradeLevelID);
            entity.Property(e => e.EntryYear).HasColumnName("EntryYear");
            entity.Property(e => e.SchoolType).HasColumnName("SchoolType").HasMaxLength(10);
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
        });

        // ClassInfo table mapping
        modelBuilder.Entity<ClassInfo>(entity =>
        {
            entity.ToTable("ClassInfo");
            entity.HasKey(e => e.ClassInfoID);
            entity.Property(e => e.GradeLevelID).HasColumnName("GradeLevelID");
            entity.Property(e => e.ClassName).HasColumnName("ClassName").HasMaxLength(20);
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");

            entity.HasOne(e => e.GradeLevel)
                  .WithMany(g => g.Classes)
                  .HasForeignKey(e => e.GradeLevelID)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Announcement table mapping
        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.ToTable("Announcement");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(200).IsRequired();
            entity.Property(e => e.TargetRole).HasColumnName("TargetRole").HasMaxLength(20);
            entity.Property(e => e.Content).HasColumnName("Content").IsRequired();
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.Property(e => e.CreatedBy).HasColumnName("CreatedBy").HasMaxLength(50);
        });

        // AnnouncementRead table mapping
        modelBuilder.Entity<AnnouncementRead>(entity =>
        {
            entity.ToTable("AnnouncementRead");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AnnouncementId).HasColumnName("AnnouncementId");
            entity.Property(e => e.TeacherPhone).HasColumnName("TeacherPhone").HasMaxLength(20);
            entity.Property(e => e.ReadTime).HasColumnName("ReadTime");
        });

        // OperationLog table mapping
        modelBuilder.Entity<OperationLog>(entity =>
        {
            entity.ToTable("OperationLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id");
            entity.Property(e => e.OperatorName).HasColumnName("OperatorName").HasMaxLength(50);
            entity.Property(e => e.OperatorRole).HasColumnName("OperatorRole").HasMaxLength(20);
            entity.Property(e => e.ActionType).HasColumnName("ActionType").HasMaxLength(30);
            entity.Property(e => e.TargetNo).HasColumnName("TargetNo").HasMaxLength(20);
            entity.Property(e => e.TargetName).HasColumnName("TargetName").HasMaxLength(50);
            entity.Property(e => e.Detail).HasColumnName("Detail");
            entity.Property(e => e.IpAddress).HasColumnName("IpAddress").HasMaxLength(50);
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
        });

        // AcademicYear table mapping
        modelBuilder.Entity<AcademicYear>(entity =>
        {
            entity.ToTable("AcademicYear");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.YearName).HasColumnName("YearName").HasMaxLength(20).IsRequired();
            entity.Property(e => e.IsCurrent).HasColumnName("IsCurrent");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
        });

        // Semester table mapping
        modelBuilder.Entity<Semester>(entity =>
        {
            entity.ToTable("Semester");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AcademicYearId).HasColumnName("AcademicYearId");
            entity.Property(e => e.SemesterName).HasColumnName("SemesterName").HasMaxLength(20).IsRequired();
            entity.Property(e => e.IsCurrent).HasColumnName("IsCurrent");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.HasOne(e => e.AcademicYear).WithMany().HasForeignKey(e => e.AcademicYearId);
        });

        // Subject table mapping
        modelBuilder.Entity<Subject>(entity =>
        {
            entity.ToTable("Subject");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Grade).HasColumnName("Grade").HasMaxLength(50);
            entity.Property(e => e.SortOrder).HasColumnName("SortOrder");
            entity.Property(e => e.FullScore).HasColumnName("FullScore");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
        });
        modelBuilder.Entity<SubjectTeacher>(entity =>
        {
            entity.ToTable("SubjectTeacher");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.AdminId).HasColumnName("AdminId");
            entity.Property(e => e.ClassId).HasColumnName("ClassId");
            entity.Property(e => e.ClassId).IsRequired();
            entity.HasOne(e => e.ClassInfo).WithMany().HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.SubjectId, e.AdminId, e.ClassId }).IsUnique();
        });
        modelBuilder.Entity<SubjectClass>(entity =>
        {
            entity.ToTable("SubjectClass");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.ClassId).HasColumnName("ClassId");
            entity.HasIndex(e => new { e.SubjectId, e.ClassId }).IsUnique();
        });

        // Score table mapping
        modelBuilder.Entity<Score>(entity =>
        {
            entity.ToTable("Score");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StudentId).HasColumnName("StudentId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.ScoreValue).HasColumnName("ScoreValue").HasColumnType("decimal(5,1)");
            entity.Property(e => e.ExamType).HasColumnName("ExamType").HasMaxLength(30);
            entity.Property(e => e.ExamDate).HasColumnName("ExamDate");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId").IsRequired();
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.ClassInfoId).HasColumnName("ClassInfoId");
            entity.HasOne(e => e.Student).WithMany().HasForeignKey(e => e.StudentId);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId);
            entity.HasOne(e => e.ExamSchedule).WithMany().HasForeignKey(e => e.ExamScheduleId);
            entity.HasOne(e => e.GradeLevel).WithMany().HasForeignKey(e => e.GradeLevelId);
            entity.HasOne(e => e.ClassInfo).WithMany().HasForeignKey(e => e.ClassInfoId);
            entity.HasIndex(e => new { e.StudentId, e.SubjectId, e.ExamScheduleId }).IsUnique();
        });

        // ExamSchedule table mapping
        modelBuilder.Entity<ExamSchedule>(entity =>
        {
            entity.ToTable("ExamSchedule");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ExamType).HasColumnName("ExamType").HasMaxLength(30).IsRequired();
            entity.Property(e => e.Grades).HasColumnName("Grades").HasMaxLength(500);
            entity.Property(e => e.ExamDate).HasColumnName("ExamDate");
            entity.Property(e => e.EndDate).HasColumnName("EndDate");
            entity.Property(e => e.SemesterId).HasColumnName("SemesterId");
            entity.Property(e => e.Status).HasColumnName("Status").HasMaxLength(20);
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.HasOne(e => e.Semester).WithMany().HasForeignKey(e => e.SemesterId);
        });

        // ExamSubject table mapping
        modelBuilder.Entity<ExamSubject>(entity =>
        {
            entity.ToTable("ExamSubject");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.FullScore).HasColumnName("FullScore");
            entity.HasOne(e => e.ExamSchedule).WithMany(e => e.ExamSubjects).HasForeignKey(e => e.ExamScheduleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ExamScheduleId, e.SubjectId }).IsUnique();
        });

        // ExamRoom table mapping
        modelBuilder.Entity<ExamRoom>(entity =>
        {
            entity.ToTable("ExamRoom");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId");
            entity.Property(e => e.Grade).HasColumnName("Grade").HasMaxLength(50);
            entity.Property(e => e.ArrangeMode).HasColumnName("ArrangeMode").HasMaxLength(20);
            entity.Property(e => e.RoomName).HasColumnName("RoomName").HasMaxLength(100);
            entity.Property(e => e.SeatCount).HasColumnName("SeatCount");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.HasOne(e => e.ExamSchedule).WithMany().HasForeignKey(e => e.ExamScheduleId).OnDelete(DeleteBehavior.Cascade);
        });

        // ExamRoomStudent table mapping
        modelBuilder.Entity<ExamRoomStudent>(entity =>
        {
            entity.ToTable("ExamRoomStudent");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamRoomId).HasColumnName("ExamRoomId");
            entity.Property(e => e.StudentId).HasColumnName("StudentId");
            entity.Property(e => e.SeatNumber).HasColumnName("SeatNumber");
            entity.HasOne(e => e.ExamRoom).WithMany(e => e.Students).HasForeignKey(e => e.ExamRoomId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Student).WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        // GradeSubject table mapping
        modelBuilder.Entity<GradeSubject>(entity =>
        {
            entity.ToTable("GradeSubject");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.FullScore).HasColumnName("FullScore");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.HasOne(e => e.GradeLevel).WithMany().HasForeignKey(e => e.GradeLevelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.GradeLevelId, e.SubjectId }).IsUnique();
        });

        // ExamSubjectTeacher table mapping
        modelBuilder.Entity<ExamSubjectTeacher>(entity =>
        {
            entity.ToTable("ExamSubjectTeacher");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.AdminId).HasColumnName("AdminId");
            entity.Property(e => e.ClassId).HasColumnName("ClassId");
            entity.HasOne(e => e.ExamSchedule).WithMany().HasForeignKey(e => e.ExamScheduleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Admin).WithMany().HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ClassInfo).WithMany().HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ExamScheduleId, e.SubjectId, e.AdminId, e.ClassId }).IsUnique();
        });

        // TeacherSubject table mapping
        modelBuilder.Entity<TeacherSubject>(entity =>
        {
            entity.ToTable("TeacherSubject");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AdminId).HasColumnName("AdminId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.HasOne(e => e.Admin).WithMany().HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.AdminId, e.SubjectId }).IsUnique();
        });

        // AiAnalysisResult table mapping
        modelBuilder.Entity<AiAnalysisResult>(entity =>
        {
            entity.ToTable("AiAnalysisResult");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId");
            entity.Property(e => e.StudentId).HasColumnName("StudentId");
            entity.Property(e => e.AnalysisResult).HasColumnName("AnalysisResult").HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt");
            entity.HasIndex(e => new { e.ExamScheduleId, e.StudentId }).IsUnique();
        });

        // AiClassAnalysisResult table mapping
        modelBuilder.Entity<AiClassAnalysisResult>(entity =>
        {
            entity.ToTable("AiClassAnalysisResult");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId");
            entity.Property(e => e.ClassInfoId).HasColumnName("ClassInfoId");
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.AnalysisResult).HasColumnName("AnalysisResult").HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt");
            entity.HasIndex(e => new { e.ExamScheduleId, e.ClassInfoId }).IsUnique();
        });

        // AiSubjectAnalysisResult table mapping
        modelBuilder.Entity<AiSubjectAnalysisResult>(entity =>
        {
            entity.ToTable("AiSubjectAnalysisResult");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExamScheduleId).HasColumnName("ExamScheduleId");
            entity.Property(e => e.ClassInfoId).HasColumnName("ClassInfoId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.AnalysisResult).HasColumnName("AnalysisResult").HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt");
            entity.HasIndex(e => new { e.ExamScheduleId, e.ClassInfoId, e.SubjectId }).IsUnique();
        });

        // ScheduleSetting table mapping
        modelBuilder.Entity<ScheduleSetting>(entity =>
        {
            entity.ToTable("ScheduleSetting");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(50).IsRequired();
            entity.Property(e => e.DaysPerWeek).HasColumnName("DaysPerWeek");
            entity.Property(e => e.PeriodsPerDay).HasColumnName("PeriodsPerDay");
            entity.Property(e => e.PeriodDurationMinutes).HasColumnName("PeriodDurationMinutes");
            entity.Property(e => e.StartTime).HasColumnName("StartTime").HasMaxLength(5).IsRequired();
            entity.Property(e => e.BreakMinutes).HasColumnName("BreakMinutes");
            entity.Property(e => e.IsActive).HasColumnName("IsActive");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
        });

        // SchedulePeriod table mapping
        modelBuilder.Entity<SchedulePeriod>(entity =>
        {
            entity.ToTable("SchedulePeriod");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SettingId).HasColumnName("SettingId");
            entity.Property(e => e.PeriodNumber).HasColumnName("PeriodNumber");
            entity.Property(e => e.StartTime).HasColumnName("StartTime").HasMaxLength(5).IsRequired();
            entity.Property(e => e.EndTime).HasColumnName("EndTime").HasMaxLength(5).IsRequired();
            entity.HasOne(e => e.Setting).WithMany().HasForeignKey(e => e.SettingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.SettingId, e.PeriodNumber }).IsUnique();
        });

        // ClassSchedule table mapping
        modelBuilder.Entity<ClassSchedule>(entity =>
        {
            entity.ToTable("ClassSchedule");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ClassId).HasColumnName("ClassId");
            entity.Property(e => e.SemesterId).HasColumnName("SemesterId");
            entity.Property(e => e.DayOfWeek).HasColumnName("DayOfWeek");
            entity.Property(e => e.Period).HasColumnName("Period");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.TeacherId).HasColumnName("TeacherId");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.Property(e => e.UpdateTime).HasColumnName("UpdateTime");
            entity.HasOne(e => e.ClassInfo).WithMany().HasForeignKey(e => e.ClassId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Semester).WithMany().HasForeignKey(e => e.SemesterId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Teacher).WithMany().HasForeignKey(e => e.TeacherId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ClassId, e.SemesterId, e.DayOfWeek, e.Period }).IsUnique();
            entity.HasIndex(e => new { e.TeacherId, e.SemesterId, e.DayOfWeek, e.Period });
        });

        // GradeScheduleConfig table mapping
        modelBuilder.Entity<GradeScheduleConfig>(entity =>
        {
            entity.ToTable("GradeScheduleConfig");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.DaysPerWeek).HasColumnName("DaysPerWeek");
            entity.Property(e => e.PeriodsPerDay).HasColumnName("PeriodsPerDay");
            entity.Property(e => e.IsActive).HasColumnName("IsActive");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.HasOne(e => e.GradeLevel).WithMany().HasForeignKey(e => e.GradeLevelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.GradeLevelId).IsUnique();
        });

        // GradePeriod table mapping
        modelBuilder.Entity<GradePeriod>(entity =>
        {
            entity.ToTable("GradePeriod");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.PeriodNumber).HasColumnName("PeriodNumber");
            entity.Property(e => e.StartTime).HasColumnName("StartTime").HasMaxLength(5).IsRequired();
            entity.Property(e => e.EndTime).HasColumnName("EndTime").HasMaxLength(5).IsRequired();
            entity.Property(e => e.SectionName).HasColumnName("SectionName").HasMaxLength(20);
            entity.HasOne(e => e.GradeLevel).WithMany().HasForeignKey(e => e.GradeLevelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.GradeLevelId, e.PeriodNumber }).IsUnique();
        });

        // GradeSubjectHour table mapping
        modelBuilder.Entity<GradeSubjectHour>(entity =>
        {
            entity.ToTable("GradeSubjectHour");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GradeLevelId).HasColumnName("GradeLevelId");
            entity.Property(e => e.SubjectId).HasColumnName("SubjectId");
            entity.Property(e => e.PeriodsPerWeek).HasColumnName("PeriodsPerWeek");
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.HasOne(e => e.GradeLevel).WithMany().HasForeignKey(e => e.GradeLevelId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.GradeLevelId, e.SubjectId }).IsUnique();
        });

        // RepairRequest table mapping
        modelBuilder.Entity<RepairRequest>(entity =>
        {
            entity.ToTable("RepairRequest");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("Description");
            entity.Property(e => e.Location).HasColumnName("Location").HasMaxLength(200);
            entity.Property(e => e.ContactPhone).HasColumnName("ContactPhone").HasMaxLength(20);
            entity.Property(e => e.Status).HasColumnName("Status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.Property(e => e.PreferredTime).HasColumnName("PreferredTime");
            entity.Property(e => e.CreatedBy).HasColumnName("CreatedBy");
            entity.Property(e => e.CreatorName).HasColumnName("CreatorName").HasMaxLength(50);
            entity.Property(e => e.ProcessTime).HasColumnName("ProcessTime");
            entity.Property(e => e.ProcessedBy).HasColumnName("ProcessedBy");
            entity.Property(e => e.ProcessorName).HasColumnName("ProcessorName").HasMaxLength(50);
            entity.Property(e => e.Remark).HasColumnName("Remark");
        });

        // Survey table mapping
        modelBuilder.Entity<Survey>(entity =>
        {
            entity.ToTable("Survey");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("Description");
            entity.Property(e => e.Status).HasColumnName("Status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedBy).HasColumnName("CreatedBy");
            entity.Property(e => e.CreatorName).HasColumnName("CreatorName").HasMaxLength(50);
            entity.Property(e => e.CreateTime).HasColumnName("CreateTime");
            entity.Property(e => e.UpdateTime).HasColumnName("UpdateTime");
            entity.HasMany(e => e.Questions).WithOne(e => e.Survey).HasForeignKey(e => e.SurveyId).OnDelete(DeleteBehavior.Cascade);
        });

        // SurveyQuestion table mapping
        modelBuilder.Entity<SurveyQuestion>(entity =>
        {
            entity.ToTable("SurveyQuestion");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SurveyId).HasColumnName("SurveyId");
            entity.Property(e => e.SortOrder).HasColumnName("SortOrder");
            entity.Property(e => e.Type).HasColumnName("Type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.IsRequired).HasColumnName("IsRequired");
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(500).IsRequired();
        });

        // SurveyQuestionOption table mapping
        modelBuilder.Entity<SurveyQuestionOption>(entity =>
        {
            entity.ToTable("SurveyQuestionOption");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.QuestionId).HasColumnName("QuestionId");
            entity.Property(e => e.SortOrder).HasColumnName("SortOrder");
            entity.Property(e => e.OptionText).HasColumnName("OptionText").HasMaxLength(200).IsRequired();
            entity.HasOne(e => e.Question).WithMany(e => e.Options).HasForeignKey(e => e.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });

        // SurveySubmission table mapping
        modelBuilder.Entity<SurveySubmission>(entity =>
        {
            entity.ToTable("SurveySubmission");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SurveyId).HasColumnName("SurveyId");
            entity.Property(e => e.SubmittedBy).HasColumnName("SubmittedBy").HasMaxLength(100);
            entity.Property(e => e.SubmitterName).HasColumnName("SubmitterName").HasMaxLength(50);
            entity.Property(e => e.SubmitTime).HasColumnName("SubmitTime");
        });

        // SurveyAnswer table mapping
        modelBuilder.Entity<SurveyAnswer>(entity =>
        {
            entity.ToTable("SurveyAnswer");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubmissionId).HasColumnName("SubmissionId");
            entity.Property(e => e.QuestionId).HasColumnName("QuestionId");
            entity.Property(e => e.AnswerText).HasColumnName("AnswerText");
            entity.Property(e => e.FilePath).HasColumnName("FilePath").HasMaxLength(500);
            entity.HasOne(e => e.Submission).WithMany(e => e.Answers).HasForeignKey(e => e.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
