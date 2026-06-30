using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StudentManagerCore.Models;

/// <summary>问卷主表</summary>
public class Survey
{
    [Key]
    public int Id { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>草稿/发布/关闭</summary>
    [Required, StringLength(20)]
    public string Status { get; set; } = "草稿";

    public int CreatedBy { get; set; }

    [StringLength(50)]
    public string? CreatorName { get; set; }

    public DateTime CreateTime { get; set; } = DateTime.Now;

    public DateTime? UpdateTime { get; set; }

    public List<SurveyQuestion> Questions { get; set; } = new();
}

/// <summary>题目表</summary>
public class SurveyQuestion
{
    [Key]
    public int Id { get; set; }

    public int SurveyId { get; set; }

    public int SortOrder { get; set; }

    /// <summary>题型：单行文本/姓名/手机号/身份证/单选/多选/文件上传/图片上传</summary>
    [Required, StringLength(20)]
    public string Type { get; set; } = "单行文本";

    public bool IsRequired { get; set; }

    [Required, StringLength(500)]
    public string Title { get; set; } = "";

    [ForeignKey("SurveyId")]
    public Survey? Survey { get; set; }

    public List<SurveyQuestionOption> Options { get; set; } = new();
}

/// <summary>选项表（单选/多选用）</summary>
public class SurveyQuestionOption
{
    [Key]
    public int Id { get; set; }

    public int QuestionId { get; set; }

    public int SortOrder { get; set; }

    [Required, StringLength(200)]
    public string OptionText { get; set; } = "";

    [ForeignKey("QuestionId")]
    public SurveyQuestion? Question { get; set; }
}

/// <summary>答卷表</summary>
public class SurveySubmission
{
    [Key]
    public int Id { get; set; }

    public int SurveyId { get; set; }

    [StringLength(100)]
    public string? SubmittedBy { get; set; }

    [StringLength(50)]
    public string? SubmitterName { get; set; }

    public DateTime SubmitTime { get; set; } = DateTime.Now;

    [ForeignKey("SurveyId")]
    public Survey? Survey { get; set; }

    public List<SurveyAnswer> Answers { get; set; } = new();
}

/// <summary>答案表</summary>
public class SurveyAnswer
{
    [Key]
    public int Id { get; set; }

    public int SubmissionId { get; set; }

    public int QuestionId { get; set; }

    /// <summary>文本答案（单选存选项值，多选存JSON数组）</summary>
    public string? AnswerText { get; set; }

    /// <summary>上传文件路径</summary>
    [StringLength(500)]
    public string? FilePath { get; set; }

    [ForeignKey("SubmissionId")]
    public SurveySubmission? Submission { get; set; }
}
