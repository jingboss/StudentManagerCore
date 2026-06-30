using StudentManagerCore.Data;
using StudentManagerCore.Models;
using System.Security.Claims;

namespace StudentManagerCore.Services;

/// <summary>
/// 统一审计日志服务 — 所有敏感操作通过此服务记录，
/// 自动捕获操作人、角色、IP地址、User-Agent 等信息。
/// </summary>
public class AuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>记录操作日志</summary>
    /// <param name="actionType">操作类型：添加/编辑/删除/导入/登录/修改密码等</param>
    /// <param name="detail">详细描述</param>
    /// <param name="targetName">目标名称（学生姓名/教师姓名/公告标题等）</param>
    /// <param name="targetNo">目标编号（学号/工号等）</param>
    public async Task LogAsync(string actionType, string detail,
        string? targetName = null, string? targetNo = null)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;

        var operatorName = user?.FindFirst("RealName")?.Value
            ?? user?.Identity?.Name
            ?? "系统";

        var operatorRole = user?.FindFirst(ClaimTypes.Role)?.Value?.Trim()
            ?? "系统";

        // 获取客户端IP（支持反向代理 X-Forwarded-For）
        var ipAddress = "未知";
        if (httpContext?.Connection.RemoteIpAddress != null)
        {
            ipAddress = httpContext.Connection.RemoteIpAddress.ToString();
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
                ipAddress = forwardedFor.Split(',')[0].Trim();
        }

        var log = new OperationLog
        {
            OperatorName = operatorName,
            OperatorRole = operatorRole,
            ActionType = actionType,
            TargetNo = targetNo,
            TargetName = targetName,
            Detail = detail,
            IpAddress = ipAddress,
            CreateTime = DateTime.Now
        };

        _db.OperationLogs.Add(log);
        await _db.SaveChangesAsync();
    }
}
