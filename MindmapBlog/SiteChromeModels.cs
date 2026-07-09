using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>左侧目录树 JSON（<c>data/site-nav.json</c>）。路径均为站点根相对、正斜杠。</summary>
internal sealed class SiteNavChromeFile
{
    [JsonPropertyName("folderTree")]
    public NavFolderNodeDto FolderTree { get; set; } = new();

    [JsonPropertyName("calendar")]
    public NavCalendarDto? Calendar { get; set; }
}

internal sealed class NavFolderNodeDto
{
    [JsonPropertyName("dirs")]
    public List<NavFolderBranchDto> Dirs { get; set; } = [];

    [JsonPropertyName("mindmaps")]
    public List<NavMindmapFileDto> Mindmaps { get; set; } = [];
}

internal sealed class NavFolderBranchDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("listPage")]
    public string ListPage { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";

    [JsonPropertyName("children")]
    public NavFolderNodeDto Children { get; set; } = new();
}

internal sealed class NavMindmapFileDto
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("listPage")]
    public string ListPage { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";

    [JsonPropertyName("root")]
    public NavMapTrieDto Root { get; set; } = new();
}

internal sealed class NavMapTrieDto
{
    [JsonPropertyName("segments")]
    public List<NavMapSegmentDto> Segments { get; set; } = [];

    [JsonPropertyName("articles")]
    public List<NavArticleLinkDto> Articles { get; set; } = [];
}

internal sealed class NavMapSegmentDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("listPage")]
    public string ListPage { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";

    [JsonPropertyName("node")]
    public NavMapTrieDto Node { get; set; } = new();
}

internal sealed class NavArticleLinkDto
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}

internal sealed class NavCalendarDto
{
    [JsonPropertyName("tree")]
    public List<NavCalendarYearDto> Tree { get; set; } = [];

    [JsonPropertyName("dayCounts")]
    public Dictionary<string, int> DayCounts { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("dayPages")]
    public Dictionary<string, string> DayPages { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("monthPages")]
    public Dictionary<string, string> MonthPages { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("yearMonths")]
    public List<string> YearMonths { get; set; } = [];

    [JsonPropertyName("defaultYear")]
    public int DefaultYear { get; set; }

    [JsonPropertyName("defaultMonth")]
    public int DefaultMonth { get; set; }
}

internal sealed class NavCalendarYearDto
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("listPage")]
    public string ListPage { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";

    [JsonPropertyName("months")]
    public List<NavCalendarMonthDto> Months { get; set; } = [];
}

internal sealed class NavCalendarMonthDto
{
    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("listPage")]
    public string ListPage { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";

    [JsonPropertyName("days")]
    public List<NavCalendarDayDto> Days { get; set; } = [];
}

internal sealed class NavCalendarDayDto
{
    [JsonPropertyName("day")]
    public int Day { get; set; }

    [JsonPropertyName("listPage")]
    public string ListPage { get; set; } = "";

    [JsonPropertyName("detailsId")]
    public string DetailsId { get; set; } = "";

    [JsonPropertyName("articles")]
    public List<NavCalendarArticleDto> Articles { get; set; } = [];
}

internal sealed class NavCalendarArticleDto
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";
}

/// <summary>右侧栏 JSON（<c>data/site-aside.json</c>）。</summary>
internal sealed class SiteAsideChromeFile
{
    [JsonPropertyName("profile")]
    public AsideProfileDto Profile { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<AsideTagDto> Tags { get; set; } = [];

    [JsonPropertyName("gallery")]
    public AsideGalleryDto Gallery { get; set; } = new();

    [JsonPropertyName("search")]
    public AsideSearchDto Search { get; set; } = new();
}

internal sealed class AsideProfileDto
{
    [JsonPropertyName("aboutPage")]
    public string AboutPage { get; set; } = "";

    [JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";
}

internal sealed class AsideTagDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

internal sealed class AsideGalleryDto
{
    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("preview")]
    public List<AsideGalleryPreviewDto> Preview { get; set; } = [];
}

internal sealed class AsideGalleryPreviewDto
{
    [JsonPropertyName("media")]
    public string Media { get; set; } = "";

    [JsonPropertyName("article")]
    public string Article { get; set; } = "";

    [JsonPropertyName("imageIndex")]
    public int ImageIndex { get; set; }

    [JsonPropertyName("caption")]
    public string Caption { get; set; } = "";
}

internal sealed class AsideSearchDto
{
    [JsonPropertyName("page")]
    public string Page { get; set; } = "";

    [JsonPropertyName("index")]
    public string Index { get; set; } = "data/search-index.json";
}
