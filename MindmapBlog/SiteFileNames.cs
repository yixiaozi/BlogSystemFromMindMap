namespace MindmapBlog;

/// <summary>
/// 一次生成周期内所有 HTML 路径（分支列表、书签标签页、计划日历、文章）的统一分配，避免冲突。
/// </summary>
internal sealed class SiteFileNames
{
    private SiteFileNames(
        BranchPageNameRegistry branchPages,
        Dictionary<string, string> tagHtmlByBookmark,
        Dictionary<string, string> calendarPages,
        string galleryPageWebPath,
        string aboutPageWebPath,
        string searchPageWebPath,
        string wordFrequencyPageWebPath)
    {
        BranchPages = branchPages;
        TagHtmlByBookmark = tagHtmlByBookmark;
        _calendarPages = calendarPages;
        GalleryPageWebPath = galleryPageWebPath;
        AboutPageWebPath = aboutPageWebPath;
        SearchPageWebPath = searchPageWebPath;
        WordFrequencyPageWebPath = wordFrequencyPageWebPath;
    }

    public BranchPageNameRegistry BranchPages { get; }

    /// <summary>书签文本 → 标签列表页路径（相对站点根）。</summary>
    public Dictionary<string, string> TagHtmlByBookmark { get; }

    /// <summary>独立「图册」页面路径（相对站点根）。</summary>
    public string GalleryPageWebPath { get; }

    /// <summary>「关于我」页面路径（相对站点根）。</summary>
    public string AboutPageWebPath { get; }

    /// <summary>独立「搜索」页面路径（相对站点根）。</summary>
    public string SearchPageWebPath { get; }

    /// <summary>「词频」统计页路径（相对站点根）。</summary>
    public string WordFrequencyPageWebPath { get; }

    /// <summary>RSS 2.0 订阅文件路径（固定在站点根，避免与其它页面冲突）。</summary>
    public string RssFeedWebPath => "rss.xml";

    private readonly Dictionary<string, string> _calendarPages;

    public static SiteFileNames Create(IReadOnlyList<BlogArticle> sortedArticles, string scanRootFull)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        used.Add("index.html");
        used.Add(HtmlLayout.GenerationHistoryPageFileName);
        used.Add(HtmlLayout.GitCommitHistoryPageFileName);
        used.Add(HtmlLayout.VisitHistoryPageFileName);
        used.Add("rss.xml");
        used.Add("search-aside.js");
        used.Add("timeline-tabs.js");
        used.Add("visit-stats.js");
        var galleryPagePath = SlugUtility.AllocateWebPath("", "图册", used);
        var aboutPagePath = SlugUtility.AllocateWebPath("", "关于我", used);
        var searchPagePath = SlugUtility.AllocateWebPath("", "搜索", used);
        var wordFreqPath = SlugUtility.AllocateWebPath("", "词频", used);

        var branchPages = BranchPageNameRegistry.Build(sortedArticles, scanRootFull, used);

        var tagMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in sortedArticles
                     .SelectMany(a => a.Bookmarks)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(t => t, StringComparer.Ordinal))
        {
            var stem = SlugUtility.Create(tag);
            tagMap[tag] = SlugUtility.AllocateWebPath("标签", "标签-" + stem, used);
        }

        var calMap = new Dictionary<string, string>(StringComparer.Ordinal);
        RegisterCalendarPages(sortedArticles, used, calMap);

        ArticleIdentity.AssignPublishFileNames(sortedArticles, scanRootFull, used);

        return new SiteFileNames(branchPages, tagMap, calMap, galleryPagePath, aboutPagePath, searchPagePath,
            wordFreqPath);
    }

    private static void RegisterCalendarPages(
        IReadOnlyList<BlogArticle> sortedArticles,
        HashSet<string> used,
        Dictionary<string, string> map)
    {
        var planned = sortedArticles.Where(a => a.ReminderAt.HasValue).ToList();
        if (planned.Count == 0)
            return;

        const string calRoot = "计划";
        foreach (var yg in planned.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Year).OrderBy(g => g.Key))
        {
            var y = yg.Key;
            map[$"y:{y}"] = SlugUtility.AllocateWebPath(calRoot, y.ToString(), used);

            foreach (var mg in yg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Month).OrderBy(g => g.Key))
            {
                var m = mg.Key;
                var stemM = $"{y}-{m:D2}";
                map[$"m:{y}-{m:D2}"] = SlugUtility.AllocateWebPath(calRoot, stemM, used);

                foreach (var dg in mg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Date).OrderBy(g => g.Key))
                {
                    var dt = dg.Key;
                    map[$"d:{dt:yyyy-MM-dd}"] = SlugUtility.AllocateWebPath(calRoot, $"{dt:yyyy-MM-dd}", used);
                }
            }
        }
    }

    public string TagPageFile(string tag) => TagHtmlByBookmark[tag];

    public string GetCalendarYearPage(int year) => _calendarPages[$"y:{year}"];

    public string GetCalendarMonthPage(int year, int month) => _calendarPages[$"m:{year}-{month:D2}"];

    public string GetCalendarDayPage(int year, int month, int day) =>
        _calendarPages[$"d:{year:0000}-{month:D2}-{day:D2}"];
}
