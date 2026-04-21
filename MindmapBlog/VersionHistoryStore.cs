using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindmapBlog;

internal static class VersionHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string GetVersionsDirectory(string outputRoot) =>
        Path.Combine(outputRoot, "data", "versions");

    public static string GetVersionFilePath(string outputRoot, string storageKey) =>
        Path.Combine(GetVersionsDirectory(outputRoot), $"{storageKey}.json");

    public static ArticleVersionDocument? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ArticleVersionDocument>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(string path, ArticleVersionDocument doc)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 根据当前正文更新版本文件：正文未变则不追加新版本，仅同步元数据（html 文件名、路径）。
    /// </summary>
    internal static string NormalizeRelPath(string rel)
    {
        if (string.IsNullOrEmpty(rel))
            return rel;
        return rel.Replace('\\', '/');
    }

    public static ArticleVersionDocument UpdateForArticle(
        BlogArticle article,
        string scanRoot,
        string outputRoot,
        DateTimeOffset generatedAtUtc)
    {
        var key = ArticleIdentity.ComputeStorageKey(scanRoot, article.SourceMmPath, article.ArticleNodeId);
        var path = GetVersionFilePath(outputRoot, key);
        var relMm = NormalizeRelPath(Path.GetRelativePath(scanRoot, article.SourceMmPath));
        var htmlName = ArticleIdentity.ResolveHtmlFileName(article);
        var plain = ArticlePlainText.Build(article);

        var existing = TryLoad(path);
        if (existing == null)
        {
            var first = new ArticleVersionDocument
            {
                StorageKey = key,
                ArticleNodeId = article.ArticleNodeId,
                SourceMmRelativePath = relMm,
                HtmlFileName = htmlName,
                ModifyCount = 0,
                Versions =
                [
                    new VersionEntryDto
                    {
                        GeneratedAtUtc = generatedAtUtc,
                        MindmapModifiedUtc = article.Modified,
                        CharsAdded = plain.Length,
                        CharsRemoved = 0,
                        CharsModifiedEstimate = 0,
                        PlainTextSnapshot = plain,
                        DiffHtmlAgainstPrevious = "",
                    },
                ],
            };
            Save(path, first);
            return first;
        }

        existing.HtmlFileName = htmlName;
        existing.SourceMmRelativePath = relMm;

        var last = existing.Versions[^1];
        var lastNorm = ArticlePlainText.Normalize(last.PlainTextSnapshot);
        if (string.Equals(lastNorm, plain, StringComparison.Ordinal))
        {
            existing.ModifyCount = Math.Max(0, existing.Versions.Count - 1);
            Save(path, existing);
            return existing;
        }

        var (added, removed, modEst) = TextDiffHelper.ComputeCharStats(lastNorm, plain);
        var diffHtml = TextDiffHelper.BuildInlineDiffHtml(lastNorm, plain);

        existing.Versions.Add(new VersionEntryDto
        {
            GeneratedAtUtc = generatedAtUtc,
            MindmapModifiedUtc = article.Modified,
            CharsAdded = added,
            CharsRemoved = removed,
            CharsModifiedEstimate = modEst,
            PlainTextSnapshot = plain,
            DiffHtmlAgainstPrevious = diffHtml,
        });

        existing.ModifyCount = existing.Versions.Count - 1;
        Save(path, existing);
        return existing;
    }
}
