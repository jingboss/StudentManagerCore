using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace StudentManagerCore.Controllers;

[Authorize(Roles = "管理员")]
public class BackupController : Controller
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private static readonly object _lock = new();

    // 备份文件存储目录（优先从配置读取，默认项目内 App_Data/backups）
    private string BackupDir
    {
        get
        {
            var configPath = _config.GetValue<string>("Backup:Directory");
            if (!string.IsNullOrEmpty(configPath))
                return configPath;
            return Path.Combine(_env.ContentRootPath, "App_Data", "backups");
        }
    }
    // mysqldump 路径（Linux默认在PATH中，Windows可在配置中指定完整路径）
    private string MysqldumpPath => _config.GetValue<string>("Backup:MysqldumpPath") ?? "mysqldump";
    // mysql 客户端路径
    private string MysqlPath => _config.GetValue<string>("Backup:MysqlPath") ?? "mysql";
    // Web 根目录
    private string WebRoot => _env.WebRootPath;

    public BackupController(IConfiguration config, IWebHostEnvironment env)
    {
        _config = config;
        _env = env;
    }

    /// <summary>解析连接字符串中的各项参数</summary>
    private (string Server, string User, string Password, string Database) ParseConnection()
    {
        var conn = _config.GetConnectionString("DefaultConnection")
                   ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") ?? "";
        var dict = conn.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

        return (
            dict.GetValueOrDefault("Server", "localhost"),
            dict.GetValueOrDefault("User Id", "root"),
            dict.GetValueOrDefault("Password", ""),
            dict.GetValueOrDefault("Database", "StudentManagerDB")
        );
    }

    /// <summary>备份管理首页</summary>
    public IActionResult Index()
    {
        try
        {
            if (!Directory.Exists(BackupDir))
                Directory.CreateDirectory(BackupDir);

            var files = new DirectoryInfo(BackupDir)
                .GetFiles("*.zip")
                .OrderByDescending(f => f.CreationTime)
                .Select(f => new BackupFileInfo
                {
                    FileName = f.Name,
                    Size = f.Length,
                    SizeText = FormatSize(f.Length),
                    CreateTime = f.CreationTime
                })
                .ToList();

            ViewBag.LastBackup = files.FirstOrDefault()?.CreateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "暂无";
            ViewBag.TotalBackups = files.Count;
            return View(files);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(
                Path.Combine(_env.ContentRootPath, "App_Data", "backup_error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n");
            return View(new List<BackupFileInfo>());
        }
    }

    /// <summary>创建备份</summary>
    [HttpPost]
    public IActionResult Create()
    {
        if (!Directory.Exists(BackupDir))
            Directory.CreateDirectory(BackupDir);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var zipPath = Path.Combine(BackupDir, $"backup_{timestamp}.zip");

        lock (_lock)
        {
            try
            {
                // 1. 备份数据库 → 临时 .sql 文件
                var sqlFile = Path.Combine(Path.GetTempPath(), $"backup_temp_{timestamp}.sql");
                try
                {
                    var (server, user, password, db) = ParseConnection();
                    var psi = new ProcessStartInfo
                    {
                        FileName = MysqldumpPath,
                        Arguments = $"-h {server} -u {user} --databases {db} --routines --events",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    psi.EnvironmentVariables["MYSQL_PWD"] = password;

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        var error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(120000);

                        if (proc.ExitCode != 0)
                            throw new Exception($"mysqldump 失败 (退出码 {proc.ExitCode}): {error}");

                        System.IO.File.WriteAllText(sqlFile, output, System.Text.Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"数据库备份失败: {ex.Message}" });
                }

                // 2. 打包到 .zip
                try
                {
                    using var zipStream = new FileStream(zipPath, FileMode.Create);
                    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

                    // 添加数据库备份
                    if (System.IO.File.Exists(sqlFile))
                    {
                        archive.CreateEntryFromFile(sqlFile, "database.sql");
                        try { System.IO.File.Delete(sqlFile); } catch { }
                    }

                    // 添加配置文件
                    var configPath = Path.Combine(_env.ContentRootPath, "appsettings.json");
                    if (System.IO.File.Exists(configPath))
                        archive.CreateEntryFromFile(configPath, "config/appsettings.json");

                    // 添加上传资源
                    var uploadsDir = Path.Combine(WebRoot, "..", "..", "uploads", "imge");
                    if (!Directory.Exists(uploadsDir))
                        uploadsDir = Path.Combine(WebRoot, "uploads", "imge");
                    if (Directory.Exists(uploadsDir))
                    {
                        var files = Directory.GetFiles(uploadsDir);
                        foreach (var f in files)
                        {
                            var relPath = "uploads/" + Path.GetFileName(f);
                            archive.CreateEntryFromFile(f, relPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return Json(new { success = false, message = $"打包失败: {ex.Message}" });
                }

                return Json(new { success = true, message = "备份创建成功", fileName = $"backup_{timestamp}.zip" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"备份失败: {ex.Message}" });
            }
        }
    }

    /// <summary>下载备份文件</summary>
    [HttpGet]
    public IActionResult Download(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return NotFound();

        // 防止路径穿越
        var safeName = Path.GetFileName(fileName);
        var filePath = Path.Combine(BackupDir, safeName);

        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var contentType = safeName.EndsWith(".zip") ? "application/zip" : "application/octet-stream";
        return PhysicalFile(filePath, contentType, safeName);
    }

    /// <summary>删除备份文件</summary>
    [HttpPost]
    public IActionResult Delete(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Json(new { success = false, message = "文件名不能为空" });

        var safeName = Path.GetFileName(fileName);
        var filePath = Path.Combine(BackupDir, safeName);

        if (System.IO.File.Exists(filePath))
        {
            System.IO.File.Delete(filePath);
            return Json(new { success = true, message = "已删除" });
        }

        return Json(new { success = false, message = "文件不存在" });
    }

    /// <summary>还原数据库（从备份ZIP中提取 SQL 并导入）</summary>
    [HttpPost]
    public IActionResult Restore(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return Json(new { success = false, message = "文件名不能为空" });

        var safeName = Path.GetFileName(fileName);
        var zipPath = Path.Combine(BackupDir, safeName);

        if (!System.IO.File.Exists(zipPath))
            return Json(new { success = false, message = "备份文件不存在" });

        try
        {
            string? sqlContent = null;

            // 从 ZIP 中提取 database.sql
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                var entry = archive.GetEntry("database.sql");
                if (entry == null)
                    return Json(new { success = false, message = "备份文件中未找到 database.sql" });

                using var reader = new StreamReader(entry.Open());
                sqlContent = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(sqlContent))
                return Json(new { success = false, message = "数据库备份内容为空" });

            // 写入临时文件
            var tempSql = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid():N}.sql");
            System.IO.File.WriteAllText(tempSql, sqlContent, System.Text.Encoding.UTF8);

            try
            {
                var (server, user, password, db) = ParseConnection();
                var psi = new ProcessStartInfo
                {
                    FileName = MysqlPath,
                    Arguments = $"-h {server} -u {user} {db} < \"{tempSql}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                psi.EnvironmentVariables["MYSQL_PWD"] = password;

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(300000); // 5 分钟超时

                    if (proc.ExitCode != 0)
                        throw new Exception($"还原失败 (退出码 {proc.ExitCode}): {error}");
                }

                return Json(new { success = true, message = "数据库还原成功！部分表可能需重新登录查看效果。" });
            }
            finally
            {
                try { System.IO.File.Delete(tempSql); } catch { }
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"还原失败: {ex.Message}" });
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

public class BackupFileInfo
{
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string SizeText { get; set; } = "";
    public DateTime CreateTime { get; set; }
}
