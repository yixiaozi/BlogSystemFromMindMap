using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>生成 <c>data/site-nav.json</c> 与 <c>data/site-aside.json</c>，供前端 site-chrome.js 加载。</summary>
internal static class SiteChromeStore
{
    public const string NavJsonWebPath = "data/site-nav.json";
    public const string AsideJsonWebPath = "data/site-aside.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(
        string outputRoot,
        IReadOnlyList<BlogArticle> articles,
        string scanRootFull,
        SiteFileNames names,
        IReadOnlyList<ArticleGalleryItem> galleryEntries,
        string? avatarSitePath)
    {
        var nav = BuildNav(articles, scanRootFull, names);
        var aside = BuildAside(articles, names, galleryEntries, avatarSitePath);
        WriteJson(outputRoot, NavJsonWebPath, nav);
        WriteJson(outputRoot, AsideJsonWebPath, aside);
        Console.WriteLine($"已写入站点公共部件：{NavJsonWebPath}、{AsideJsonWebPath}");
    }

    private static void WriteJson<T>(string outputRoot, string webPath, T data)
    {
        var local = SitePathHelper.CombineLocal(outputRoot, webPath);
        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(local, JsonSerializer.Serialize(data, JsonOptions));
    }

    private static SiteNavChromeFile BuildNav(
        IReadOnlyList<BlogArticle> articles,
        string scanRootFull,
        SiteFileNames names)
    {
        var tree = NavTreeBuilder.BuildFolderTree(articles, scanRootFull);
        return new SiteNavChromeFile
        {
            FolderTree = BuildFolderNode(tree, scanRootFull, [], names.BranchPages),
            Calendar = BuildCalendar(articles, names),
        };
    }

    private static NavFolderNodeDto BuildFolderNode(
        FolderBranch branch,
        string scanRootFull,
        List<string> folderPathPrefix,
        BranchPageNameRegistry branchPages)
    {
        var node = new NavFolderNodeDto();
        foreach (var dir in branch.Dirs.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = folderPathPrefix.Concat(new[] { dir.Key }).ToList();
            node.Dirs.Add(new NavFolderBranchDto
            {
                Name = dir.Key,
                ListPage = branchPages.GetFolderListPage(path),
                DetailsId = BranchNav.FolderBranchDetailsId(scanRootFull, path),
                Children = BuildFolderNode(dir.Value, scanRootFull, path, branchPages),
            });
        }

        foreach (var fileEntry in branch.MindmapFiles.OrderBy(kv => Path.GetFileName(kv.Key), StringComparer.OrdinalIgnoreCase))
        {
            var mmPath = fileEntry.Key;
            node.Mindmaps.Add(new NavMindmapFileDto
            {
                Label = Path.GetFileName(mmPath),
                ListPage = branchPages.GetMmPrefixListPage(mmPath, ""),
                DetailsId = BranchNav.MmFileDetailsId(mmPath),
                Root = BuildMapTrie(NavTreeBuilder.BuildMapTrie(fileEntry.Value), mmPath, [], branchPages),
            });
        }

        return node;
    }

    private static NavMapTrieDto BuildMapTrie(
        MapTrieNode trie,
        string mmPath,
        List<string> prefixSegments,
        BranchPageNameRegistry branchPages)
    {
        var dto = new NavMapTrieDto();
        foreach (var seg in trie.Segments.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var nextPrefix = new List<string>(prefixSegments) { seg.Key };
            var joined = string.Join("/", nextPrefix);
            dto.Segments.Add(new NavMapSegmentDto
            {
                Name = seg.Key,
                ListPage = branchPages.GetMmPrefixListPage(mmPath, joined),
                DetailsId = BranchNav.MmNodeDetailsId(mmPath, joined),
                Node = BuildMapTrie(seg.Value, mmPath, nextPrefix, branchPages),
            });
        }

        foreach (var art in trie.ArticlesHere.OrderByDescending(a => a.Modified))
        {
            dto.Articles.Add(new NavArticleLinkDto
            {
                Href = art.HtmlFileName,
                Title = art.Title,
            });
        }

        return dto;
    }

    private static NavCalendarDto? BuildCalendar(IReadOnlyList<BlogArticle> articles, SiteFileNames names)
    {
        var planned = articles.Where(a => a.ReminderAt.HasValue).ToList();
        if (planned.Count == 0)
            return null;

        var dayMap = planned
            .GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var ymList = dayMap.Keys
            .Select(d => (d.Year, d.Month))
            .Distinct()
            .OrderBy(t => t.Year).ThenBy(t => t.Month)
            .ToList();
        var latest = ymList[^1];

        var dayCounts = dayMap.ToDictionary(
            kv => kv.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            kv => kv.Value,
            StringComparer.Ordinal);

        var dayPages = dayMap.Keys.ToDictionary(
            d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            d => names.GetCalendarDayPage(d.Year, d.Month, d.Day),
            StringComparer.Ordinal);

        var monthPages = ymList.ToDictionary(
            t => $"{t.Year:D4}-{t.Month:D2}",
            t => names.GetCalendarMonthPage(t.Year, t.Month),
            StringComparer.Ordinal);

        var tree = new List<NavCalendarYearDto>();
        foreach (var yg in planned.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Year).OrderBy(g => g.Key))
        {
            var year = yg.Key;
            var yearDto = new NavCalendarYearDto
            {
                Year = year,
                ListPage = names.GetCalendarYearPage(year),
                DetailsId = BranchNav.CalendarYearDetailsId(year),
            };

            foreach (var mg in yg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Month).OrderBy(g => g.Key))
            {
                var month = mg.Key;
                var monthDto = new NavCalendarMonthDto
                {
                    Month = month,
                    ListPage = names.GetCalendarMonthPage(year, month),
                    DetailsId = BranchNav.CalendarMonthDetailsId(year, month),
                };

                foreach (var dg in mg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Date).OrderBy(g => g.Key))
                {
                    var date = dg.Key;
                    var dayDto = new NavCalendarDayDto
                    {
                        Day = date.Day,
                        ListPage = names.GetCalendarDayPage(date.Year, date.Month, date.Day),
                        DetailsId = BranchNav.CalendarDayDetailsId(date.Year, date.Month, date.Day),
                    };

                    foreach (var art in dg.OrderBy(a => a.ReminderAt))
                    {
                        dayDto.Articles.Add(new NavCalendarArticleDto
                        {
                            Href = art.HtmlFileName,
                            Title = art.Title,
                            Time = art.ReminderAt!.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                        });
                    }

                    monthDto.Days.Add(dayDto);
                }

                yearDto.Months.Add(monthDto);
            }

            tree.Add(yearDto);
        }

        return new NavCalendarDto
        {
            Tree = tree,
            DayCounts = dayCounts,
            DayPages = dayPages,
            MonthPages = monthPages,
            YearMonths = ymList.Select(t => $"{t.Year:D4}-{t.Month:D2}").ToList(),
            DefaultYear = latest.Year,
            DefaultMonth = latest.Month,
        };
    }

    private static SiteAsideChromeFile BuildAside(
        IReadOnlyList<BlogArticle> articles,
        SiteFileNames names,
        IReadOnlyList<ArticleGalleryItem> galleryEntries,
        string? avatarSitePath)
    {
        var counts = HtmlLayout.CountBookmarks(articles);
        var tags = counts.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(tag => new AsideTagDto
            {
                Name = tag,
                Page = names.TagPageFile(tag),
                Count = counts[tag],
            })
            .ToList();

        var preview = galleryEntries.Take(8).Select(e => new AsideGalleryPreviewDto
        {
            Media = e.MediaPathFromSiteRoot,
            Article = e.ArticleWebPath,
            ImageIndex = e.ImageIndexInArticle,
            Caption = e.Caption,
        }).ToList();

        return new SiteAsideChromeFile
        {
            Profile = new AsideProfileDto
            {
                AboutPage = names.AboutPageWebPath,
                Avatar = avatarSitePath,
                Signature = SiteProfile.Signature,
            },
            Tags = tags,
            Gallery = new AsideGalleryDto
            {
                Page = names.GalleryPageWebPath,
                Total = galleryEntries.Count,
                Preview = preview,
            },
            Search = new AsideSearchDto
            {
                Page = names.SearchPageWebPath,
                Index = "data/search-index.json",
            },
        };
    }
}
