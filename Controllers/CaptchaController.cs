using Microsoft.AspNetCore.Mvc;

namespace StudentManagerCore.Controllers;

public class CaptchaController : Controller
{
    private const string SessionKey = "CaptchaCode";

    /// <summary>
    /// 生成 4 位数字验证码 SVG 图片
    /// </summary>
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        var random = new Random();
        var code = random.Next(1000, 9999).ToString(); // 4位随机数字

        // 存入 Session
        HttpContext.Session.SetString(SessionKey, code);

        // 生成 SVG
        var svg = GenerateCaptchaSvg(code, random);
        return Content(svg, "image/svg+xml");
    }

    private static string GenerateCaptchaSvg(string code, Random random)
    {
        var width = 140;
        var height = 48;
        var chars = code.ToCharArray();
        var charCount = chars.Length;

        // 随机颜色（深色系，确保可读）
        string RandomColor() =>
            $"rgb({random.Next(20, 100)},{random.Next(20, 100)},{random.Next(20, 100)})";

        // 背景
        var bgR = random.Next(210, 245);
        var bgG = random.Next(210, 245);
        var bgB = random.Next(210, 245);

        var sb = new System.Text.StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.Append($"<rect width=\"{width}\" height=\"{height}\" fill=\"rgb({bgR},{bgG},{bgB})\" rx=\"6\"/>");

        // 干扰线（3-5条）
        var lineCount = random.Next(3, 6);
        for (int i = 0; i < lineCount; i++)
        {
            var x1 = random.Next(0, width / 3);
            var y1 = random.Next(0, height);
            var x2 = random.Next(width * 2 / 3, width);
            var y2 = random.Next(0, height);
            sb.Append($"<line x1=\"{x1}\" y1=\"{y1}\" x2=\"{x2}\" y2=\"{y2}\" stroke=\"{RandomColor()}\" stroke-width=\"{random.Next(1, 3)}\" opacity=\"0.5\"/>");
        }

        // 噪点（30-60个）
        var dotCount = random.Next(30, 61);
        for (int i = 0; i < dotCount; i++)
        {
            var cx = random.Next(0, width);
            var cy = random.Next(0, height);
            var r = random.Next(1, 3);
            sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"{RandomColor()}\" opacity=\"0.4\"/>");
        }

        // 数字（每个字符随机位置、旋转、大小、颜色）
        for (int i = 0; i < charCount; i++)
        {
            var fontSize = random.Next(26, 34);
            var x = 12 + i * (width - 24) / charCount + random.Next(-5, 6);
            var y = height / 2 + random.Next(-4, 5);
            var rotate = random.Next(-25, 26);
            var offsetX = random.Next(-3, 4);
            sb.Append($"<text x=\"{x}\" y=\"{y + 6}\" font-size=\"{fontSize}\" font-family=\"Arial,sans-serif\" font-weight=\"bold\" fill=\"{RandomColor()}\" transform=\"rotate({rotate},{x + offsetX},{y})\">{chars[i]}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// 验证验证码（供 AccountController 调用）
    /// </summary>
    public static bool Validate(string? userInput, HttpContext httpContext)
    {
        if (string.IsNullOrEmpty(userInput)) return false;
        var stored = httpContext.Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(stored)) return false;

        // 验证后清除，防止重复使用
        httpContext.Session.Remove(SessionKey);
        return string.Equals(stored.Trim(), userInput.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
