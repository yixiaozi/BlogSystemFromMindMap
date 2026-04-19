using System.Security.Cryptography;
using System.Text;

namespace MindmapBlog;

/// <summary>导图日历列表页文件名与左侧树 details 的稳定 id。</summary>
internal static class BranchNav
{
    /// <summary>左侧树中对应分支的 &lt;details&gt; 的稳定 id（与扫描路径绑定）。</summary>
    public static string FolderBranchDetailsId(string scanRootFullPath, IReadOnlyList<string> folderSegmentsUnderScan)
    {
        var rel = string.Join("/", folderSegmentsUnderScan);
        var payload = "details-folder\x1E" + Path.GetFullPath(scanRootFullPath) + "\x1E" + rel;
        return "nav-det-" + Hash12(payload);
    }

    public static string MmFileDetailsId(string mmFileFullPath)
    {
        var payload = "details-mmfile\x1E" + Path.GetFullPath(mmFileFullPath);
        return "nav-det-" + Hash12(payload);
    }

    /// <param name="structuralPrefixJoined">导图内路径前缀；根节点用空串。</param>
    public static string MmNodeDetailsId(string mmFileFullPath, string structuralPrefixJoined)
    {
        var payload = "details-mmnode\x1E" + Path.GetFullPath(mmFileFullPath) + "\x1E" + structuralPrefixJoined;
        return "nav-det-" + Hash12(payload);
    }

    public static string CalendarYearDetailsId(int year) => $"nav-det-cal-y-{year}";

    public static string CalendarMonthDetailsId(int year, int month) => $"nav-det-cal-ym-{year}-{month:D2}";

    public static string CalendarDayDetailsId(int year, int month, int day) =>
        $"nav-det-cal-ymd-{year}-{month:D2}-{day:D2}";

    private static string Hash12(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash[..6]).ToLowerInvariant();
    }
}
