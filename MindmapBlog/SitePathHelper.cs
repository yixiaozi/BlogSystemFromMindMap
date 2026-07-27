using System.Text;

namespace MindmapBlog;

/// <summary>
/// 站点输出路径（相对站点根，正斜杠）与页面间相对链接。
/// </summary>
internal static class SitePathHelper
{
    /// <summary>
    /// 导图文件所在目录相对扫描根的 Web 路径（用于文章与导图分支列表输出目录）。
    /// 各段经 <see cref="SlugUtility.FileNameToken"/>：小写、空白→连字符，避免 Linux 大小写/空格路径问题。
    /// </summary>
    public static string GetMmParentWebDir(string scanRootFull, string mmFullPath)
    {
        var scan = Path.GetFullPath(scanRootFull);
        var mmDir = Path.GetDirectoryName(Path.GetFullPath(mmFullPath));
        if (string.IsNullOrEmpty(mmDir))
            return "";
        var rel = Path.GetRelativePath(scan, mmDir);
        if (rel.StartsWith("..", StringComparison.Ordinal) || rel == ".")
            return "";
        var segs = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        return FolderSegmentsToWebDir(segs);
    }

    /// <summary>磁盘文件夹导航路径 → 输出子目录（各段经 <see cref="SlugUtility.FileNameToken"/> 风格安全化）。</summary>
    public static string FolderSegmentsToWebDir(IReadOnlyList<string> pathSegs)
    {
        if (pathSegs.Count == 0)
            return "";
        return string.Join("/", pathSegs.Select(SlugUtility.FileNameToken));
    }

    /// <summary>将 Web 路径转为写到磁盘上的绝对路径。</summary>
    public static string CombineLocal(string outputRootFull, string webPath)
    {
        webPath = webPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(outputRootFull, webPath));
    }

    /// <summary>
    /// 从当前页（相对站点根的 Web 路径，如 <c>日程/规划/文.html</c>）到目标页的相对 URL。
    /// 仅按正斜杠解析，不依赖 <see cref="Path"/>（在 Windows 上对 <c>foo/bar.html</c> 式路径更可靠）。
    /// </summary>
    public static string RelFromTo(string? currentPageWebPath, string targetWebPathFromRoot)
    {
        currentPageWebPath = NormalizeWeb(currentPageWebPath);
        if (string.IsNullOrEmpty(currentPageWebPath))
            currentPageWebPath = "index.html";

        targetWebPathFromRoot = NormalizeWeb(targetWebPathFromRoot);
        if (string.IsNullOrEmpty(targetWebPathFromRoot))
            targetWebPathFromRoot = "index.html";

        var fromParts = WebPathDirSegments(currentPageWebPath);
        var toParts = WebPathDirSegments(targetWebPathFromRoot);
        var toFile = WebPathFileName(targetWebPathFromRoot);
        if (string.IsNullOrEmpty(toFile))
            toFile = "index.html";

        var i = 0;
        while (i < fromParts.Count && i < toParts.Count &&
               string.Equals(fromParts[i], toParts[i], StringComparison.OrdinalIgnoreCase))
            i++;

        var up = fromParts.Count - i;
        var sb = new StringBuilder();
        for (var u = 0; u < up; u++)
            sb.Append("../");

        for (var j = i; j < toParts.Count; j++)
        {
            sb.Append(toParts[j]);
            sb.Append('/');
        }

        sb.Append(toFile);
        return sb.ToString();
    }

    private static string WebPathFileName(string webPath)
    {
        if (string.IsNullOrEmpty(webPath))
            return "";
        var i = webPath.LastIndexOf('/');
        return i < 0 ? webPath : webPath[(i + 1)..];
    }

    private static List<string> WebPathDirSegments(string webPathToFile)
    {
        if (string.IsNullOrEmpty(webPathToFile))
            return new List<string>();
        var i = webPathToFile.LastIndexOf('/');
        if (i <= 0)
            return new List<string>();
        var dir = webPathToFile[..i];
        return dir.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static string NormalizeWeb(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        var s = path.Trim().Replace('\\', '/');
        while (s.Length > 0 && s[0] == '/')
            s = s[1..];
        return s;
    }
}
