using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MindmapBlog;

internal static class SlugUtility
{
    /// <summary>生成可含中文的短路径/文件名段：空白变 <c>-</c>，去掉 Windows 非法文件名字符及控制符。</summary>
    public static string Create(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "未命名";

        var normalized = raw.Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"\s+", "-");
        normalized = Regex.Replace(normalized, @"[^a-z0-9\u4e00-\u9fff\-]", "");
        normalized = Regex.Replace(normalized, "-{2,}", "-").Trim('-');
        if (normalized.Length > 80)
            normalized = normalized[..80].TrimEnd('-');

        if (string.IsNullOrEmpty(normalized))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return "项-" + Convert.ToHexString(hash[..4]).ToLowerInvariant();
        }

        return normalized;
    }

    /// <summary>路径中的一段（文件夹名、导图节点名等）：优先保留中文，过长则截断。</summary>
    public static string FileNameToken(string segment)
    {
        var slug = Create(segment);
        if (slug.Length > 48)
            slug = slug[..48].TrimEnd('-');
        return slug;
    }

    /// <summary>
    /// 生成可用的 HTML 文件名主干（可含中文）；去掉 \ / : * ? &quot; &lt; &gt; | 与控制字符。
    /// </summary>
    public static string SanitizeFileStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return "";
        var s = stem.Trim();
        s = Regex.Replace(s, @"[\x00-\x1f\\/:*?""<>|]", "");
        s = Regex.Replace(s, @"\s+", "-");
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        if (s.Length > 120)
            s = s[..120].TrimEnd('-');
        return s;
    }

    /// <summary>在全局 <paramref name="used"/> 集合中占用一个不与现有文件冲突的 <c>*.html</c> 名（仅根目录）。</summary>
    public static string AllocateHtmlFile(string preferredStemWithoutExtension, HashSet<string> used)
    {
        var stem = SanitizeFileStem(preferredStemWithoutExtension);
        if (string.IsNullOrEmpty(stem))
            stem = "页面";

        var attempt = stem + ".html";
        if (used.Add(attempt))
            return attempt;

        for (var i = 2; ; i++)
        {
            attempt = $"{stem}-{i}.html";
            if (used.Add(attempt))
                return attempt;
        }
    }

    /// <summary>
    /// 在指定子目录（Web 路径，正斜杠，可为空表示根目录）下占用唯一的 <c>*.html</c>，返回自站点根起的相对路径。
    /// </summary>
    public static string AllocateWebPath(string? parentDirWebFromRoot, string preferredStemWithoutExtension, HashSet<string> used)
    {
        var stem = SanitizeFileStem(preferredStemWithoutExtension);
        if (string.IsNullOrEmpty(stem))
            stem = "页面";

        parentDirWebFromRoot = string.IsNullOrWhiteSpace(parentDirWebFromRoot)
            ? ""
            : parentDirWebFromRoot.Trim().Trim('/').Replace('\\', '/');

        string FullPath(string s)
        {
            return string.IsNullOrEmpty(parentDirWebFromRoot) ? s : $"{parentDirWebFromRoot}/{s}";
        }

        var attempt = FullPath(stem + ".html");
        if (used.Add(attempt))
            return attempt;

        for (var i = 2; ; i++)
        {
            attempt = FullPath($"{stem}-{i}.html");
            if (used.Add(attempt))
                return attempt;
        }
    }
}
