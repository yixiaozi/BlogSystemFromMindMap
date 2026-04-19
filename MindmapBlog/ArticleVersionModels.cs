using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>单篇文章版本记录（写入 data/versions/*.json）。</summary>
public sealed class ArticleVersionDocument
{
    [JsonPropertyName("storageKey")]
    public required string StorageKey { get; init; }

    [JsonPropertyName("articleNodeId")]
    public required string ArticleNodeId { get; init; }

    [JsonPropertyName("sourceMmRelativePath")]
    public required string SourceMmRelativePath { get; set; }

    [JsonPropertyName("htmlFileName")]
    public required string HtmlFileName { get; set; }

    [JsonPropertyName("modifyCount")]
    public int ModifyCount { get; set; }

    /// <summary>按时间顺序：第 0 条为首次入库快照。</summary>
    [JsonPropertyName("versions")]
    public List<VersionEntryDto> Versions { get; set; } = new();
}

public sealed class VersionEntryDto
{
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; set; }

    [JsonPropertyName("mindmapModifiedUtc")]
    public DateTimeOffset MindmapModifiedUtc { get; set; }

    [JsonPropertyName("charsAdded")]
    public int CharsAdded { get; set; }

    [JsonPropertyName("charsRemoved")]
    public int CharsRemoved { get; set; }

    /// <summary>与上一版相比替换型变更的估算字数（插入+删除重叠时统计，可选）。</summary>
    [JsonPropertyName("charsModifiedEstimate")]
    public int CharsModifiedEstimate { get; set; }

    [JsonPropertyName("plainTextSnapshot")]
    public required string PlainTextSnapshot { get; set; }

    /// <summary>相对上一版本的 HTML 差异（首版为空串）。</summary>
    [JsonPropertyName("diffHtmlAgainstPrevious")]
    public required string DiffHtmlAgainstPrevious { get; set; }
}
