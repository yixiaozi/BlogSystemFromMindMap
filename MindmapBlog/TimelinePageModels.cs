using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>时间轴列表页数据（<c>data/timelines/{hash}.json</c>）。</summary>
internal sealed class TimelinePageFile
{
    [JsonPropertyName("pagePath")]
    public string PagePath { get; set; } = "";

    [JsonPropertyName("documentTitle")]
    public string DocumentTitle { get; set; } = "";

    [JsonPropertyName("heading")]
    public string Heading { get; set; } = "";

    [JsonPropertyName("subLine")]
    public string? SubLine { get; set; }

    [JsonPropertyName("leadHtml")]
    public string? LeadHtml { get; set; }

    [JsonPropertyName("wrapperClass")]
    public string WrapperClass { get; set; } = "page-with-timeline";

    [JsonPropertyName("enableSortTabs")]
    public bool EnableSortTabs { get; set; }

    /// <summary>published=入站/版本时间；reminder=计划提醒时间（日历列表页）。</summary>
    [JsonPropertyName("timeSource")]
    public string TimeSource { get; set; } = "published";

    [JsonPropertyName("items")]
    public List<TimelineItemDto> Items { get; set; } = [];
}

internal sealed class TimelineItemDto
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("published")]
    public string Published { get; set; } = "";

    [JsonPropertyName("modified")]
    public string Modified { get; set; } = "";

    [JsonPropertyName("bookmarks")]
    public List<string> Bookmarks { get; set; } = [];

    [JsonPropertyName("bookmarkPages")]
    public Dictionary<string, string> BookmarkPages { get; set; } =
        new(StringComparer.Ordinal);

    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; set; }
}

/// <summary>词频页数据（<c>data/word-frequency.json</c>）。</summary>
internal sealed class WordFrequencyPageFile
{
    [JsonPropertyName("articleCount")]
    public int ArticleCount { get; set; }

    [JsonPropertyName("totalTokenOccurrences")]
    public int TotalTokenOccurrences { get; set; }

    [JsonPropertyName("uniqueTokens")]
    public int UniqueTokens { get; set; }

    [JsonPropertyName("minCount")]
    public int MinCount { get; set; }

    [JsonPropertyName("maxCount")]
    public int MaxCount { get; set; }

    [JsonPropertyName("topTerms")]
    public List<WordFrequencyTermDto> TopTerms { get; set; } = [];

    [JsonPropertyName("hitsByTerm")]
    public Dictionary<string, List<WordFrequencyHitDto>> HitsByTerm { get; set; } =
        new(StringComparer.Ordinal);
}

internal sealed class WordFrequencyTermDto
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

internal sealed class WordFrequencyHitDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("href")]
    public string Href { get; set; } = "";

    [JsonPropertyName("snippets")]
    public List<string> Snippets { get; set; } = [];
}
