using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>写入 <c>data/generation-history.json</c> 的根结构（含上次快照用于差分）。</summary>
public sealed class GenerationHistoryFile
{
    [JsonPropertyName("runs")]
    public List<GenerationRunRecord> Runs { get; set; } = new();

    /// <summary>上一成功生成时的文章指纹，用于下次对比。</summary>
    [JsonPropertyName("lastSnapshot")]
    public Dictionary<string, ArticleFingerDto> LastSnapshot { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>单次网站生成的统计信息。</summary>
public sealed class GenerationRunRecord
{
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [JsonPropertyName("articlesAdded")]
    public int ArticlesAdded { get; set; }

    [JsonPropertyName("articlesRemoved")]
    public int ArticlesRemoved { get; set; }

    [JsonPropertyName("articlesModified")]
    public int ArticlesModified { get; set; }

    /// <summary>新增文章正文（纯文本）总字数。</summary>
    [JsonPropertyName("charsInNewArticles")]
    public int CharsInNewArticles { get; set; }

    /// <summary>被移除文章在移除前的正文总字数。</summary>
    [JsonPropertyName("charsInRemovedArticles")]
    public int CharsInRemovedArticles { get; set; }

    /// <summary>既有文章因编辑而净增加的正文字数。</summary>
    [JsonPropertyName("charsAddedByEdits")]
    public int CharsAddedByEdits { get; set; }

    /// <summary>既有文章因编辑而净减少的正文字数。</summary>
    [JsonPropertyName("charsRemovedByEdits")]
    public int CharsRemovedByEdits { get; set; }

    [JsonPropertyName("totalArticles")]
    public int TotalArticles { get; set; }

    [JsonPropertyName("totalPlainChars")]
    public long TotalPlainChars { get; set; }

    [JsonPropertyName("mindmapFileCount")]
    public int MindmapFileCount { get; set; }

    [JsonPropertyName("distinctBookmarkCount")]
    public int DistinctBookmarkCount { get; set; }

    [JsonPropertyName("articlesWithReminder")]
    public int ArticlesWithReminder { get; set; }

    /// <summary>本趟相对上一快照新增的文章标题（顺序与统计一致）。</summary>
    [JsonPropertyName("addedTitles")]
    public List<string> AddedTitles { get; set; } = new();

    /// <summary>本趟不再发布的文章标题（上一快照中仍存在的条目）。</summary>
    [JsonPropertyName("removedTitles")]
    public List<string> RemovedTitles { get; set; } = new();

    /// <summary>本趟正文有改动的既有文章标题。</summary>
    [JsonPropertyName("modifiedTitles")]
    public List<string> ModifiedTitles { get; set; } = new();
}

/// <summary>单篇文章用于跨次对比的快照（仅存长度与正文哈希，不存全文）。</summary>
public sealed class ArticleFingerDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("plainChars")]
    public int PlainChars { get; set; }

    [JsonPropertyName("contentHash8")]
    public string ContentHash8 { get; set; } = "";

    public ArticleFingerDto()
    {
    }

    public ArticleFingerDto(string title, int plainChars, string contentHash8)
    {
        Title = title;
        PlainChars = plainChars;
        ContentHash8 = contentHash8;
    }
}
