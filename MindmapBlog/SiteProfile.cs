namespace MindmapBlog;

/// <summary>站点展示文案（可由导图「变量」节点覆盖）与头像发布。</summary>
internal static class SiteProfile
{
    public const string DefaultBlogTitle = "思维导图博客";
    public const string DefaultSignature = "以增加选项为己任，无视强者唤醒弱者，一个透明，纯粹，善良的人";
    public const string DefaultAboutBody = "欢迎来到本站。以上是我在此处展示的签名与态度；站内文章与导图仅代表学习与记录。";

    public static string BlogTitle { get; private set; } = DefaultBlogTitle;
    public static string Signature { get; private set; } = DefaultSignature;
    public static string AboutBody { get; private set; } = DefaultAboutBody;
    public static string? AboutBodyHtml { get; private set; }

    /// <summary>重置为默认值，再应用从导图解析到的变量（未提供的项保持默认）。</summary>
    public static void Apply(SiteVariables? variables)
    {
        BlogTitle = DefaultBlogTitle;
        Signature = DefaultSignature;
        AboutBody = DefaultAboutBody;
        AboutBodyHtml = null;

        if (variables == null)
            return;

        if (!string.IsNullOrWhiteSpace(variables.BlogTitle))
            BlogTitle = variables.BlogTitle.Trim();
        if (!string.IsNullOrWhiteSpace(variables.Signature))
            Signature = variables.Signature.Trim();
        if (!string.IsNullOrWhiteSpace(variables.AboutBodyHtml))
        {
            AboutBodyHtml = variables.AboutBodyHtml.Trim();
            AboutBody = string.IsNullOrWhiteSpace(variables.AboutBody)
                ? MarkdownRenderer.HtmlToPlain(AboutBodyHtml)
                : variables.AboutBody.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(variables.AboutBody))
        {
            AboutBody = variables.AboutBody.Trim();
            if (MarkdownRenderer.LooksLikeMarkdown(AboutBody))
                AboutBodyHtml = MarkdownRenderer.ToHtml(AboutBody);
        }
    }

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif",
        };

    /// <summary>
    /// 在若干根目录下查找头像文件并复制到 <paramref name="mediaDir"/>，
    /// 返回站点相对路径（如 <c>media/site-avatar.jpg</c>）；找不到则返回 null。
    /// </summary>
    public static string? TryPublishAvatar(string scanRoot, string mediaDir)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            roots.Add(Path.GetFullPath(scanRoot));
            roots.Add(Path.GetFullPath(Environment.CurrentDirectory));
        }
        catch
        {
            // ignore invalid paths
        }

        foreach (var root in roots)
        {
            var hit = TryPickUnderRoot(root);
            if (hit != null)
                return CopyToMedia(hit, mediaDir);
        }

        // 兼容误放在生成目录 dist/data 下的头像（不推荐，但可避免「有文件却看不到」）
        var distData = Path.Combine(Environment.CurrentDirectory, "dist", "data");
        if (Directory.Exists(distData))
        {
            var hit = PickFirstImage(distData, preferNameContains: "Avator");
            hit ??= PickFirstImage(distData, preferNameContains: null);
            if (hit != null)
                return CopyToMedia(hit, mediaDir);
        }

        return null;
    }

    private static string? TryPickUnderRoot(string rootFull)
    {
        foreach (var dataSeg in new[] { "Data", "data" })
        {
            var dataPath = Path.Combine(rootFull, dataSeg);
            if (!Directory.Exists(dataPath))
                continue;

            // Data/Avator.jpg、Data/avatar.png …（单层文件）
            foreach (var stem in new[] { "Avator", "avatar", "Avatar" })
            {
                foreach (var extDot in ImageExtensions)
                {
                    var path = Path.Combine(dataPath, stem + extDot);
                    if (File.Exists(path))
                        return path;
                }
            }

            // Data/Avator/*、Data/Avatar/*
            foreach (var folder in new[] { "Avator", "Avatar" })
            {
                var dir = Path.Combine(dataPath, folder);
                if (!Directory.Exists(dir))
                    continue;

                foreach (var stem in new[] { "Avator", "avatar", "Avatar" })
                {
                    foreach (var extDot in ImageExtensions)
                    {
                        var path = Path.Combine(dir, stem + extDot);
                        if (File.Exists(path))
                            return path;
                    }
                }

                var fromDir = PickFirstImage(dir, preferNameContains: null);
                if (fromDir != null)
                    return fromDir;
            }
        }

        return null;
    }

    private static string? PickFirstImage(string dir, string? preferNameContains)
    {
        var files = Directory.GetFiles(dir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
            .ToList();
        if (files.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(preferNameContains))
        {
            var pref = files
                .Where(f => Path.GetFileName(f).Contains(preferNameContains, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (pref != null)
                return pref;
        }

        return files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string CopyToMedia(string sourceFile, string mediaDir)
    {
        var ext = Path.GetExtension(sourceFile);
        if (string.IsNullOrEmpty(ext))
            ext = ".jpg";

        var destName = "site-avatar" + ext.ToLowerInvariant();
        Directory.CreateDirectory(mediaDir);
        File.Copy(sourceFile, Path.Combine(mediaDir, destName), overwrite: true);
        return "media/" + destName.Replace('\\', '/');
    }
}
