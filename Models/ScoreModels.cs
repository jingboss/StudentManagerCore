namespace StudentManagerCore.Models;

/// <summary>
/// 成绩录入 - 单条成绩项
/// </summary>
public class ScoreItem
{
    public int StudentId { get; set; }
    public int SubjectId { get; set; }
    public decimal ScoreValue { get; set; }
    public bool IsAbsent { get; set; }
}

/// <summary>
/// 批量导入 - 保存请求
/// </summary>
public class SaveImportRequest
{
    public int ExamScheduleId { get; set; }
    public List<ImportRow> Rows { get; set; } = new();
}

/// <summary>
/// 批量导入 - 单行数据
/// </summary>
public class ImportRow
{
    public int StudentId { get; set; }
    public string StudentNo { get; set; } = "";
    public string Name { get; set; } = "";
    public List<ImportScoreItem> Scores { get; set; } = new();
}

/// <summary>
/// 批量导入 - 单科成绩
/// </summary>
public class ImportScoreItem
{
    public int SubjectId { get; set; }
    public decimal ScoreValue { get; set; }
    public bool IsAbsent { get; set; }
}