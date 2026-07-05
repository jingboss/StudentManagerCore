using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagerCore.Data;
using StudentManagerCore.Models;

namespace StudentManagerCore.Controllers;

[Authorize]
public class SiteSettingsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public SiteSettingsController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    /// <summary>服务端校验图片文件头（Magic Number），防止恶意文件上传</summary>
    private static bool IsValidImageFile(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            var header = new byte[8];
            if (stream.Read(header, 0, 8) < 8) return false;
            // JPEG: FF D8 FF
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return true;
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E &&
                header[3] == 0x47 && header[4] == 0x0D && header[5] == 0x0A &&
                header[6] == 0x1A && header[7] == 0x0A) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>获取外部上传目录</summary>
    private string GetUploadsDir()
    {
        return Path.Combine(_env.ContentRootPath, "uploads", "imge");
    }

    public async Task<IActionResult> Index()
    {
        var configs = await _db.SiteConfigs.ToListAsync();
        var dict = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue ?? "");
        return View(dict);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string ConfigKey, string ConfigValue)
    {
        if (string.IsNullOrWhiteSpace(ConfigKey))
            return Json(new { success = false, message = "参数错误" });

        var config = await _db.SiteConfigs.FindAsync(ConfigKey);
        if (config == null)
        {
            _db.SiteConfigs.Add(new SiteConfig { ConfigKey = ConfigKey, ConfigValue = ConfigValue });
        }
        else
        {
            config.ConfigValue = ConfigValue;
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UploadBackground(IFormFile? file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "请选择文件" });

            if (file.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "图片不能超过5MB" });

            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return Json(new { success = false, message = "仅支持JPG、JPEG、PNG格式" });

            // 服务端校验文件头（Magic Number）
            if (!IsValidImageFile(file))
                return Json(new { success = false, message = "文件格式无效，请上传真实图片文件" });

            var uploadsDir = GetUploadsDir();
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var fileName = $"login_bg{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Save to SiteConfig
            var config = await _db.SiteConfigs.FindAsync("BackgroundImage");
            if (config == null)
            {
                _db.SiteConfigs.Add(new SiteConfig { ConfigKey = "BackgroundImage", ConfigValue = $"/uploads/imge/{fileName}" });
            }
            else
            {
                config.ConfigValue = $"/uploads/imge/{fileName}";
            }
            await _db.SaveChangesAsync();

            return Json(new { success = true, path = $"/uploads/imge/{fileName}" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "上传失败：" + ex.Message });
        }
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UploadLogo(IFormFile? file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "请选择文件" });

            if (file.Length > 5 * 1024 * 1024)
                return Json(new { success = false, message = "图片不能超过5MB" });

            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
                return Json(new { success = false, message = "仅支持JPG、JPEG、PNG格式" });

            // 服务端校验文件头（Magic Number）
            if (!IsValidImageFile(file))
                return Json(new { success = false, message = "文件格式无效，请上传真实图片文件" });

            var uploadsDir = GetUploadsDir();
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var fileName = $"logo{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Save to SiteConfig
            var config = await _db.SiteConfigs.FindAsync("Logo");
            if (config == null)
            {
                _db.SiteConfigs.Add(new SiteConfig { ConfigKey = "Logo", ConfigValue = $"/uploads/imge/{fileName}" });
            }
            else
            {
                config.ConfigValue = $"/uploads/imge/{fileName}";
            }
            await _db.SaveChangesAsync();

            return Json(new { success = true, path = $"/uploads/imge/{fileName}" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "上传失败：" + ex.Message });
        }
    }
}
