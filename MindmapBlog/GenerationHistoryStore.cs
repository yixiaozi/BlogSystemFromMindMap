using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindmapBlog;

internal static class GenerationHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string HistoryFilePath(string outputRoot) =>
        Path.Combine(outputRoot, "data", "generation-history.json");

    public static GenerationHistoryFile LoadOrEmpty(string path)
    {
        if (!File.Exists(path))
            return new GenerationHistoryFile();
        try
        {
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<GenerationHistoryFile>(json, JsonOptions);
            return doc ?? new GenerationHistoryFile();
        }
        catch
        {
            return new GenerationHistoryFile();
        }
    }

    public static void Save(string path, GenerationHistoryFile file)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(file, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static Dictionary<string, ArticleFingerDto> BuildFingerprints(
        IReadOnlyList<BlogArticle> articles,
        string scanRootFullPath)
    {
        var dict = new Dictionary<string, ArticleFingerDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in articles)
        {
            var key = ArticleIdentity.ComputeStorageKey(scanRootFullPath, a.SourceMmPath, a.ArticleNodeId);
            var plain = ArticlePlainText.Build(a);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plain));
            var h8 = Convert.ToHexString(hash[..8]).ToLowerInvariant();
            dict[key] = new ArticleFingerDto(a.Title, plain.Length, h8);
        }

        return dict;
    }

    public static GenerationRunRecord BuildRunRecord(
        Dictionary<string, ArticleFingerDto>? previous,
        Dictionary<string, ArticleFingerDto> current,
        IReadOnlyList<BlogArticle> sortedArticles,
        DateTimeOffset generatedAtUtc)
    {
        previous ??= new Dictionary<string, ArticleFingerDto>(StringComparer.OrdinalIgnoreCase);

        var prevKeys = previous.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currKeys = current.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var addedKeys = currKeys.Where(k => !prevKeys.Contains(k)).ToList();
        var removedKeys = prevKeys.Where(k => !currKeys.Contains(k)).ToList();

        var charsNew = addedKeys.Sum(k => current[k].PlainChars);
        var charsRem = removedKeys.Sum(k => previous[k].PlainChars);

        int modCount = 0;
        var addByEdit = 0;
        var remByEdit = 0;
        foreach (var k in currKeys)
        {
            if (!prevKeys.Contains(k))
                continue;
            if (string.Equals(previous[k].ContentHash8, current[k].ContentHash8, StringComparison.Ordinal))
                continue;
            modCount++;
            var d = current[k].PlainChars - previous[k].PlainChars;
            if (d > 0)
                addByEdit += d;
            else if (d < 0)
                remByEdit += -d;
        }

        var bmCount = HtmlLayout.CountBookmarks(sortedArticles).Count;

        var modifiedKeys = currKeys.Where(k =>
            prevKeys.Contains(k) &&
            !string.Equals(previous[k].ContentHash8, current[k].ContentHash8, StringComparison.Ordinal)).ToList();

        var record = new GenerationRunRecord
        {
            GeneratedAtUtc = generatedAtUtc,
            ArticlesAdded = addedKeys.Count,
            ArticlesRemoved = removedKeys.Count,
            ArticlesModified = modCount,
            CharsInNewArticles = charsNew,
            CharsInRemovedArticles = charsRem,
            CharsAddedByEdits = addByEdit,
            CharsRemovedByEdits = remByEdit,
            TotalArticles = sortedArticles.Count,
            TotalPlainChars = current.Values.Sum(v => (long)v.PlainChars),
            MindmapFileCount = sortedArticles.Select(a => a.SourceMmPath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            DistinctBookmarkCount = bmCount,
            ArticlesWithReminder = sortedArticles.Count(a => a.ReminderAt.HasValue),
            AddedTitles = addedKeys.Select(k => current[k].Title).ToList(),
            RemovedTitles = removedKeys.Select(k => previous[k].Title).ToList(),
            ModifiedTitles = modifiedKeys.Select(k => current[k].Title).ToList(),
        };

        return record;
    }
}
