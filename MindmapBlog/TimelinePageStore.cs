using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>时间轴列表页 JSON（按页面路径哈希分文件存放）。</summary>
internal static class TimelinePageStore
{
    public const string DataDirWebPath = "data/timelines";
    public const string ManifestWebPath = "data/timeline-manifest.json";

    private static readonly Dictionary<string, string> Manifest =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Reset() => Manifest.Clear();

    public static string DataFileWebPath(string pageWebPath)
    {
        var normalized = NormalizeWeb(pageWebPath);
        var payload = "timeline-page\x1E" + normalized;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"{DataDirWebPath}/{Convert.ToHexString(hash[..6]).ToLowerInvariant()}.json";
    }

    public static void WritePage(string outputRoot, TimelinePageFile page)
    {
        var webPath = DataFileWebPath(page.PagePath);
        Manifest[page.PagePath] = webPath;
        var local = SitePathHelper.CombineLocal(outputRoot, webPath);
        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(local, JsonSerializer.Serialize(page, JsonOptions));
    }

    public static void FlushManifest(string outputRoot)
    {
        var local = SitePathHelper.CombineLocal(outputRoot, ManifestWebPath);
        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string>(Manifest, StringComparer.Ordinal),
            JsonOptions);
        File.WriteAllText(local, json);
        Console.WriteLine($"已写入时间轴清单：{ManifestWebPath}（{Manifest.Count} 页）");
    }

    public static TimelinePageFile BuildPage(
        string pageWebPath,
        string documentTitle,
        string heading,
        string? subLine,
        string? leadHtml,
        string wrapperClass,
        IReadOnlyList<BlogArticle> articles,
        string scanRoot,
        SiteFileNames names,
        IReadOnlyDictionary<string, ArticleVersionDocument>? versionDocs,
        bool enableSortTabs,
        string timeSource = "published")
    {
        var items = BuildItems(articles, scanRoot, versionDocs, names, timeSource);
        return new TimelinePageFile
        {
            PagePath = NormalizeWeb(pageWebPath),
            DocumentTitle = documentTitle,
            Heading = heading,
            SubLine = subLine,
            LeadHtml = leadHtml,
            WrapperClass = wrapperClass,
            EnableSortTabs = enableSortTabs,
            TimeSource = timeSource,
            Items = items,
        };
    }

    public static List<TimelineItemDto> BuildItems(
        IReadOnlyList<BlogArticle> articles,
        string scanRoot,
        IReadOnlyDictionary<string, ArticleVersionDocument>? versionDocs,
        SiteFileNames? names = null,
        string timeSource = "published")
    {
        var list = new List<TimelineItemDto>();
        foreach (var art in articles)
        {
            var versionDoc = TryGetVersionDoc(versionDocs, scanRoot, art);
            var published = GetPublishedAt(art, versionDoc).ToLocalTime();
            var modified = art.Modified.ToLocalTime();
            if (string.Equals(timeSource, "reminder", StringComparison.OrdinalIgnoreCase) && art.ReminderAt.HasValue)
            {
                published = art.ReminderAt.Value.ToLocalTime();
                modified = published;
            }
            var bookmarkPages = new Dictionary<string, string>(StringComparer.Ordinal);
            if (names != null)
            {
                foreach (var bm in art.Bookmarks)
                    bookmarkPages[bm] = names.TagPageFile(bm);
            }

            list.Add(new TimelineItemDto
            {
                Href = art.HtmlFileName.Replace('\\', '/'),
                Title = art.Title,
                Published = published.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Modified = modified.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Bookmarks = art.Bookmarks.ToList(),
                BookmarkPages = bookmarkPages,
                Excerpt = BuildExcerpt(art),
            });
        }

        return list;
    }

    private static DateTimeOffset GetPublishedAt(BlogArticle article, ArticleVersionDocument? versionDoc) =>
        versionDoc?.Versions.Count > 0 ? versionDoc.Versions[0].GeneratedAtUtc : article.Modified;

    private static ArticleVersionDocument? TryGetVersionDoc(
        IReadOnlyDictionary<string, ArticleVersionDocument>? versionDocs,
        string scanRoot,
        BlogArticle article)
    {
        if (versionDocs == null)
            return null;
        var key = ArticleIdentity.ComputeStorageKey(scanRoot, article.SourceMmPath, article.ArticleNodeId);
        return versionDocs.TryGetValue(key, out var doc) ? doc : null;
    }

    private static string BuildExcerpt(BlogArticle article)
    {
        var first = article.Blocks.OfType<ParagraphBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(first))
            return "";
        first = first.Trim();
        return first.Length <= 180 ? first : first[..180] + "…";
    }

    private static string NormalizeWeb(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "index.html";
        var s = path.Trim().Replace('\\', '/');
        while (s.Length > 0 && s[0] == '/')
            s = s[1..];
        return string.IsNullOrEmpty(s) ? "index.html" : s;
    }
}

/// <summary>词频页 JSON。</summary>
internal static class WordFrequencyPageStore
{
    public const string DataWebPath = "data/word-frequency.json";
    public const string TermsDataWebPath = "data/word-frequency-terms.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(string outputRoot, WordFrequencyPageFile data)
    {
        var local = SitePathHelper.CombineLocal(outputRoot, DataWebPath);
        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(local, JsonSerializer.Serialize(data, JsonOptions));
    }

    public static void WriteTermsIndex(string outputRoot, string wordFrequencyPageWebPath, WordFrequencyResult stats)
    {
        var data = new WordFrequencyTermsFile
        {
            PageWebPath = wordFrequencyPageWebPath.Replace('\\', '/'),
            Terms = stats.TopTerms.Select(t => t.Token).ToList(),
        };
        var local = SitePathHelper.CombineLocal(outputRoot, TermsDataWebPath);
        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(local, JsonSerializer.Serialize(data, JsonOptions));
    }

    public static WordFrequencyPageFile Build(
        WordFrequencyResult stats,
        IReadOnlyDictionary<string, List<WordFrequencyArticleHit>> hitsByTerm)
    {
        return new WordFrequencyPageFile
        {
            ArticleCount = stats.DocumentCount,
            TotalTokenOccurrences = stats.TotalTokenOccurrences,
            UniqueTokens = stats.UniqueTokens,
            MinCount = stats.MinCount,
            MaxCount = stats.MaxCount,
            TopTerms = stats.TopTerms
                .Select(t => new WordFrequencyTermDto { Token = t.Token, Count = t.Count })
                .ToList(),
            HitsByTerm = hitsByTerm.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(h => new WordFrequencyHitDto
                {
                    Title = h.Title,
                    Href = h.HtmlFileName.Replace('\\', '/'),
                    Snippets = h.Snippets.ToList(),
                }).ToList(),
                StringComparer.Ordinal),
        };
    }
}
