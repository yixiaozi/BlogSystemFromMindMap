namespace MindmapBlog;

/// <summary>
/// 每次站点生成的资源版本号，用于 CSS/JS/JSON 的 <c>?v=</c> 缓存破坏。
/// </summary>
internal static class AssetVersion
{
    /// <summary>写入 HTML / 查询串的版本戳（UTC，紧凑）。</summary>
    public static string Current { get; private set; } =
        DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    public static void Begin(DateTimeOffset generatedAtUtc) =>
        Current = generatedAtUtc.ToUniversalTime()
            .ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    public static string Bust(string href)
    {
        if (string.IsNullOrEmpty(href))
            return href;
        var sep = href.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return href + sep + "v=" + Current;
    }
}
