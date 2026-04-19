using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MindmapBlog;

public sealed class StaticSiteGenerator
{
    private sealed record CopiedArticleImage(
        ImageBlock Block,
        string UrlRelativeToArticle,
        string MediaPathFromSiteRoot,
        int IndexInArticle);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>删除上次生成的静态页与媒体，保留 <c>data/</c> 下的 JSON 版本库与生成记录。</summary>
    private static void CleanStaleSiteArtifacts(string outputRoot, string mediaDir)
    {
        foreach (var html in Directory.EnumerateFiles(outputRoot, "*.html", SearchOption.AllDirectories))
        {
            try
            {
                var rel = Path.GetRelativePath(outputRoot, html);
                if (rel.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch
            {
                continue;
            }

            try { File.Delete(html); } catch { /* ignore */ }
        }

        RemoveEmptyDirsUnder(outputRoot);

        if (Directory.Exists(mediaDir))
        {
            foreach (var f in Directory.GetFiles(mediaDir))
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }

        }
    }

    /// <summary>删除输出目录下因清空 HTML 产生的空文件夹（不触及 <c>data</c>）。</summary>
    private static void RemoveEmptyDirsUnder(string outputRoot)
    {
        if (!Directory.Exists(outputRoot))
            return;

        foreach (var dir in Directory.EnumerateDirectories(outputRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                var rel = Path.GetRelativePath(outputRoot, dir);
                if (string.Equals(rel, "data", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("data" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static void WriteUtf8Web(string outputRoot, string webPath, string content)
    {
        var local = SitePathHelper.CombineLocal(outputRoot, webPath);
        var dir = Path.GetDirectoryName(local);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(local, content, Utf8);
    }

    public void Generate(
        IReadOnlyList<BlogArticle> articles,
        string outputDirectory,
        string scanRootDirectory,
        string? siteBaseUrl = null)
    {
        var outDir = Path.GetFullPath(outputDirectory);
        var scanRoot = Path.GetFullPath(scanRootDirectory);
        Directory.CreateDirectory(outDir);

        var mediaDir = Path.Combine(outDir, "media");
        CleanStaleSiteArtifacts(outDir, mediaDir);
        Directory.CreateDirectory(mediaDir);

        var generatedAt = DateTimeOffset.UtcNow;

        var sortedArticles = articles.OrderByDescending(a => a.Modified).ToList();

        var historyPath = GenerationHistoryStore.HistoryFilePath(outDir);
        var historyFile = GenerationHistoryStore.LoadOrEmpty(historyPath);
        var fingerprints = GenerationHistoryStore.BuildFingerprints(sortedArticles);
        var runRecord = GenerationHistoryStore.BuildRunRecord(
            historyFile.LastSnapshot.Count > 0 ? historyFile.LastSnapshot : null,
            fingerprints,
            sortedArticles,
            generatedAt);

        var names = SiteFileNames.Create(sortedArticles, scanRoot);
        var avatarSitePath = SiteProfile.TryPublishAvatar(scanRoot, mediaDir);

        var galleryItems = new List<ArticleGalleryItem>();

        var versionDocs = new Dictionary<string, ArticleVersionDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var article in sortedArticles)
        {
            var doc = VersionHistoryStore.UpdateForArticle(article, scanRoot, outDir, generatedAt);
            versionDocs[ArticleIdentity.ComputeStorageKey(article.SourceMmPath, article.ArticleNodeId)] = doc;
        }

        foreach (var article in sortedArticles)
        {
            var key = ArticleIdentity.ComputeStorageKey(article.SourceMmPath, article.ArticleNodeId);
            var doc = versionDocs[key];
            var htmlName = article.HtmlFileName;

            var copiedImages = CopyArticleImages(article, mediaDir);
            foreach (var c in copiedImages)
            {
                var cap = string.IsNullOrWhiteSpace(c.Block.AltText)
                    ? article.Title
                    : c.Block.AltText.Trim();
                galleryItems.Add(new ArticleGalleryItem(
                    article.HtmlFileName,
                    c.MediaPathFromSiteRoot,
                    cap,
                    c.IndexInArticle));
            }

            var bodyHtml = RenderBodyHtml(article, copiedImages);

            var inner = BuildArticleInner(article, bodyHtml, doc, names);
            var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRoot, htmlName, htmlName, names);
            var tags = HtmlLayout.BuildRightAside(sortedArticles, null, names, htmlName, galleryItems, avatarSitePath);

            var page = HtmlLayout.BuildDocument(
                article.Title,
                headExtra: "",
                innerMain: inner,
                navLeftHtml: nav,
                tagAsideHtml: tags,
                htmlName,
                names.RssFeedWebPath,
                names);

            WriteUtf8Web(outDir, htmlName, page);
        }

        WriteBranchListPages(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteCalendarListPages(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteIndex(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteTagPages(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteGalleryPage(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteAboutPage(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteSearchPage(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);
        WriteWordFrequencyPage(outDir, scanRoot, sortedArticles, names, galleryItems, avatarSitePath);

        historyFile.Runs.Insert(0, runRecord);
        historyFile.LastSnapshot = fingerprints;
        GenerationHistoryStore.Save(historyPath, historyFile);
        WriteGenerationHistoryPage(outDir, scanRoot, historyFile.Runs, sortedArticles, names, galleryItems,
            avatarSitePath);

        WriteRssFeed(outDir, sortedArticles, names, siteBaseUrl, generatedAt);

        WriteSearchIndex(outDir, sortedArticles);
        CopySearchAsideScript(outDir);

        WriteStylesheet(Path.Combine(outDir, "site.css"));
    }

    private static void WriteRssFeed(
        string outDir,
        IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names,
        string? siteBaseUrl,
        DateTimeOffset generatedAt)
    {
        const int maxItems = 40;
        var items = sortedArticles.OrderByDescending(a => a.Modified).Take(maxItems).ToList();

        var channelTitle = "思维导图博客";
        var channelDesc = "按文章修改时间推送更新（RSS 2.0）。";
        var channelLink = CombineSiteUrl(siteBaseUrl, "index.html");
        var selfLink = CombineSiteUrl(siteBaseUrl, names.RssFeedWebPath);

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">");
        sb.AppendLine("  <channel>");
        sb.Append("    <title>").Append(XmlEscaped(channelTitle)).AppendLine("</title>");
        sb.Append("    <link>").Append(XmlEscaped(channelLink)).AppendLine("</link>");
        sb.Append("    <description>").Append(XmlEscaped(channelDesc)).AppendLine("</description>");
        sb.AppendLine("    <language>zh-CN</language>");
        sb.Append("    <lastBuildDate>").Append(XmlEscaped(Rfc822Date(generatedAt))).AppendLine("</lastBuildDate>");
        sb.Append("    <atom:link href=\"").Append(XmlEscaped(selfLink)).Append("\" rel=\"self\" type=\"application/rss+xml\" />");
        sb.AppendLine();

        foreach (var article in items)
        {
            var excerpt = BuildExcerpt(article);
            var path = article.HtmlFileName.Replace('\\', '/');
            var itemLink = CombineSiteUrl(siteBaseUrl, path);
            sb.AppendLine("    <item>");
            sb.Append("      <title>").Append(XmlEscaped(article.Title)).AppendLine("</title>");
            sb.Append("      <link>").Append(XmlEscaped(itemLink)).AppendLine("</link>");
            sb.Append("      <guid isPermaLink=\"true\">").Append(XmlEscaped(itemLink)).AppendLine("</guid>");
            sb.Append("      <pubDate>").Append(XmlEscaped(Rfc822Date(article.Modified))).AppendLine("</pubDate>");
            sb.Append("      <description>").Append(XmlEscaped(string.IsNullOrEmpty(excerpt) ? article.Title : excerpt))
                .AppendLine("</description>");
            sb.AppendLine("    </item>");
        }

        sb.AppendLine("  </channel>");
        sb.AppendLine("</rss>");

        WriteUtf8Web(outDir, names.RssFeedWebPath, sb.ToString());
    }

    private static string CombineSiteUrl(string? siteBaseUrl, string webPathFromRoot)
    {
        webPathFromRoot = webPathFromRoot.Trim().Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(webPathFromRoot))
            webPathFromRoot = "index.html";

        if (string.IsNullOrWhiteSpace(siteBaseUrl))
            return webPathFromRoot;

        var b = siteBaseUrl.TrimEnd('/');
        return b + "/" + webPathFromRoot;
    }

    private static string XmlEscaped(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return WebUtility.HtmlEncode(text);
    }

    private static string Rfc822Date(DateTimeOffset dto)
    {
        return dto.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss \\G\\M\\T", CultureInfo.InvariantCulture);
    }

    private static void WriteSearchIndex(string outDir, IReadOnlyList<BlogArticle> sortedArticles)
    {
        var list = sortedArticles.Select(SearchIndexRecord.FromArticle).ToList();
        var json = JsonSerializer.Serialize(list,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

        WriteUtf8Web(outDir, "data/search-index.json", json);
    }

    /// <summary>将脚本复制到站点根（与 HtmlLayout 中的 script src 一致）。</summary>
    private static void CopySearchAsideScript(string outDir)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "Scripts", "search-aside.js");
        if (!File.Exists(src))
        {
            Console.Error.WriteLine("警告：找不到 Scripts/search-aside.js，侧栏搜索不可用。");
            return;
        }

        File.Copy(src, Path.Combine(outDir, "search-aside.js"), overwrite: true);
    }

    private static void WriteGenerationHistoryPage(
        string outDir,
        string scanRoot,
        IReadOnlyList<GenerationRunRecord> runs,
        IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names,
        IReadOnlyList<ArticleGalleryItem> galleryEntries,
        string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var cur = HtmlLayout.GenerationHistoryPageFileName;
        var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, cur, names);
        var aside = HtmlLayout.BuildRightAside(sortedArticles, null, names, cur, galleryEntries, avatarSitePath);

        var inner = new StringBuilder();
        inner.AppendLine("<div class=\"page-gen-history\">");
        inner.AppendLine("<header class=\"hero\">");
        inner.AppendLine("<h1 class=\"page-title\">网站生成记录</h1>");
        inner.AppendLine("<p class=\"page-lead\">每次运行生成器都会追加一行。与<strong>上一次成功生成</strong>保存的快照对比得到增减篇数与字数；编辑+/− 表示既有文章正文变化带来的字数增减。点击下方表格中的某一行，在页面底部查看该次的新增、移除与改动标题。</p>");
        inner.Append("<p class=\"page-lead\">原始数据：<code>")
            .Append(WebUtility.HtmlEncode("data/generation-history.json"))
            .AppendLine("</code></p>");
        inner.AppendLine("</header>");

        inner.AppendLine("<div class=\"gen-history-table-wrap\">");
        inner.AppendLine("<table class=\"gen-history-table\">");
        inner.AppendLine("<thead><tr>");
        inner.AppendLine("<th scope=\"col\">生成时间（本地）</th>");
        inner.AppendLine("<th scope=\"col\">+篇</th>");
        inner.AppendLine("<th scope=\"col\">−篇</th>");
        inner.AppendLine("<th scope=\"col\">改篇</th>");
        inner.AppendLine("<th scope=\"col\">新文字数</th>");
        inner.AppendLine("<th scope=\"col\">删文字数</th>");
        inner.AppendLine("<th scope=\"col\">编辑+</th>");
        inner.AppendLine("<th scope=\"col\">编辑−</th>");
        inner.AppendLine("<th scope=\"col\">总篇</th>");
        inner.AppendLine("<th scope=\"col\">总字数</th>");
        inner.AppendLine("<th scope=\"col\">图册</th>");
        inner.AppendLine("<th scope=\"col\">书签标签</th>");
        inner.AppendLine("<th scope=\"col\">提醒</th>");
        inner.AppendLine("</tr></thead><tbody>");

        if (runs.Count == 0)
        {
            inner.AppendLine(
                "<tr><td colspan=\"13\" class=\"gen-history-empty\">尚无记录。请成功运行一次生成器后查看。</td></tr>");
        }

        for (var ri = 0; ri < runs.Count; ri++)
        {
            var r = runs[ri];
            inner.Append("<tr class=\"gen-history-row\" tabindex=\"0\" role=\"button\" aria-label=\"查看此次生成的标题详情\" data-run-index=\"")
                .Append(ri)
                .AppendLine("\">");
            inner.Append("<td><time datetime=\"")
                .Append(WebUtility.HtmlEncode(r.GeneratedAtUtc.ToString("O")))
                .Append("\">")
                .Append(WebUtility.HtmlEncode(r.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")))
                .AppendLine("</time></td>");
            inner.Append("<td title=\"")
                .Append(WebUtility.HtmlEncode(TitlesTooltip(r.AddedTitles)))
                .Append("\">")
                .Append(r.ArticlesAdded)
                .AppendLine("</td>");
            inner.Append("<td title=\"")
                .Append(WebUtility.HtmlEncode(TitlesTooltip(r.RemovedTitles)))
                .Append("\">")
                .Append(r.ArticlesRemoved)
                .AppendLine("</td>");
            inner.Append("<td title=\"")
                .Append(WebUtility.HtmlEncode(TitlesTooltip(r.ModifiedTitles)))
                .Append("\">")
                .Append(r.ArticlesModified)
                .AppendLine("</td>");
            inner.Append("<td>").Append(r.CharsInNewArticles).AppendLine("</td>");
            inner.Append("<td>").Append(r.CharsInRemovedArticles).AppendLine("</td>");
            inner.Append("<td>").Append(r.CharsAddedByEdits).AppendLine("</td>");
            inner.Append("<td>").Append(r.CharsRemovedByEdits).AppendLine("</td>");
            inner.Append("<td>").Append(r.TotalArticles).AppendLine("</td>");
            inner.Append("<td>").Append(r.TotalPlainChars).AppendLine("</td>");
            inner.Append("<td>").Append(r.MindmapFileCount).AppendLine("</td>");
            inner.Append("<td>").Append(r.DistinctBookmarkCount).AppendLine("</td>");
            inner.Append("<td>").Append(r.ArticlesWithReminder).AppendLine("</td>");
            inner.AppendLine("</tr>");
        }

        inner.AppendLine("</tbody></table>");
        inner.AppendLine("</div>");
        inner.AppendLine("<div class=\"gen-history-pager\" aria-label=\"网站生成记录分页\">");
        inner.AppendLine("<div class=\"gen-history-pager-left\">");
        inner.AppendLine("<label class=\"gen-history-pager-label\" for=\"gen-history-page-size\">每页</label>");
        inner.AppendLine(
            "<select id=\"gen-history-page-size\" class=\"gen-history-page-size\"><option value=\"10\">10</option><option value=\"20\" selected>20</option><option value=\"50\">50</option><option value=\"100\">100</option></select>");
        inner.AppendLine("<span class=\"gen-history-pager-label\">条</span>");
        inner.AppendLine("</div>");
        inner.AppendLine("<div class=\"gen-history-pager-right\">");
        inner.AppendLine("<button type=\"button\" id=\"gen-history-prev\" class=\"gen-history-page-btn\">上一页</button>");
        inner.AppendLine("<span id=\"gen-history-page-info\" class=\"gen-history-page-info\">第 1 / 1 页</span>");
        inner.AppendLine("<button type=\"button\" id=\"gen-history-next\" class=\"gen-history-page-btn\">下一页</button>");
        inner.AppendLine("</div>");
        inner.AppendLine("</div>");

        inner.AppendLine("<section class=\"gen-history-detail-section\" aria-live=\"polite\" aria-label=\"选中批次详情\">");
        inner.AppendLine("<h2 class=\"gen-history-subtitle\">选中批次的新增、移除与改动标题</h2>");
        inner.AppendLine("<p id=\"gen-history-detail-placeholder\" class=\"gen-detail-placeholder\">请在上方表格中点击某一行，在此查看该次生成对应的文章标题。</p>");
        inner.AppendLine("<div id=\"gen-history-detail-panel\" class=\"gen-detail-panel\" hidden></div>");
        inner.AppendLine("</section>");

        var titleHrefLookup = sortedArticles
            .GroupBy(a => a.Title, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => SitePathHelper.RelFromTo(cur, g.First().HtmlFileName),
                StringComparer.Ordinal);
        var payload = runs.Select(r => new
        {
            timeLabel = r.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            addedTitles = r.AddedTitles ?? [],
            addedLinks = (r.AddedTitles ?? [])
                .Select(t => new
                {
                    title = t,
                    href = titleHrefLookup.TryGetValue(t, out var h) ? h : "",
                }).ToList(),
            removedTitles = r.RemovedTitles ?? [],
            modifiedTitles = r.ModifiedTitles ?? [],
        }).ToList();

        var jsonUtf8 = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        var b64 = Convert.ToBase64String(jsonUtf8);
        inner.AppendLine("<textarea id=\"gen-runs-b64\" hidden readonly>");
        inner.Append(b64);
        inner.AppendLine("</textarea>");
        inner.Append(GenerationHistoryInteractiveScript);
        inner.AppendLine("</div>");

        var page = HtmlLayout.BuildDocument("网站生成记录", "", inner.ToString(), nav, aside, cur,
            names.RssFeedWebPath,
            names);
        WriteUtf8Web(outDir, HtmlLayout.GenerationHistoryPageFileName, page);
    }

    private static string TitlesTooltip(IReadOnlyList<string>? titles)
    {
        if (titles == null || titles.Count == 0)
            return "";
        return string.Join(
            " · ",
            titles.Select(t => t.Replace('\r', ' ').Replace('\n', ' ').Trim()));
    }

    private const string GenerationHistoryInteractiveScript = """
<script>
(function () {
  var ta = document.getElementById("gen-runs-b64");
  var placeholder = document.getElementById("gen-history-detail-placeholder");
  var panel = document.getElementById("gen-history-detail-panel");
  var rows = Array.prototype.slice.call(document.querySelectorAll(".gen-history-table tbody tr.gen-history-row"));
  var pageSizeSel = document.getElementById("gen-history-page-size");
  var prevBtn = document.getElementById("gen-history-prev");
  var nextBtn = document.getElementById("gen-history-next");
  var pageInfo = document.getElementById("gen-history-page-info");
  if (!ta || !placeholder || !panel || rows.length === 0) return;
  var runs;
  try {
    var bin = atob(ta.textContent.trim());
    var u8 = new Uint8Array(bin.length);
    for (var j = 0; j < bin.length; j++) u8[j] = bin.charCodeAt(j);
    runs = JSON.parse(new TextDecoder("utf-8").decode(u8));
  } catch (e) {
    return;
  }
  function esc(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
  function renderTitles(label, titles) {
    if (!titles || !titles.length) return "";
    return (
      '<p class="gen-sample-p"><strong>' +
      esc(label) +
      "</strong>：" +
      titles.map(function (t) {
        return esc(t);
      }).join("、") +
      "</p>"
    );
  }
  function renderAddedBlock(label, links, titles) {
    if (links && links.length) {
      return (
        '<p class="gen-sample-p"><strong>' +
        esc(label) +
        "</strong>：" +
        links
          .map(function (x) {
            if (x && x.href) return '<a class="gen-detail-link" href="' + esc(x.href) + '">' + esc(x.title || "") + "</a>";
            return esc((x && x.title) || "");
          })
          .join("、") +
        "</p>"
      );
    }
    return renderTitles(label, titles);
  }
  function showRun(i) {
    var r = runs[i];
    if (!r) return;
    panel.removeAttribute("hidden");
    placeholder.setAttribute("hidden", "hidden");
    var h =
      '<div class="gen-sample-block">' +
      '<h3 class="gen-sample-h3">' +
      esc(r.timeLabel || "") +
      "</h3>" +
      renderAddedBlock("新增", r.addedLinks, r.addedTitles) +
      renderTitles("移除", r.removedTitles) +
      renderTitles("改动", r.modifiedTitles);
    if (
      (!r.addedTitles || !r.addedTitles.length) &&
      (!r.removedTitles || !r.removedTitles.length) &&
      (!r.modifiedTitles || !r.modifiedTitles.length)
    ) {
      h +=
        '<p class="gen-sample-p gen-detail-empty">该次与上一快照相比，无新增、移除或正文改动条目。</p>';
    }
    h += "</div>";
    panel.innerHTML = h;
    panel.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }
  var pageSize = pageSizeSel ? parseInt(pageSizeSel.value, 10) : 10;
  if (isNaN(pageSize) || pageSize <= 0) pageSize = 10;
  var page = 1;
  function totalPages() {
    return Math.max(1, Math.ceil(rows.length / pageSize));
  }
  function renderPage() {
    var tp = totalPages();
    if (page > tp) page = tp;
    if (page < 1) page = 1;
    var start = (page - 1) * pageSize;
    var end = start + pageSize;
    rows.forEach(function (tr, i) {
      tr.hidden = i < start || i >= end;
    });
    if (pageInfo) pageInfo.textContent = "第 " + page + " / " + tp + " 页";
    if (prevBtn) prevBtn.disabled = page <= 1;
    if (nextBtn) nextBtn.disabled = page >= tp;
  }
  function clearSelect() {
    rows.forEach(function (tr) {
      tr.classList.remove("is-selected");
    });
  }
  rows.forEach(function (tr) {
    tr.addEventListener("click", function () {
      clearSelect();
      tr.classList.add("is-selected");
      var idx = parseInt(tr.getAttribute("data-run-index"), 10);
      if (!isNaN(idx)) showRun(idx);
    });
    tr.addEventListener("keydown", function (ev) {
      if (ev.key === "Enter" || ev.key === " ") {
        ev.preventDefault();
        tr.click();
      }
    });
  });
  if (pageSizeSel) {
    pageSizeSel.addEventListener("change", function () {
      var v = parseInt(pageSizeSel.value, 10);
      if (!isNaN(v) && v > 0) {
        pageSize = v;
        page = 1;
        renderPage();
      }
    });
  }
  if (prevBtn) {
    prevBtn.addEventListener("click", function () {
      if (page > 1) {
        page--;
        renderPage();
      }
    });
  }
  if (nextBtn) {
    nextBtn.addEventListener("click", function () {
      if (page < totalPages()) {
        page++;
        renderPage();
      }
    });
  }
  renderPage();
})();
</script>
""";

    /// <summary>为每个磁盘文件夹分支、每个导图文件及导图内路径前缀生成「该分支全部文章」列表页（时间轴样式）。</summary>
    private static void WriteBranchListPages(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void WriteOne(string fileName, string heading, string subLine, List<BlogArticle> arts)
        {
            if (!written.Add(fileName))
                return;

            var inner = new StringBuilder();
            inner.AppendLine("<div class=\"page-branch page-with-timeline\">");
            inner.AppendLine("<header class=\"hero\">");
            inner.Append("<h1 class=\"page-title\">").Append(WebUtility.HtmlEncode(heading)).AppendLine("</h1>");
            inner.Append("<p class=\"page-lead\">").Append(WebUtility.HtmlEncode(subLine)).AppendLine("</p>");
            inner.AppendLine("</header>");
            AppendTimelineList(inner, arts, a => a.Modified.LocalDateTime, descending: true, names, fileName);
            inner.AppendLine("</div>");

            var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, fileName, names);
            var tags = HtmlLayout.BuildRightAside(sortedArticles, null, names, fileName, galleryEntries, avatarSitePath);
            var page = HtmlLayout.BuildDocument(
                heading + " · 分支文章",
                "",
                inner.ToString(),
                nav,
                tags,
                fileName,
                names.RssFeedWebPath,
                names);
            WriteUtf8Web(outDir, fileName, page);
        }

        var folderRoot = NavTreeBuilder.BuildFolderTree(sortedArticles, scanRootFull);

        void EmitFolders(FolderBranch fb, List<string> pathSegs)
        {
            var arts = NavTreeBuilder.CollectFolderSubtreeArticles(fb);
            if (arts.Count > 0)
            {
                var fn = names.BranchPages.GetFolderListPage(pathSegs);
                var heading = pathSegs.Count == 0
                    ? "扫描目录内全部文章"
                    : "文件夹：" + string.Join(" / ", pathSegs);
                var sub = $"共 {arts.Count} 篇（含本文件夹及子文件夹内导图）";
                WriteOne(fn, heading, sub, arts);
            }

            foreach (var kv in fb.Dirs.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                EmitFolders(kv.Value, pathSegs.Concat(new[] { kv.Key }).ToList());
        }

        EmitFolders(folderRoot, []);

        foreach (var grp in sortedArticles.GroupBy(a => a.SourceMmPath))
        {
            var mmPath = grp.Key;
            var trie = NavTreeBuilder.BuildMapTrie(grp.ToList());

            void Visit(MapTrieNode node, List<string> prefix)
            {
                var arts = NavTreeBuilder.CollectSubtreeArticles(node);
                if (arts.Count == 0)
                    return;

                var joined = string.Join("/", prefix);
                var fn = names.BranchPages.GetMmPrefixListPage(mmPath, joined);
                var fileBase = Path.GetFileName(mmPath);
                var heading = string.IsNullOrEmpty(joined)
                    ? $"导图：{fileBase}（全部）"
                    : $"导图：{fileBase} · {joined}";
                var sub = $"共 {arts.Count} 篇（该节点及以下全部文章）";
                WriteOne(fn, heading, sub, arts);

                foreach (var kv in node.Segments.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    Visit(kv.Value, prefix.Concat(new[] { kv.Key }).ToList());
            }

            Visit(trie, []);
        }
    }

    /// <summary>按节点提醒日期生成 年 / 月 / 日 计划列表页（与左侧日期导航对应，时间轴样式）。</summary>
    private static void WriteCalendarListPages(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var planned = sortedArticles.Where(a => a.ReminderAt.HasValue).ToList();
        if (planned.Count == 0)
            return;

        var scanRootFull = Path.GetFullPath(scanRoot);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void WriteCalPage(string fileName, string heading, string subLine, List<BlogArticle> arts)
        {
            if (!written.Add(fileName))
                return;

            var inner = new StringBuilder();
            inner.AppendLine("<div class=\"page-branch page-with-timeline\">");
            inner.AppendLine("<header class=\"hero\">");
            inner.Append("<h1 class=\"page-title\">").Append(WebUtility.HtmlEncode(heading)).AppendLine("</h1>");
            inner.Append("<p class=\"page-lead\">").Append(WebUtility.HtmlEncode(subLine)).AppendLine("</p>");
            inner.AppendLine("</header>");
            AppendTimelineList(
                inner,
                arts,
                a => a.ReminderAt!.Value.LocalDateTime,
                descending: false,
                names,
                fileName);
            inner.AppendLine("</div>");

            var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, fileName, names);
            var tags = HtmlLayout.BuildRightAside(sortedArticles, null, names, fileName, galleryEntries, avatarSitePath);
            var page = HtmlLayout.BuildDocument(
                heading + " · 计划",
                "",
                inner.ToString(),
                nav,
                tags,
                fileName,
                names.RssFeedWebPath,
                names);
            WriteUtf8Web(outDir, fileName, page);
        }

        foreach (var yg in planned.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Year).OrderBy(g => g.Key))
        {
            var y = yg.Key;
            WriteCalPage(
                names.GetCalendarYearPage(y),
                $"计划 · {y}年",
                $"共 {yg.Count()} 项（按提醒时间先后）",
                yg.ToList());

            foreach (var mg in yg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Month).OrderBy(g => g.Key))
            {
                var m = mg.Key;
                WriteCalPage(
                    names.GetCalendarMonthPage(y, m),
                    $"计划 · {y}年{m}月",
                    $"共 {mg.Count()} 项（按提醒时间先后）",
                    mg.ToList());

                foreach (var dg in mg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Date).OrderBy(g => g.Key))
                {
                    var dt = dg.Key;
                    WriteCalPage(
                        names.GetCalendarDayPage(dt.Year, dt.Month, dt.Day),
                        $"计划 · {dt:yyyy年M月d日}",
                        $"共 {dg.Count()} 项（按提醒时间先后）",
                        dg.ToList());
                }
            }
        }
    }

    private static string BuildArticleInner(BlogArticle article, string bodyHtml, ArticleVersionDocument versionDoc,
        SiteFileNames names)
    {
        var articlePath = article.HtmlFileName;
        var c = article.Created.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var m = article.Modified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var revAside = BuildRevisionAside(versionDoc);

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"article-page\">");
        sb.AppendLine("<div class=\"article-header\">");
        var home = SitePathHelper.RelFromTo(articlePath, "index.html");
        sb.Append("<a class=\"crumb\" href=\"").Append(WebUtility.HtmlEncode(home)).AppendLine("\">首页</a>");
        for (var i = 0; i < article.Bookmarks.Count; i++)
        {
            sb.AppendLine(" <span class=\"crumb-sep\">·</span> ");
            var bm = article.Bookmarks[i];
            var tagHref = SitePathHelper.RelFromTo(articlePath, names.TagPageFile(bm));
            sb.Append("<a class=\"crumb\" href=\"").Append(WebUtility.HtmlEncode(tagHref))
                .Append("\">").Append(WebUtility.HtmlEncode(bm)).Append("</a>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("<header class=\"article-title-block\">");
        sb.Append("<h1>").Append(WebUtility.HtmlEncode(article.Title)).AppendLine("</h1>");
        sb.Append("<p class=\"article-meta-line\">");
        if (article.ReminderAt.HasValue)
        {
            var planLocal = article.ReminderAt.Value.ToLocalTime().ToString("yyyy年M月d日 HH:mm");
            sb.Append("计划 <strong class=\"article-plan-time\">")
                .Append(WebUtility.HtmlEncode(planLocal))
                .Append("</strong> ");
        }

        sb.Append("「").Append(WebUtility.HtmlEncode(article.NotebookTitle)).Append("」·节点路径 ")
            .Append(WebUtility.HtmlEncode(article.StructuralSection))
            .Append(" · 最后修改 ").Append(m)
            .Append(" · 节点创建 ").Append(c)
            .AppendLine("</p>");
        sb.AppendLine("</header>");

        sb.AppendLine("<article class=\"content\">");
        sb.Append(bodyHtml);
        sb.AppendLine("</article>");
        sb.Append(revAside);
        sb.AppendLine("</div>");

        return sb.ToString();
    }

    private static string BuildRevisionAside(ArticleVersionDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<details class=\"rev-dock\">");
        sb.AppendLine("<summary class=\"rev-dock-summary\"><span class=\"rev-dock-title\">修订与对比</span></summary>");
        sb.AppendLine("<div class=\"rev-dock-panel-wrap\">");
        sb.AppendLine("<aside class=\"rev-aside\" aria-label=\"版本记录\">");
        var revTimes = Math.Max(0, doc.Versions.Count - 1);
        sb.Append("<p class=\"rev-summary\">已修订 <strong>").Append(revTimes)
            .Append("</strong> 次 · 共 <strong>").Append(doc.Versions.Count)
            .AppendLine("</strong> 条快照</p>");

        if (doc.Versions.Count >= 2)
        {
            var latest = doc.Versions[^1];
            sb.Append("<p class=\"rev-latest\">最近一次相对上一版：");
            AppendStatPills(sb, latest.CharsAdded, latest.CharsRemoved, latest.CharsModifiedEstimate);
            sb.AppendLine("</p>");
        }

        sb.AppendLine("<div class=\"rev-list\">");
        for (var i = doc.Versions.Count - 1; i >= 0; i--)
        {
            var v = doc.Versions[i];
            var gen = v.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            sb.AppendLine("<details class=\"rev-item\">");
            if (i == 0)
            {
                sb.Append("<summary><span class=\"rev-sum-text\">").Append(WebUtility.HtmlEncode(gen))
                    .Append(" · 首发快照</span>");
                AppendStatPills(sb, v.CharsAdded, v.CharsRemoved, v.CharsModifiedEstimate);
                sb.Append("<span class=\"rev-sum-hint\">展开正文</span></summary>");
                sb.AppendLine("<div class=\"rev-body\">");
                sb.AppendLine("<pre class=\"snapshot-pre\">");
                sb.Append(WebUtility.HtmlEncode(v.PlainTextSnapshot));
                sb.AppendLine("</pre></div>");
            }
            else
            {
                sb.Append("<summary><span class=\"rev-sum-text\">").Append(WebUtility.HtmlEncode(gen))
                    .Append(" · 相对上一版</span>");
                AppendStatPills(sb, v.CharsAdded, v.CharsRemoved, v.CharsModifiedEstimate);
                sb.Append("<span class=\"rev-sum-hint\">展开差异</span></summary>");
                sb.AppendLine("<div class=\"rev-body diff-rev-body\">");
                sb.Append(v.DiffHtmlAgainstPrevious);
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</details>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("</aside>");
        sb.AppendLine("</div>");
        sb.AppendLine("</details>");
        return sb.ToString();
    }

    private static void AppendStatPills(StringBuilder sb, int added, int removed, int mod)
    {
        sb.Append(" <span class=\"version-stat version-stat-add\" title=\"增加字数\">+").Append(added).Append("</span>");
        sb.Append(" <span class=\"version-stat version-stat-del\" title=\"删除字数\">−").Append(removed).Append("</span>");
        sb.Append(" <span class=\"version-stat version-stat-mod\" title=\"估算的替换字数：同一快照内既有删除又有新增的位置，按字数近似统计（与单纯的加、减不完全相同）\">替换")
            .Append(mod).Append("</span>");
    }

    private static string BuildExcerpt(BlogArticle article)
    {
        var first = article.Blocks.OfType<ParagraphBlock>().FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(first))
            return "";
        first = first.Trim();
        return first.Length <= 180 ? first : first[..180] + "…";
    }

    /// <summary>与首页相同的「日期 + 时刻 + 卡片」时间轴条；<paramref name="selectLocalTime"/> 决定左侧一列的时间（修改或提醒）。</summary>
    private static void AppendTimelineList(
        StringBuilder sb,
        IReadOnlyList<BlogArticle> arts,
        Func<BlogArticle, DateTime> selectLocalTime,
        bool descending,
        SiteFileNames names,
        string? timelinePageWebPath)
    {
        IEnumerable<BlogArticle> ordered = descending
            ? arts.OrderByDescending(selectLocalTime)
            : arts.OrderBy(selectLocalTime);

        sb.AppendLine("<ol class=\"timeline timeline-page\">");
        DateTime? prevDate = null;
        foreach (var art in ordered)
        {
            var local = selectLocalTime(art);
            var excerpt = BuildExcerpt(art);
            var isSameDate = prevDate.HasValue && prevDate.Value.Date == local.Date;
            prevDate = local.Date;
            sb.AppendLine("<li class=\"timeline-item\">");
            sb.Append("<div class=\"timeline-lead\">");
            sb.Append("<time class=\"timeline-datetime\" datetime=\"")
                .Append(WebUtility.HtmlEncode(local.ToString("yyyy-MM-ddTHH:mm:ss")))
                .Append("\">");
            if (isSameDate)
                sb.AppendLine("<span class=\"timeline-date timeline-date-repeat\" aria-hidden=\"true\"></span>");
            else
                sb.Append("<span class=\"timeline-date\">").Append(WebUtility.HtmlEncode(local.ToString("yyyy年M月d日")))
                    .AppendLine("</span>");
            sb.AppendLine("<div class=\"timeline-clock-row\">");
            sb.Append("<span class=\"timeline-clock\">").Append(WebUtility.HtmlEncode(local.ToString("HH:mm")))
                .AppendLine("</span>");
            sb.AppendLine("<span class=\"timeline-marker\" aria-hidden=\"true\"><span class=\"timeline-dot\"></span></span>");
            sb.AppendLine("</div>");
            sb.AppendLine("</time></div>");
            sb.AppendLine("<article class=\"timeline-card\">");
            var artHref = SitePathHelper.RelFromTo(timelinePageWebPath, art.HtmlFileName);
            sb.AppendLine("<div class=\"timeline-head\">");
            sb.Append("<h2 class=\"timeline-title\"><a href=\"")
                .Append(WebUtility.HtmlEncode(artHref)).Append("\">")
                .Append(WebUtility.HtmlEncode(art.Title))
                .AppendLine("</a></h2>");
            sb.Append("<div class=\"timeline-bm\">");
            foreach (var bm in art.Bookmarks)
            {
                var tm = SitePathHelper.RelFromTo(timelinePageWebPath, names.TagPageFile(bm));
                sb.Append("<a class=\"bm-pill sm\" href=\"")
                    .Append(WebUtility.HtmlEncode(tm)).Append("\">")
                    .Append(WebUtility.HtmlEncode(bm)).Append("</a>");
            }

            sb.AppendLine("</div></div>");
            if (!string.IsNullOrEmpty(excerpt))
                sb.Append("<p class=\"timeline-excerpt\">").Append(WebUtility.HtmlEncode(excerpt)).AppendLine("</p>");

            sb.AppendLine("</article>");
            sb.AppendLine("</li>");
        }

        sb.AppendLine("</ol>");
    }

    private static void WriteIndex(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles, SiteFileNames names,
        IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        const string idx = "index.html";
        var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, idx, names);
        var tagsAside = HtmlLayout.BuildRightAside(sortedArticles, null, names, idx, galleryEntries, avatarSitePath);

        var center = new StringBuilder();
        center.AppendLine("<div class=\"page-index\">");
        center.AppendLine("<header class=\"hero\">");
        center.AppendLine("<h1 class=\"page-title\">时间轴</h1>");
        center.AppendLine("<p class=\"page-lead\">按<strong>文章节点</strong>最后修改时间排序；左侧为日期与时间。书签：明细里 <code>#话题</code>；未写 # 时用图册根名称归类。</p>");
        center.AppendLine("</header>");

        AppendTimelineList(center, sortedArticles, a => a.Modified.LocalDateTime, descending: true, names, idx);
        center.AppendLine("</div>");

        var html = HtmlLayout.BuildDocument("思维导图博客 · 时间轴", "", center.ToString(), nav, tagsAside, idx,
            names.RssFeedWebPath,
            names);
        WriteUtf8Web(outDir, idx, html);
    }

    private static void WriteTagPages(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var bookmarkNames = HtmlLayout.CountBookmarks(sortedArticles).Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var tag in bookmarkNames)
        {
            var inTag = sortedArticles.Where(a =>
                a.Bookmarks.Any(b => string.Equals(b, tag, StringComparison.OrdinalIgnoreCase))).ToList();
            if (inTag.Count == 0)
                continue;

            var fileName = names.TagPageFile(tag);
            var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, fileName, names);
            var tagsAside = HtmlLayout.BuildRightAside(sortedArticles, tag, names, fileName, galleryEntries, avatarSitePath);

            var inner = new StringBuilder();
            inner.AppendLine("<div class=\"page-tag page-with-timeline\">");
            inner.AppendLine("<header class=\"hero\">");
            inner.Append("<h1 class=\"page-title\">书签：").Append(WebUtility.HtmlEncode(tag)).AppendLine("</h1>");
            inner.Append("<p class=\"page-lead\">共 <strong>").Append(inTag.Count)
                .AppendLine("</strong> 篇文章（按导图修改时间倒序）</p>");
            inner.AppendLine("</header>");
            AppendTimelineList(inner, inTag, a => a.Modified.LocalDateTime, descending: true, names, fileName);
            inner.AppendLine("</div>");

            var page = HtmlLayout.BuildDocument($"书签：{tag}", "", inner.ToString(), nav, tagsAside, fileName,
                names.RssFeedWebPath,
                names);
            WriteUtf8Web(outDir, fileName, page);
        }
    }

    private static void WriteGalleryPage(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var webPath = names.GalleryPageWebPath;
        var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, webPath, names);
        var aside = HtmlLayout.BuildRightAside(sortedArticles, null, names, webPath, galleryEntries, avatarSitePath);
        var inner = HtmlLayout.BuildGalleryPageMain(galleryEntries, sortedArticles, webPath);
        var page = HtmlLayout.BuildDocument("图册", "", inner, nav, aside, webPath, names.RssFeedWebPath, names);
        WriteUtf8Web(outDir, webPath, page);
    }

    private static void WriteAboutPage(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var webPath = names.AboutPageWebPath;
        var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, webPath, names);
        var aside = HtmlLayout.BuildRightAside(sortedArticles, null, names, webPath, galleryEntries, avatarSitePath);

        var inner = new StringBuilder();
        inner.AppendLine("<div class=\"page-about\">");
        inner.AppendLine("<header class=\"hero hero-about\">");
        if (!string.IsNullOrEmpty(avatarSitePath))
        {
            var imgSrc = SitePathHelper.RelFromTo(webPath, avatarSitePath);
            inner.AppendLine("<div class=\"about-avatar-wrap\">");
            inner.Append("<img class=\"about-avatar\" src=\"").Append(WebUtility.HtmlEncode(imgSrc))
                .Append("\" alt=\"\" width=\"120\" height=\"120\" decoding=\"async\"/>");
            inner.AppendLine("</div>");
        }

        inner.AppendLine("<h1 class=\"page-title\">关于我</h1>");
        inner.Append("<p class=\"page-lead page-about-signature\">")
            .Append(WebUtility.HtmlEncode(SiteProfile.Signature))
            .AppendLine("</p>");
        inner.AppendLine("</header>");
        inner.AppendLine("<div class=\"about-body\">");
        inner.AppendLine("<p>欢迎来到本站。以上是我在此处展示的签名与态度；站内文章与导图仅代表学习与记录。</p>");
        inner.AppendLine("</div>");
        inner.AppendLine("</div>");

        var page = HtmlLayout.BuildDocument("关于我", "", inner.ToString(), nav, aside, webPath,
            names.RssFeedWebPath,
            names);
        WriteUtf8Web(outDir, webPath, page);
    }

    private static void WriteSearchPage(string outDir, string scanRoot, IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var webPath = names.SearchPageWebPath;
        var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, webPath, names);
        var aside = HtmlLayout.BuildRightAside(sortedArticles, null, names, webPath, galleryEntries, avatarSitePath);
        var inner = HtmlLayout.BuildSearchPageMain(webPath, names.SearchPageWebPath);
        var page = HtmlLayout.BuildDocument("搜索", "", inner, nav, aside, webPath, names.RssFeedWebPath, names);
        WriteUtf8Web(outDir, webPath, page);
    }

    private static void WriteWordFrequencyPage(string outDir, string scanRoot,
        IReadOnlyList<BlogArticle> sortedArticles,
        SiteFileNames names, IReadOnlyList<ArticleGalleryItem> galleryEntries, string? avatarSitePath)
    {
        var scanRootFull = Path.GetFullPath(scanRoot);
        var webPath = names.WordFrequencyPageWebPath;
        var nav = HtmlLayout.BuildLeftNavTree(sortedArticles, scanRootFull, null, webPath, names);
        var aside = HtmlLayout.BuildRightAside(sortedArticles, null, names, webPath, galleryEntries, avatarSitePath);
        var stats = WordFrequencyService.Compute(sortedArticles, maxTerms: 180);
        var inner = BuildWordFrequencyInner(stats);
        var page = HtmlLayout.BuildDocument("词频", "", inner, nav, aside, webPath, names.RssFeedWebPath, names);
        WriteUtf8Web(outDir, webPath, page);
    }

    private static string BuildWordFrequencyInner(WordFrequencyResult stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"page-wordfreq\">");
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<h1 class=\"page-title\">词频</h1>");
        sb.AppendLine(
            "<p class=\"page-lead\">基于全部文章的标题、正文段落、图册说明与书签文本；中文使用 jieba 精确模式分词，并过滤常见虚词（停用词表）。气泡大小表示相对频次。</p>");
        sb.AppendLine("</header>");

        sb.Append("<p class=\"wordfreq-stats\">")
            .Append(stats.ArticleCount.ToString(CultureInfo.InvariantCulture)).Append(" 篇文章 · ")
            .Append(stats.TotalTokenOccurrences.ToString(CultureInfo.InvariantCulture)).Append(" 次词命中 · ")
            .Append(stats.UniqueTokens.ToString(CultureInfo.InvariantCulture)).Append(" 个不同词形 · 本页列出前 ")
            .Append(stats.TopTerms.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个高频词</p>");

        if (stats.TopTerms.Count == 0)
        {
            sb.AppendLine("<p class=\"wordfreq-empty\">暂无可用正文，无法生成词频。</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        static double Weight(int count, int minC, int maxC)
        {
            if (maxC <= minC)
                return 0.55;
            var lc = Math.Log(Math.Max(1, count));
            var lo = Math.Log(Math.Max(1, minC));
            var hi = Math.Log(Math.Max(1, maxC));
            return Math.Clamp((lc - lo) / (hi - lo), 0, 1);
        }

        sb.AppendLine("<div class=\"wordfreq-cloud\" aria-label=\"词频标签云\">");
        foreach (var t in stats.TopTerms)
        {
            var wf = Weight(t.Count, stats.MinCount, stats.MaxCount).ToString("0.###", CultureInfo.InvariantCulture);
            sb.Append("<span class=\"wordfreq-chip\" style=\"--wf:").Append(wf).Append("\">")
                .Append(WebUtility.HtmlEncode(t.Token)).AppendLine("</span>");
        }

        sb.AppendLine("</div>");

        sb.AppendLine("<section class=\"wordfreq-chart\" aria-label=\"高频词条形图\">");
        sb.AppendLine("<h2 class=\"wordfreq-chart-title\">高频词排行</h2>");
        var chart = stats.TopTerms.Take(28).ToList();
        var maxBar = chart[0].Count;
        foreach (var t in chart)
        {
            var pct = maxBar <= 0
                ? 0
                : Math.Min(100, Math.Max(3, (int)Math.Round(100.0 * t.Count / maxBar)));
            sb.AppendLine("<div class=\"wordfreq-row\">");
            sb.Append("<span class=\"wordfreq-label\">").Append(WebUtility.HtmlEncode(t.Token)).AppendLine("</span>");
            sb.Append("<span class=\"wordfreq-bar-wrap\"><span class=\"wordfreq-bar\" style=\"width:")
                .Append(pct.ToString(CultureInfo.InvariantCulture)).Append("%\"></span></span>");
            sb.Append("<span class=\"wordfreq-n\">").Append(t.Count.ToString(CultureInfo.InvariantCulture))
                .AppendLine("</span></div>");
        }

        sb.AppendLine("</section>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static List<CopiedArticleImage> CopyArticleImages(BlogArticle article, string mediaRoot)
    {
        var list = new List<CopiedArticleImage>();
        var prefix = ArticleIdentity.ComputeStorageKey(article.SourceMmPath, article.ArticleNodeId);
        var index = 0;
        foreach (var block in article.Blocks)
        {
            if (block is not ImageBlock img || string.IsNullOrEmpty(img.ResolvedSourcePath))
                continue;
            if (!File.Exists(img.ResolvedSourcePath))
                continue;

            var orig = Path.GetFileName(img.ResolvedSourcePath);
            var safe = $"{prefix}_{index}_{orig}";
            var dest = Path.Combine(mediaRoot, safe);
            File.Copy(img.ResolvedSourcePath, dest, overwrite: true);
            var mediaFromRoot = "media/" + safe.Replace('\\', '/');
            var url = SitePathHelper.RelFromTo(article.HtmlFileName, mediaFromRoot);
            list.Add(new CopiedArticleImage(img, url, mediaFromRoot, index));
            index++;
        }

        return list;
    }

    private static string RenderBodyHtml(BlogArticle article, IReadOnlyList<CopiedArticleImage> imageRefs)
    {
        var sb = new StringBuilder();
        foreach (var block in article.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    sb.Append("<p>").Append(WebUtility.HtmlEncode(p.Text)).AppendLine("</p>");
                    break;
                case ImageBlock img:
                    var match = imageRefs.FirstOrDefault(t => ReferenceEquals(t.Block, img));
                    if (string.IsNullOrEmpty(match?.UrlRelativeToArticle))
                    {
                        sb.Append("<p class=\"missing\">图片缺失：")
                            .Append(WebUtility.HtmlEncode(img.RelativeUri))
                            .AppendLine("</p>");
                        break;
                    }

                    sb.Append("<figure class=\"article-figure\" id=\"img-")
                        .Append(match.IndexInArticle)
                        .Append("\"><img src=\"")
                        .Append(WebUtility.HtmlEncode(match.UrlRelativeToArticle))
                        .Append("\" alt=\"")
                        .Append(WebUtility.HtmlEncode(img.AltText))
                        .Append("\" loading=\"lazy\"/><figcaption>")
                        .Append(WebUtility.HtmlEncode(img.AltText))
                        .AppendLine("</figcaption></figure>");
                    break;
            }
        }

        return sb.ToString();
    }

    private static void WriteStylesheet(string path)
    {
        var css = """
:root {
  --layout-shell-max: 1680px;
  /* 与顶栏 .site-topbar-inner 一致：三栏左右缘与「思维导图博客」「夜间」按钮对齐，中间栏可用宽度固定 */
  --layout-shell-pad-x: clamp(0.85rem, 2vw, 1.35rem);
  --layout-col-tags-max: 234px;
  /* 主栏内容区最大 1138px，列更窄时随列宽 */
  --layout-main-max: min(1138px, 100%);

  font-family: "Noto Sans SC", "Segoe UI", "PingFang SC", "Microsoft YaHei", system-ui, sans-serif;
  line-height: 1.68;
  letter-spacing: 0.01em;
  font-feature-settings: "kern" 1;
  -webkit-font-smoothing: antialiased;
  color: var(--text-primary);

  --text-primary: #151c28;
  --text-muted: #5c6578;
  --text-soft: #8b93a7;

  --surface-page: #ebe8e3;
  --surface-main: radial-gradient(ellipse 95% 55% at 50% -15%, rgba(255, 255, 255, 0.92) 0%, transparent 52%),
    linear-gradient(175deg, #faf9f7 0%, #f4f2ee 48%, #ebe8e3 100%);
  --surface-nav: linear-gradient(165deg, #fafaf9 0%, #f3f1ec 42%, #eae7e1 100%);
  --surface-aside: linear-gradient(188deg, #fdfcfd 0%, #f9f7fc 38%, #f3effa 100%);
  --surface-card: #ffffff;

  --border: rgba(21, 28, 40, 0.09);
  --border-focus: rgba(67, 56, 202, 0.22);

  --accent: #4338ca;
  --accent-deep: #3730a3;
  --accent-soft: rgba(67, 56, 202, 0.11);
  --accent-glow: rgba(129, 140, 248, 0.35);

  --radius-sm: 8px;
  --radius-md: 14px;
  --radius-lg: 18px;

  --shadow-sm: 0 1px 4px rgba(21, 28, 40, 0.05);
  --shadow-nav: inset -1px 0 0 rgba(255, 255, 255, 0.65), 4px 0 28px rgba(21, 28, 40, 0.045);
  --shadow-aside: inset 1px 0 0 rgba(255, 255, 255, 0.55), -4px 0 28px rgba(21, 28, 40, 0.04);
  --shadow-card: 0 14px 42px rgba(21, 28, 40, 0.075), 0 4px 14px rgba(21, 28, 40, 0.035);
  --shadow-float: 0 22px 56px rgba(21, 28, 40, 0.1);

  --site-topbar-h: 44px;
  --site-footer-h: 46px;
  /* 固定底栏总占位（与 .site-footer-inner 的上下 padding + min-height 一致），侧栏可视高度与正文底部留白共用 */
  --site-footer-occupy: calc(var(--site-footer-h) + 0.76rem);
}

* { box-sizing: border-box; }

/* 减少「有/无纵向滚动条」时主栏可用宽度来回变；与 footbar 一起调视觉时建议保留 */
html {
  scrollbar-gutter: stable;
}

body.site-body {
  margin: 0;
  min-height: 100vh;
  /* 固定 footbar 不占文档流，留出底部避免正文滚到栏下；安全区计入 iPhone 横条 */
  padding-bottom: calc(var(--site-footer-occupy) + env(safe-area-inset-bottom, 0px));
  background: var(--surface-page);
  color: var(--text-primary);
  display: flex;
  flex-direction: column;
}

.site-topbar {
  position: sticky;
  top: 0;
  z-index: 300;
  background: rgba(250, 249, 247, 0.94);
  border-bottom: 1px solid var(--border);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
}

.site-topbar-inner {
  max-width: var(--layout-shell-max);
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.65rem;
  flex-wrap: wrap;
  padding: 0.35rem var(--layout-shell-pad-x);
  min-height: var(--site-topbar-h);
}

.site-topbar-brand {
  font-weight: 700;
  font-size: clamp(1.06rem, 2.6vw, 1.26rem);
  letter-spacing: 0.12em;
  text-decoration: none;
  white-space: nowrap;
  color: var(--text-primary);
}

@supports ((-webkit-background-clip: text) or (background-clip: text)) {
  .site-topbar-brand {
    background-image: linear-gradient(
      118deg,
      var(--text-primary) 0%,
      var(--accent-deep) 40%,
      var(--accent) 100%
    );
    -webkit-background-clip: text;
    background-clip: text;
    color: transparent;
  }
}

.site-topbar-brand:hover {
  filter: brightness(1.08);
}

@supports ((-webkit-background-clip: text) or (background-clip: text)) {
  .site-topbar-brand:hover {
    background-image: linear-gradient(118deg, var(--accent-deep) 0%, var(--accent) 70%, var(--accent) 100%);
    filter: none;
  }
}

.site-topbar-controls {
  display: flex;
  align-items: center;
  gap: 0.45rem;
  flex-wrap: wrap;
}

.site-topbar-swatches {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
}

.site-topbar-swatch {
  width: 22px;
  height: 22px;
  padding: 0;
  border-radius: 999px;
  border: 1px solid rgba(21, 28, 40, 0.18);
  background: var(--site-swatch);
  cursor: pointer;
  vertical-align: middle;
  transition: transform 0.12s ease, box-shadow 0.12s ease;
}

.site-topbar-swatch:hover {
  transform: scale(1.06);
  box-shadow: 0 0 0 2px var(--accent-soft);
}

.site-topbar-swatch:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.site-topbar-color-label {
  display: inline-flex;
  align-items: center;
  cursor: pointer;
}

.site-topbar-color-input {
  width: 28px;
  height: 28px;
  padding: 0;
  border: 1px solid rgba(21, 28, 40, 0.15);
  border-radius: 8px;
  background: transparent;
  cursor: pointer;
}

.site-topbar-color-input::-webkit-color-swatch-wrapper {
  padding: 2px;
}

.site-topbar-color-input::-webkit-color-swatch {
  border: none;
  border-radius: 5px;
}

.site-topbar-chip {
  margin: 0;
  padding: 0.22rem 0.62rem;
  font: inherit;
  font-size: 0.76rem;
  font-weight: 600;
  letter-spacing: 0.06em;
  color: var(--text-muted);
  background: var(--surface-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  cursor: pointer;
  transition: border-color 0.15s ease, background 0.15s ease, color 0.15s ease;
}

.site-topbar-chip:hover {
  border-color: rgba(67, 56, 202, 0.32);
  color: var(--text-primary);
}

html.theme-dark .site-topbar-chip:hover {
  border-color: rgba(165, 180, 252, 0.45);
}

.site-topbar-chip.is-active {
  border-color: var(--accent);
  color: var(--accent-deep);
  background: var(--accent-soft);
}

.site-topbar-chip:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.site-footer {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  flex-shrink: 0;
  margin-top: 0;
  z-index: 295;
  background: rgba(250, 249, 247, 0.94);
  border-top: 1px solid var(--border);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  padding-bottom: env(safe-area-inset-bottom, 0px);
}

.site-footer-inner {
  max-width: var(--layout-shell-max);
  margin: 0 auto;
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
  align-items: center;
  gap: 0.45rem 0.85rem;
  padding: 0.38rem var(--layout-shell-pad-x);
  min-height: var(--site-footer-h);
}

.site-footer-leading {
  min-width: 0;
}

.site-footer-copy {
  margin: 0;
  justify-self: center;
  text-align: center;
  font-size: 0.74rem;
  color: var(--text-muted);
  letter-spacing: 0.04em;
}

.site-footer-inner > .site-footer-links {
  justify-self: end;
}

html.theme-bw {
  filter: grayscale(1) contrast(1.02);
}

html.theme-bw .site-topbar-color-input {
  opacity: 0.85;
}

html.theme-dark {
  color-scheme: dark;
  --text-primary: #e8ecf4;
  --text-muted: #9aa4b8;
  --text-soft: #788299;

  --surface-page: #10131a;
  --surface-main:
    radial-gradient(ellipse 95% 52% at 50% -14%, rgba(76, 91, 150, 0.38) 0%, transparent 52%),
    linear-gradient(176deg, #171b26 0%, #131822 46%, #10131a 100%);
  --surface-nav: linear-gradient(166deg, #1a1f2c 0%, #161b28 44%, #121724 100%);
  --surface-aside: linear-gradient(188deg, #1e1a2a 0%, #1a1624 38%, #15131c 100%);
  --surface-card: #1e2433;

  --border: rgba(255, 255, 255, 0.09);
  --border-focus: rgba(165, 180, 252, 0.45);

  --shadow-sm: 0 1px 4px rgba(0, 0, 0, 0.38);
  --shadow-nav:
    inset -1px 0 0 rgba(255, 255, 255, 0.05),
    4px 0 28px rgba(0, 0, 0, 0.28);
  --shadow-aside:
    inset 1px 0 0 rgba(255, 255, 255, 0.05),
    -4px 0 28px rgba(0, 0, 0, 0.26);
  --shadow-card: 0 14px 42px rgba(0, 0, 0, 0.42), 0 4px 14px rgba(0, 0, 0, 0.28);
  --shadow-float: 0 22px 56px rgba(0, 0, 0, 0.55);
}

html.theme-dark .site-topbar {
  background: rgba(16, 19, 27, 0.94);
  border-bottom-color: var(--border);
}

html.theme-dark .site-footer {
  background: rgba(16, 19, 27, 0.94);
  border-top-color: var(--border);
}

html.theme-dark .site-topbar-swatch {
  border-color: rgba(255, 255, 255, 0.26);
}

html.theme-dark .site-topbar-color-input {
  border-color: rgba(255, 255, 255, 0.22);
}

html.theme-dark .rev-dock {
  filter: drop-shadow(0 10px 26px rgba(0, 0, 0, 0.55));
}

html.theme-dark .rev-dock-summary {
  background: linear-gradient(166deg, #252b3c 0%, #1c2230 100%);
  border-color: var(--border);
  color: var(--accent);
}

html.theme-dark .rev-aside {
  background: linear-gradient(166deg, #252b3c 0%, #1e2434 42%, #1a1f2e 100%);
  border-color: var(--border);
}

html.theme-dark .rev-summary {
  color: var(--text-muted);
}

html.theme-dark .rev-latest {
  color: #93c5fd;
}

html.theme-dark .rev-item {
  background: rgba(22, 26, 38, 0.92);
  border-color: var(--border);
}

html.theme-dark .rev-item summary {
  color: var(--text-primary);
}

html.theme-dark .rev-sum-hint {
  color: var(--text-soft);
}

html.theme-dark .version-stat-add {
  background: rgba(22, 163, 74, 0.22);
  color: #86efac;
  border-color: rgba(34, 197, 94, 0.35);
}

html.theme-dark .version-stat-del {
  background: rgba(220, 38, 38, 0.22);
  color: #fca5a5;
  border-color: rgba(248, 113, 113, 0.35);
}

html.theme-dark .version-stat-mod {
  background: rgba(217, 119, 6, 0.22);
  color: #fcd34d;
  border-color: rgba(251, 191, 36, 0.38);
}

html.theme-dark .rev-body {
  border-top-color: var(--border);
}

html.theme-dark .diff-rev-body {
  background: rgba(14, 17, 24, 0.96);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

html.theme-dark .snapshot-pre {
  color: var(--text-muted);
}

html.theme-dark .diff-ins {
  background: rgba(34, 197, 94, 0.28);
  color: #bbf7d0;
}

html.theme-dark .diff-del {
  background: rgba(239, 68, 68, 0.26);
  color: #fecaca;
}

html.theme-dark .diff-same {
  color: var(--text-muted);
}

/* 夜间：侧栏「计划日期」「关于我」「全文搜索」等原为浅色硬编码的背景与边框 */
html.theme-dark .nav-cal-section,
html.theme-dark .nav-filetree-section {
  background: linear-gradient(160deg, rgba(34, 40, 54, 0.92) 0%, rgba(26, 30, 42, 0.82) 100%);
  box-shadow: 0 2px 14px rgba(0, 0, 0, 0.32);
  border-color: var(--border);
}

html.theme-dark .nav-cal-root > details.nav-cal-year {
  border-left-color: #5c6f86;
}

html.theme-dark .nav-cal-section details.nav-cal-month {
  border-left-color: #6f7f94;
}

html.theme-dark .nav-cal-section details.nav-cal-day {
  border-left-color: #8b98ab;
}

html.theme-dark .nav-cal-year > summary.nav-cal-summary {
  color: var(--text-primary);
}

html.theme-dark .nav-cal-month > summary.nav-cal-summary {
  color: var(--text-primary);
}

html.theme-dark .nav-cal-day > summary.nav-cal-summary {
  color: var(--text-primary);
}

html.theme-dark .nav-cal-time {
  color: var(--text-soft);
}

html.theme-dark .nav-cal-visual {
  border-top-color: rgba(148, 163, 184, 0.42);
}

html.theme-dark .nav-cal-mode-switch {
  background: rgba(22, 26, 36, 0.94);
  border-color: rgba(165, 180, 252, 0.28);
}

html.theme-dark .nav-cal-step-btn {
  background: rgba(28, 34, 48, 0.96);
  border-color: var(--border);
}

html.theme-dark .nav-cal-step-btn:hover {
  border-color: rgba(165, 180, 252, 0.42);
  background: rgba(99, 102, 241, 0.14);
}

html.theme-dark .nav-cal-select {
  background: var(--surface-card);
  color: var(--text-primary);
  border-color: var(--border);
}

html.theme-dark .nav-cal-month-card {
  background: rgba(28, 34, 48, 0.78);
  border-color: rgba(255, 255, 255, 0.12);
}

html.theme-dark .aside-profile-card {
  background: linear-gradient(
    155deg,
    rgba(34, 40, 54, 0.97) 0%,
    rgba(28, 32, 46, 0.95) 52%,
    rgba(22, 26, 38, 0.93) 100%
  );
  border-color: rgba(165, 180, 252, 0.26);
  box-shadow:
    0 6px 22px rgba(0, 0, 0, 0.38),
    0 2px 12px rgba(0, 0, 0, 0.24),
    inset 0 1px 0 rgba(255, 255, 255, 0.06);
}

html.theme-dark .aside-profile-card:hover {
  box-shadow:
    0 12px 36px rgba(0, 0, 0, 0.48),
    0 4px 16px rgba(0, 0, 0, 0.28);
  border-color: rgba(165, 180, 252, 0.42);
}

html.theme-dark .aside-profile-avatar {
  border-color: rgba(30, 36, 52, 0.98);
  box-shadow:
    0 6px 22px rgba(0, 0, 0, 0.45),
    inset 0 1px 0 rgba(255, 255, 255, 0.07);
}

html.theme-dark .aside-profile-placeholder {
  border-color: rgba(165, 180, 252, 0.38);
  background: linear-gradient(145deg, rgba(99, 102, 241, 0.18) 0%, rgba(124, 58, 237, 0.16) 100%);
}

html.theme-dark .search-aside-wrap,
html.theme-dark .tag-aside-inner,
html.theme-dark .gallery-aside-inner {
  background: linear-gradient(162deg, rgba(34, 40, 54, 0.95) 0%, rgba(26, 30, 42, 0.93) 100%);
  box-shadow: 0 4px 22px rgba(0, 0, 0, 0.38);
  border-color: var(--border);
}

html.theme-dark .search-aside-input {
  background: rgba(22, 26, 38, 0.96);
  border-color: rgba(165, 180, 252, 0.32);
  color: var(--text-primary);
}

html.theme-dark .search-aside-input:focus {
  border-color: rgba(165, 180, 252, 0.58);
}

html.theme-dark .search-aside-list {
  border-top-color: rgba(255, 255, 255, 0.09);
}

html.theme-dark .search-hit-item {
  border-bottom-color: rgba(255, 255, 255, 0.09);
}

html.theme-dark .search-hit-link:hover {
  background: rgba(99, 102, 241, 0.14);
}

html.theme-dark .tag-cloud-link:hover {
  background: rgba(38, 44, 58, 0.92);
  border-color: rgba(165, 180, 252, 0.22);
}

html.theme-dark .gen-aside-main-link {
  background: rgba(28, 34, 48, 0.88);
  border-color: rgba(165, 180, 252, 0.26);
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.28);
}

html.theme-dark .gen-aside-main-link:hover {
  border-color: rgba(165, 180, 252, 0.42);
}

html.theme-dark .gen-aside-rss-link {
  color: #fdba74;
  border-color: rgba(251, 146, 60, 0.38);
  background: linear-gradient(165deg, rgba(58, 32, 18, 0.72) 0%, rgba(42, 26, 14, 0.68) 100%);
}

html.theme-dark .gen-aside-main-link:hover,
html.theme-dark .gen-aside-rss-link:hover {
  box-shadow: 0 6px 18px rgba(0, 0, 0, 0.35);
}

html.theme-dark .gen-history-page-size {
  background: var(--surface-card);
}

html.theme-dark .gen-history-page-btn {
  background: rgba(28, 34, 48, 0.94);
  border-color: rgba(165, 180, 252, 0.32);
}

html.theme-dark .bm-pill {
  background: rgba(28, 34, 48, 0.94);
  color: var(--accent);
  border-color: rgba(165, 180, 252, 0.32);
  box-shadow: 0 1px 5px rgba(0, 0, 0, 0.35);
}

html.theme-dark .bm-pill:hover {
  background: rgba(42, 48, 64, 0.98);
  border-color: rgba(165, 180, 252, 0.48);
}

.layout-shell {
  display: grid;
  grid-template-columns: minmax(234px, 272px) minmax(0, 1fr) minmax(196px, 234px);
  flex: 1 1 auto;
  min-height: calc(100vh - var(--site-topbar-h) - var(--site-footer-occupy));
  max-width: var(--layout-shell-max);
  margin: 0 auto;
  padding-left: var(--layout-shell-pad-x);
  padding-right: var(--layout-shell-pad-x);
}

.layout-nav {
  background: var(--surface-nav);
  border-right: 1px solid var(--border);
  padding: 1rem 0.65rem 1.75rem 0.95rem;
  overflow: auto;
  max-height: calc(100vh - var(--site-topbar-h) - var(--site-footer-occupy));
  position: sticky;
  top: var(--site-topbar-h);
  box-shadow: var(--shadow-nav);
}

.layout-main {
  box-sizing: border-box;
  min-width: 0;
  width: 100%;
  max-width: var(--layout-main-max);
  justify-self: center;
  padding: 1.55rem clamp(1.15rem, 3vw, 2.1rem) 3.25rem;
  overflow: hidden;
  background: var(--surface-main);
}

.layout-tags {
  background: var(--surface-aside);
  border-left: 1px solid var(--border);
  padding: 1rem 0.85rem 1.6rem 0.85rem;
  overflow: auto;
  max-height: calc(100vh - var(--site-topbar-h) - var(--site-footer-occupy));
  position: sticky;
  top: var(--site-topbar-h);
  display: flex;
  flex-direction: column;
  min-height: min(calc(100vh - var(--site-topbar-h) - var(--site-footer-occupy)), 100%);
  box-shadow: var(--shadow-aside);
}

.right-aside-stack {
  display: flex;
  flex-direction: column;
  gap: 0;
  flex: 1;
  min-height: 0;
}

.aside-main-blocks {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.aside-profile-wrap {
  margin: 0;
}

.aside-profile-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  text-decoration: none;
  color: inherit;
  padding: 1rem 0.65rem 0.95rem;
  border-radius: var(--radius-md);
  background: linear-gradient(
    155deg,
    rgba(255, 255, 255, 0.96) 0%,
    rgba(249, 246, 255, 0.93) 45%,
    rgba(241, 238, 252, 0.9) 100%
  );
  border: 1px solid rgba(67, 56, 202, 0.14);
  box-shadow:
    0 6px 22px rgba(67, 56, 202, 0.07),
    0 2px 10px rgba(21, 28, 40, 0.04),
    inset 0 1px 0 rgba(255, 255, 255, 0.85);
  transition: transform 0.18s ease, box-shadow 0.18s ease, border-color 0.18s ease;
}

.aside-profile-card:hover {
  transform: translateY(-2px);
  box-shadow:
    0 12px 34px rgba(67, 56, 202, 0.13),
    0 4px 14px rgba(21, 28, 40, 0.055);
  border-color: rgba(67, 56, 202, 0.26);
}

.aside-profile-card:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 3px;
}

.aside-profile-visual {
  display: flex;
  justify-content: center;
  margin-bottom: 0.75rem;
}

.aside-profile-avatar {
  width: 80px;
  height: 80px;
  border-radius: 999px;
  object-fit: cover;
  border: 3px solid rgba(255, 255, 255, 0.95);
  box-shadow:
    0 6px 22px rgba(67, 56, 202, 0.24),
    inset 0 1px 0 rgba(255, 255, 255, 0.9);
}

.aside-profile-placeholder {
  width: 80px;
  height: 80px;
  border-radius: 999px;
  background: linear-gradient(145deg, rgba(67, 56, 202, 0.1) 0%, rgba(124, 58, 237, 0.12) 100%);
  border: 2px dashed rgba(67, 56, 202, 0.26);
  box-sizing: border-box;
}

.aside-profile-quote {
  font-size: 0.74rem;
  line-height: 1.58;
  color: var(--text-muted);
  letter-spacing: 0.018em;
  display: block;
  margin: 0 0 0.5rem;
}

.aside-profile-cta {
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--accent);
  letter-spacing: 0.08em;
}

.aside-module-title {
  font-size: 0.76rem;
  font-weight: 700;
  letter-spacing: 0.055em;
  color: var(--text-muted);
  margin: 0 0 0.52rem;
  line-height: 1.45;
}

.layout-tags .aside-module-title {
  margin-bottom: 0.48rem;
}

.nav-cal-section > .aside-module-title,
.nav-filetree-section > .aside-module-title {
  margin-bottom: 0.5rem;
}

.nav-major-divider {
  border: 0;
  border-top: 1px solid var(--border);
  margin: 1.1rem 0 0.95rem;
  opacity: 0.9;
}

.nav-cal-section,
.nav-filetree-section {
  margin: 0 0 0.4rem;
  padding: 0.65rem 0.5rem 0.6rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: linear-gradient(160deg, rgba(255, 255, 255, 0.75) 0%, rgba(250, 248, 255, 0.5) 100%);
  box-shadow: 0 2px 10px rgba(21, 28, 40, 0.04);
}

.nav-cal-root {
  display: flex;
  flex-direction: column;
  gap: 0.02rem;
}

/* 左侧时间线贴在整段 details（含 summary）左侧，避免「标题与竖线错位」 */
.nav-cal-section details.nav-cal {
  box-sizing: border-box;
  margin: 0;
}

.nav-cal-root > details.nav-cal-year {
  padding-left: 0.52rem;
  border-left: 2px solid #64748b;
}

.nav-cal-section details.nav-cal-month {
  padding-left: 0.46rem;
  margin: 0.03rem 0;
  border-left: 2px solid #94a3b8;
}

.nav-cal-section details.nav-cal-day {
  padding-left: 0.4rem;
  margin: 0.03rem 0;
  border-left: 2px solid #cbd5e1;
}

.nav-cal-year > .nav-cal-body,
.nav-cal-month > .nav-cal-body,
.nav-cal-day > .nav-cal-body {
  margin: 0;
  padding: 0;
  border: none;
}

.nav-cal-section .nav-cal-summary.nav-folder-summary {
  padding: 0.18rem 0.18rem;
}

.nav-cal-year > summary.nav-cal-summary { font-size: 0.88rem; color: #0f172a; }
.nav-cal-month > summary.nav-cal-summary { font-size: 0.82rem; color: #1e293b; }
.nav-cal-day > summary.nav-cal-summary {
  font-size: 0.78rem;
  font-weight: 600;
  color: #1e293b;
}

.nav-cal-section ul.nav-cal-articles {
  padding-left: 0;
  padding-top: 0.03rem;
  padding-bottom: 0.08rem;
}

.nav-cal-articles li {
  display: flex;
  align-items: center;
  gap: 0.18rem;
  line-height: 1.28;
  margin: 0.03rem 0;
}

.nav-cal-articles li a {
  flex: 1 1 auto;
  min-width: 0;
  font-size: 0.76rem;
}

.nav-cal-time {
  flex: 0 0 2.35rem;
  font-size: 0.62rem;
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  color: #94a3b8;
}

.nav-cal-visual {
  margin-top: 0.65rem;
  padding-top: 0.65rem;
  border-top: 1px dashed rgba(100, 116, 139, 0.35);
}

.nav-cal-visual-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.4rem;
  margin-bottom: 0.45rem;
}

.nav-cal-visual-title {
  margin: 0;
  font-size: 0.74rem;
  color: var(--text-muted);
  font-weight: 600;
}

.nav-cal-mode-switch {
  display: inline-flex;
  background: rgba(255, 255, 255, 0.75);
  border: 1px solid rgba(67, 56, 202, 0.16);
  border-radius: 999px;
  padding: 0.1rem;
  gap: 0.1rem;
}

.nav-cal-mode-btn {
  border: none;
  background: transparent;
  border-radius: 999px;
  font-size: 0.68rem;
  padding: 0.2rem 0.48rem;
  color: var(--text-muted);
  cursor: pointer;
}

.nav-cal-mode-btn.is-active {
  background: linear-gradient(120deg, rgba(67, 56, 202, 0.16), rgba(124, 58, 237, 0.18));
  color: var(--accent-deep);
  font-weight: 600;
}

.nav-cal-controls {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.28rem;
  margin: 0.15rem 0 0.5rem;
}

.nav-cal-month-stepper {
  display: inline-flex;
  align-items: center;
  gap: 0.15rem;
}

.nav-cal-step-btn {
  flex: 0 0 auto;
  width: 1.55rem;
  height: 1.55rem;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.85);
  font-size: 1rem;
  line-height: 1;
  color: var(--accent-deep);
  cursor: pointer;
}

.nav-cal-step-btn:hover {
  border-color: rgba(67, 56, 202, 0.45);
  background: rgba(67, 56, 202, 0.06);
}

.nav-cal-select-label {
  font-size: 0.67rem;
  color: var(--text-soft);
}

.nav-cal-select {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 0.12rem 0.22rem;
  font-size: 0.7rem;
  background: #fff;
  color: var(--text-primary);
}

.nav-cal-panel-title {
  margin: 0 0 0.35rem;
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-muted);
}

.nav-cal-grid {
  width: 100%;
  border-collapse: collapse;
  table-layout: fixed;
}

.nav-cal-grid th {
  font-size: 0.62rem;
  font-weight: 600;
  color: var(--text-soft);
  padding-bottom: 0.18rem;
}

.nav-cal-grid td {
  padding: 0.08rem;
  text-align: center;
}

/* 仅限日历表格格子：勿写成全局 .nav-cal-day，会与侧栏「计划日期」里 details.nav-cal-day 冲突 */
.nav-cal-grid td.out .nav-cal-day {
  opacity: 0.38;
}

.nav-cal-grid .nav-cal-day {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.02rem;
  border-radius: 8px;
  min-height: 2rem;
  text-decoration: none;
  color: var(--text-muted);
  border: 1px solid transparent;
}

.nav-cal-grid .nav-cal-day .d {
  font-size: 0.72rem;
  line-height: 1.1;
}

.nav-cal-grid .nav-cal-day .n {
  font-size: 0.6rem;
  line-height: 1;
}

.nav-cal-grid .nav-cal-day.has-task {
  background: linear-gradient(160deg, rgba(67, 56, 202, 0.14), rgba(124, 58, 237, 0.13));
  border-color: rgba(67, 56, 202, 0.28);
  color: var(--accent-deep);
}

.nav-cal-grid .nav-cal-day.has-task:hover {
  border-color: rgba(67, 56, 202, 0.48);
}

.nav-cal-year-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.24rem;
}

.nav-cal-month-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.04rem;
  min-height: 2.25rem;
  border-radius: 8px;
  text-decoration: none;
  background: rgba(255, 255, 255, 0.66);
  border: 1px solid rgba(21, 28, 40, 0.08);
  color: var(--text-muted);
}

.nav-cal-month-card .m {
  font-size: 0.66rem;
  font-weight: 600;
}

.nav-cal-month-card .c {
  font-size: 0.58rem;
}

.nav-cal-month-card.has-task {
  color: var(--accent-deep);
  border-color: rgba(67, 56, 202, 0.3);
  background: linear-gradient(160deg, rgba(67, 56, 202, 0.12), rgba(124, 58, 237, 0.14));
}

.nav-root {
  display: flex;
  flex-direction: column;
  gap: 0.03rem;
}

/* 各级目录同一套浅竖线，不按文件分色块 */
.nav-folder,
.nav-mmfile,
.nav-mmnod {
  border: none;
  background: transparent;
  margin: 0;
}

.nav-folder-summary,
.nav-mmfile-summary,
.nav-mmnod-summary {
  cursor: pointer;
  list-style: none;
  padding: 0.26rem 0.22rem;
  font-size: 0.81rem;
  font-weight: 600;
  color: var(--text-primary);
  border-radius: var(--radius-sm);
  transition: background 0.15s ease;
}

.nav-folder-summary:hover,
.nav-mmfile-summary:hover,
.nav-mmnod-summary:hover {
  background: rgba(67, 56, 202, 0.06);
}

.nav-folder-summary::-webkit-details-marker,
.nav-mmfile-summary::-webkit-details-marker,
.nav-mmnod-summary::-webkit-details-marker {
  display: none;
}

.nav-folder-summary::before,
.nav-mmfile-summary::before,
.nav-mmnod-summary::before {
  content: "▸";
  display: inline-block;
  margin-right: 0.32rem;
  transition: transform 0.22s cubic-bezier(0.34, 1.56, 0.64, 1);
  color: var(--accent);
  opacity: 0.55;
  font-size: 0.72rem;
}

details.nav-folder[open] > .nav-folder-summary::before,
details.nav-mmfile[open] > .nav-mmfile-summary::before,
details.nav-mmnod[open] > .nav-mmnod-summary::before {
  transform: rotate(90deg);
}

.nav-folder-body,
.nav-mmfile-body,
.nav-mmnod-body {
  padding: 0.03rem 0 0.1rem 0.34rem;
  margin: 0 0 0.05rem 0.26rem;
  border-left: 2px solid rgba(100, 116, 139, 0.35);
}

.nav-mmfile-summary {
  color: var(--accent-deep);
}

.nav-articles {
  list-style: none;
  padding: 0.08rem 0 0.2rem 0.32rem;
  margin: 0;
}

.nav-articles li { margin: 0.1rem 0; }

.nav-articles a {
  font-size: 0.82rem;
  color: var(--accent);
  text-decoration: none;
  border-radius: 5px;
  padding: 0.08rem 0.12rem;
  margin: -0.08rem -0.12rem;
  transition: background 0.15s, color 0.15s;
}

.nav-articles a:hover {
  background: var(--accent-soft);
  text-decoration: none;
}

.nav-articles li.is-active > a {
  font-weight: 700;
  color: var(--accent-deep);
  background: linear-gradient(90deg, rgba(67, 56, 202, 0.12), rgba(124, 58, 237, 0.08));
}

.nav-cal-section ul.nav-cal-articles li {
  margin: 0.03rem 0;
}

.nav-branch-title {
  color: inherit;
  text-decoration: none;
  font-weight: inherit;
}

.nav-branch-title:hover {
  color: var(--accent);
  text-decoration: none;
}

/* 与首页时间轴一致：主栏内容区横向占满，避免右侧大块留白 */
.page-branch,
.page-with-timeline,
.page-tag {
  width: 100%;
  max-width: 100%;
  min-width: 0;
}

.page-with-timeline .hero {
  margin-bottom: 1.35rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border);
}

.timeline-page {
  margin-top: 0.25rem;
}

.page-branch .page-lead {
  color: var(--text-muted);
  margin: 0 0 1.1rem;
  font-size: 0.93rem;
}

.branch-post-list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.branch-post-list li {
  margin: 0.5rem 0;
  padding: 0.5rem 0 0.55rem;
  border-bottom: 1px solid #e2e8f0;
}

.branch-post-list a {
  color: var(--accent);
  text-decoration: none;
  font-weight: 500;
}

.branch-post-list a:hover { text-decoration: underline; }

.branch-post-meta {
  font-size: 0.86rem;
  color: #64748b;
}

.tag-aside-empty {
  font-size: 0.8rem;
  color: var(--text-soft);
  margin: 0;
}

.tag-cloud {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: center;
  gap: 0.32rem 0.5rem;
  line-height: 1.45;
  padding: 0.25rem 0.15rem;
}

.tag-cloud-link {
  display: inline-block;
  color: var(--text-muted);
  text-decoration: none;
  font-weight: 500;
  padding: 0.12rem 0.24rem;
  border-radius: 999px;
  transition: color 0.18s, background 0.18s, box-shadow 0.18s, transform 0.18s;
  border: 1px solid transparent;
}

.tag-cloud-link:hover {
  color: var(--accent-deep);
  background: rgba(255, 255, 255, 0.85);
  border-color: rgba(67, 56, 202, 0.15);
  box-shadow: 0 2px 8px rgba(67, 56, 202, 0.08);
  transform: translateY(-1px);
}

.tag-cloud-link.is-active {
  color: #fff;
  font-weight: 700;
  background: linear-gradient(135deg, var(--accent) 0%, #7c3aed 100%);
  border-color: transparent;
  box-shadow: 0 4px 14px rgba(67, 56, 202, 0.28);
}

.tag-aside-inner,
.gallery-aside-inner {
  margin: 0;
  padding: 0.75rem 0.65rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: linear-gradient(162deg, rgba(255, 255, 255, 0.94) 0%, rgba(248, 250, 252, 0.92) 100%);
  box-shadow: 0 4px 18px rgba(21, 28, 40, 0.045);
}

.gallery-aside-lead {
  font-size: 0.66rem;
  color: var(--text-muted);
  margin: 0 0 0.58rem;
  line-height: 1.4;
}

.gallery-aside-hint {
  font-size: 0.7rem;
  color: #64748b;
  margin: 0 0 0.55rem;
  line-height: 1.45;
}

.gallery-aside-hint code {
  font-size: 0.85em;
  background: #f1f5f9;
  padding: 0.08rem 0.28rem;
  border-radius: 4px;
}

.gallery-aside-preview {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 4px;
  margin: 0 0 0.6rem;
  border-radius: var(--radius-md);
  overflow: hidden;
  border: 1px solid var(--border);
  box-shadow: var(--shadow-sm);
}

.gallery-aside-thumb {
  display: block;
  aspect-ratio: 1;
  overflow: hidden;
  background: linear-gradient(145deg, #e8e6e3, #dcd9d4);
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.gallery-aside-thumb:hover {
  transform: scale(1.02);
  box-shadow: inset 0 0 0 2px rgba(67, 56, 202, 0.25);
  z-index: 1;
}

.gallery-aside-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  vertical-align: middle;
}

.gallery-aside-more-wrap {
  margin: 0;
  text-align: center;
}

.gallery-aside-more {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--accent);
  text-decoration: none;
}

.gallery-aside-more:hover {
  text-decoration: underline;
}

.page-gallery {
  max-width: 52rem;
}

.page-wordfreq {
  max-width: 52rem;
}

.wordfreq-stats {
  margin: 0 0 1.15rem;
  font-size: 0.82rem;
  color: var(--text-muted);
}

.wordfreq-empty {
  margin: 1rem 0;
  font-size: 0.92rem;
  color: var(--text-soft);
}

.wordfreq-cloud {
  display: flex;
  flex-wrap: wrap;
  gap: 0.38rem 0.52rem;
  align-items: center;
  justify-content: center;
  padding: 1.35rem 1.05rem;
  margin: 0 0 2rem;
  border-radius: var(--radius-lg);
  border: 1px solid var(--border);
  background:
    radial-gradient(ellipse 90% 120% at 50% -10%, rgba(129, 140, 248, 0.16) 0%, transparent 55%),
    linear-gradient(165deg, rgba(255, 255, 255, 0.82) 0%, rgba(248, 246, 255, 0.68) 100%);
  box-shadow: var(--shadow-card);
}

.wordfreq-chip {
  display: inline-block;
  padding: 0.16rem 0.48rem;
  border-radius: 999px;
  font-weight: 600;
  line-height: 1.35;
  letter-spacing: 0.015em;
  border: 1px solid rgba(67, 56, 202, 0.18);
  background: rgba(255, 255, 255, 0.65);
  color: var(--accent-deep);
  font-size: calc(0.62rem + var(--wf, 0.45) * 0.82rem);
  transition: transform 0.14s ease, border-color 0.14s ease;
}

.wordfreq-chip:hover {
  transform: translateY(-1px);
  border-color: rgba(67, 56, 202, 0.35);
}

.wordfreq-chart {
  margin: 0;
  padding: 0;
}

.wordfreq-chart-title {
  font-size: 1.02rem;
  font-weight: 700;
  letter-spacing: -0.02em;
  margin: 0 0 0.65rem;
  color: var(--text-primary);
}

.wordfreq-row {
  display: grid;
  grid-template-columns: minmax(4.5rem, 10rem) minmax(0, 1fr) 2.85rem;
  gap: 0.45rem;
  align-items: center;
  padding: 0.32rem 0;
  border-bottom: 1px solid rgba(21, 28, 40, 0.06);
  font-size: 0.82rem;
}

.wordfreq-label {
  font-weight: 600;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.wordfreq-bar-wrap {
  height: 0.42rem;
  border-radius: 999px;
  background: rgba(21, 28, 40, 0.08);
  overflow: hidden;
}

.wordfreq-bar {
  display: block;
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, var(--accent), #c084fc);
}

.wordfreq-n {
  font-variant-numeric: tabular-nums;
  text-align: right;
  color: var(--text-muted);
  font-size: 0.78rem;
}

html.theme-dark .wordfreq-cloud {
  background:
    radial-gradient(ellipse 90% 120% at 50% -10%, rgba(129, 140, 248, 0.22) 0%, transparent 55%),
    linear-gradient(165deg, rgba(28, 34, 48, 0.92) 0%, rgba(22, 26, 38, 0.88) 100%);
}

html.theme-dark .wordfreq-chip {
  background: rgba(22, 26, 38, 0.85);
  border-color: rgba(165, 180, 252, 0.28);
}

html.theme-dark .wordfreq-bar-wrap {
  background: rgba(255, 255, 255, 0.1);
}

html.theme-dark .wordfreq-row {
  border-bottom-color: rgba(255, 255, 255, 0.07);
}

.gallery-empty {
  color: #64748b;
  font-size: 0.92rem;
}

.gallery-group-title {
  font-size: 1.08rem;
  margin: 1.5rem 0 0.7rem;
  padding-bottom: 0.42rem;
  border-bottom: 1px dashed var(--border);
  color: var(--text-primary);
  font-weight: 700;
  letter-spacing: -0.02em;
}

.gallery-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(148px, 1fr));
  gap: 0.85rem;
  margin-bottom: 1rem;
}

.gallery-card {
  margin: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  overflow: hidden;
  background: var(--surface-card);
  box-shadow: var(--shadow-card);
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.gallery-card:hover {
  transform: translateY(-3px);
  box-shadow: var(--shadow-float);
}

.gallery-card-link {
  display: block;
  aspect-ratio: 1;
  background: linear-gradient(155deg, #f4f3f8, #e8e6ef);
}

.gallery-card-link img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.gallery-card-cap {
  font-size: 0.72rem;
  padding: 0.4rem 0.45rem;
  color: #475569;
  line-height: 1.35;
}

.visually-hidden {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.search-aside-wrap {
  padding: 0.75rem 0.65rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: linear-gradient(162deg, rgba(255, 255, 255, 0.94) 0%, rgba(248, 250, 252, 0.92) 100%);
  box-shadow: 0 4px 18px rgba(21, 28, 40, 0.045);
}

.search-aside-title-link {
  color: inherit;
  text-decoration: none;
}

.search-aside-title-link:hover {
  color: var(--accent);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.search-aside-lead {
  font-size: 0.68rem;
  color: var(--text-soft);
  margin: 0 0 0.65rem;
  line-height: 1.45;
}

.search-aside-input {
  width: 100%;
  box-sizing: border-box;
  padding: 0.45rem 0.55rem;
  font-size: 0.82rem;
  border-radius: var(--radius-sm);
  border: 1px solid rgba(67, 56, 202, 0.22);
  background: rgba(255, 255, 255, 0.95);
  color: var(--text-primary);
  outline: none;
  transition: border-color 0.15s, box-shadow 0.15s;
}

.search-aside-input::placeholder {
  color: var(--text-soft);
}

.search-aside-input:focus {
  border-color: rgba(67, 56, 202, 0.45);
  box-shadow: 0 0 0 3px var(--accent-soft);
}

.search-aside-status {
  margin: 0.45rem 0 0;
  min-height: 1.2em;
  font-size: 0.68rem;
  color: var(--text-muted);
}

.search-aside-list {
  list-style: none;
  margin: 0.45rem 0 0;
  padding: 0;
  max-height: 17rem;
  overflow: auto;
  border-top: 1px solid rgba(21, 28, 40, 0.06);
}

.search-hit-item {
  border-bottom: 1px solid rgba(21, 28, 40, 0.06);
}

.search-hit-item:last-child {
  border-bottom: none;
}

.search-hit-link {
  display: block;
  padding: 0.45rem 0.15rem;
  text-decoration: none;
  color: inherit;
  border-radius: var(--radius-sm);
  transition: background 0.15s;
}

.search-hit-link:hover {
  background: rgba(67, 56, 202, 0.06);
}

.search-hit-title {
  display: block;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--accent-deep);
  margin-bottom: 0.12rem;
  line-height: 1.35;
}

.search-hit-snippet {
  display: block;
  font-size: 0.68rem;
  color: var(--text-muted);
  line-height: 1.45;
  word-break: break-word;
}

.gen-aside-links {
  margin: 0;
  font-size: 0.76rem;
  text-align: center;
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  justify-content: center;
  gap: 0.15rem;
  white-space: nowrap;
  min-width: 0;
}

.gen-aside-links .gen-aside-sep {
  flex-shrink: 0;
}

.gen-aside-sep {
  color: var(--text-soft);
  user-select: none;
  font-weight: 500;
}

.gen-aside-main-link,
.gen-aside-rss-link {
  font-weight: 600;
  text-decoration: none;
  padding: 0.26rem 0.44rem;
  border-radius: 999px;
  border: 1px solid rgba(67, 56, 202, 0.2);
  background: rgba(255, 255, 255, 0.65);
  box-shadow: 0 2px 10px rgba(67, 56, 202, 0.06);
  display: inline-block;
  flex-shrink: 0;
  transition: transform 0.18s, box-shadow 0.18s, border-color 0.18s, color 0.18s;
}

.gen-aside-main-link {
  color: var(--accent);
}

.gen-aside-rss-link {
  color: #c2410c;
  border-color: rgba(194, 65, 12, 0.25);
  background: linear-gradient(165deg, rgba(255, 247, 237, 0.95) 0%, rgba(255, 237, 213, 0.88) 100%);
  font-size: 0.82rem;
}

.gen-aside-main-link:hover,
.gen-aside-rss-link:hover {
  text-decoration: none;
  transform: translateY(-1px);
  border-color: rgba(67, 56, 202, 0.35);
  box-shadow: 0 6px 18px rgba(67, 56, 202, 0.12);
}

.gen-aside-rss-link:hover {
  border-color: rgba(194, 65, 12, 0.45);
  color: #9a3412;
}

.page-gen-history {
  max-width: min(56rem, 100%);
}

.gen-history-table-wrap {
  overflow-x: auto;
  margin-bottom: 1.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--surface-card);
  box-shadow: var(--shadow-sm);
}

.gen-history-pager {
  margin: -0.4rem 0 1.25rem;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 0.65rem;
}

.gen-history-pager-left,
.gen-history-pager-right {
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.gen-history-pager-label,
.gen-history-page-info {
  font-size: 0.8rem;
  color: var(--text-muted);
}

.gen-history-page-size {
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: #fff;
  color: var(--text-primary);
  padding: 0.22rem 0.32rem;
  font-size: 0.8rem;
}

.gen-history-page-btn {
  border: 1px solid rgba(67, 56, 202, 0.25);
  background: rgba(255, 255, 255, 0.95);
  color: var(--accent-deep);
  font-size: 0.8rem;
  border-radius: 999px;
  padding: 0.22rem 0.65rem;
  cursor: pointer;
  transition: all 0.15s;
}

.gen-history-page-btn:hover:not(:disabled) {
  background: rgba(67, 56, 202, 0.08);
  border-color: rgba(67, 56, 202, 0.45);
}

.gen-history-page-btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.gen-history-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
}

.gen-history-table th,
.gen-history-table td {
  padding: 0.45rem 0.55rem;
  border-bottom: 1px solid var(--border);
  text-align: right;
}

.gen-history-table th:first-child,
.gen-history-table td:first-child {
  text-align: left;
  white-space: nowrap;
}

.gen-history-table thead th {
  background: linear-gradient(180deg, #faf9fc 0%, #f3f1f8 100%);
  font-weight: 600;
  color: var(--text-primary);
}

.gen-history-table tbody tr:last-child td {
  border-bottom: none;
}

.gen-history-row {
  cursor: pointer;
}

.gen-history-row:hover td {
  background: rgba(67, 56, 202, 0.05);
}

.gen-history-row.is-selected td {
  background: linear-gradient(90deg, rgba(67, 56, 202, 0.1), rgba(168, 85, 247, 0.08));
}

.gen-history-detail-section {
  margin-top: 1.75rem;
}

.gen-detail-placeholder {
  font-size: 0.88rem;
  color: #64748b;
  margin: 0 0 0.75rem;
  line-height: 1.55;
}

.gen-detail-panel {
  margin-top: 0.35rem;
}

.gen-detail-empty {
  color: #64748b;
}

td.gen-history-empty {
  text-align: center !important;
  color: #64748b;
  padding: 1rem !important;
}

.gen-history-subtitle {
  font-size: 1.05rem;
  margin: 0 0 0.75rem;
}

.gen-sample-block {
  margin-bottom: 1rem;
  padding: 0.65rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: #fafbfc;
}

.gen-sample-h3 {
  font-size: 0.88rem;
  margin: 0 0 0.35rem;
  color: #475569;
}

.gen-sample-p {
  font-size: 0.82rem;
  margin: 0.25rem 0 0;
  color: #0f172a;
  line-height: 1.5;
}

.gen-detail-link {
  color: var(--accent);
  text-decoration: none;
}

.gen-detail-link:hover {
  text-decoration: underline;
}

.hero {
  margin-bottom: 1.85rem;
  padding-bottom: 1.15rem;
  border-bottom: 1px solid var(--border);
  position: relative;
}

.hero::after {
  content: "";
  position: absolute;
  left: 0;
  bottom: -1px;
  width: 4.5rem;
  height: 3px;
  border-radius: 3px;
  background: linear-gradient(90deg, var(--accent), #a855f7);
  opacity: 0.85;
}

.page-title {
  margin: 0 0 0.45rem;
  font-size: clamp(1.45rem, 2.6vw, 1.85rem);
  font-weight: 700;
  letter-spacing: -0.035em;
  line-height: 1.25;
  color: var(--text-primary);
}

.page-lead {
  margin: 0;
  color: var(--text-muted);
  font-size: 0.93rem;
  max-width: 40rem;
  line-height: 1.65;
}

.page-lead code {
  font-size: 0.85em;
  background: rgba(67, 56, 202, 0.07);
  color: var(--accent-deep);
  padding: 0.12rem 0.4rem;
  border-radius: 6px;
  font-weight: 500;
}

.timeline {
  --timeline-lead-width: 11.85rem;
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  position: relative;
}

.timeline::before {
  content: "";
  position: absolute;
  /* 与时间列右缘留白 + 圆点对齐；translate 使 3px 条以轴线居中 */
  left: calc(var(--timeline-lead-width) - 0.62rem);
  top: 0.6rem;
  bottom: 0.6rem;
  width: 3px;
  transform: translateX(-50%);
  background: linear-gradient(180deg, rgba(99, 102, 241, 0.5), rgba(168, 85, 247, 0.55), rgba(236, 72, 153, 0.35));
  border-radius: 4px;
  opacity: 0.82;
}

.timeline-item {
  --timeline-gap: 0.85rem;
  display: grid;
  grid-template-columns: var(--timeline-lead-width) minmax(0, 1fr);
  gap: var(--timeline-gap);
  align-items: start;
  padding-bottom: 0.75rem;
  position: relative;
}

.timeline-lead {
  flex-shrink: 0;
  padding: 0.15rem 0.85rem 0 0.12rem;
  box-sizing: border-box;
}

.timeline-datetime {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 0.18rem;
  font-variant-numeric: tabular-nums;
  max-width: 100%;
}

.timeline-clock-row {
  display: flex;
  flex-direction: row;
  align-items: center;
  justify-content: flex-end;
  gap: 0.38rem;
  padding-right: 0.05rem;
}

.timeline-date {
  display: block;
  max-width: 100%;
  padding-right: 0.15rem;
  box-sizing: border-box;
  word-break: keep-all;
  overflow-wrap: anywhere;
  font-size: 0.96rem;
  font-weight: 700;
  color: #334155;
  line-height: 1.35;
}

.timeline-clock {
  font-size: 0.8rem;
  font-weight: 600;
  background: linear-gradient(118deg, var(--accent), #9333ea);
  -webkit-background-clip: text;
  background-clip: text;
  -webkit-text-fill-color: transparent;
  letter-spacing: 0.04em;
}

.timeline-date-repeat {
  min-height: 1.35em;
}

.timeline-marker {
  display: flex;
  align-items: center;
  justify-content: center;
  flex: 0 0 auto;
  position: relative;
  z-index: 1;
}

.timeline-dot {
  display: block;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: linear-gradient(145deg, #6366f1, #a855f7);
  border: 2px solid #fff;
  box-shadow: 0 0 0 3px rgba(129, 140, 248, 0.35);
}

.timeline-card {
  background: var(--surface-card);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  padding: 0.7rem 0.82rem;
  box-shadow: var(--shadow-card);
  transition: transform 0.22s ease, box-shadow 0.22s ease;
}

.timeline-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--shadow-float);
}

.timeline-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 0.45rem;
  margin: 0 0 0.18rem;
}

.timeline-title {
  margin: 0;
  font-size: clamp(0.95rem, 1.4vw, 1.08rem);
  font-weight: 700;
  letter-spacing: -0.025em;
  min-width: 0;
}

.timeline-title a {
  color: var(--text-primary);
  text-decoration: none;
  transition: color 0.18s;
}

.timeline-title a:hover {
  color: var(--accent);
  text-decoration: none;
}

.timeline-bm {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 0.22rem;
  margin: 0;
}

.timeline-excerpt {
  margin: 0.08rem 0 0;
  font-size: 0.8rem;
  color: var(--text-muted);
  line-height: 1.5;
  display: -webkit-box;
  -webkit-line-clamp: 3;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.bm-pill {
  display: inline-flex;
  align-items: center;
  padding: 0.22rem 0.62rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 500;
  background: rgba(255, 255, 255, 0.9);
  color: var(--accent-deep);
  text-decoration: none;
  border: 1px solid rgba(67, 56, 202, 0.18);
  box-shadow: 0 1px 4px rgba(67, 56, 202, 0.06);
  transition: transform 0.15s, box-shadow 0.15s, border-color 0.15s;
}

.bm-pill:hover {
  background: linear-gradient(135deg, rgba(238, 242, 255, 0.95), rgba(250, 245, 255, 0.95));
  border-color: rgba(67, 56, 202, 0.28);
  transform: translateY(-1px);
}

.bm-pill.sm {
  padding: 0.12rem 0.45rem;
  font-size: 0.78rem;
}

.post-list {
  list-style: none;
  padding: 0;
  margin: 0;
}

.post-list li { margin: 0.5rem 0; }

.post-list a {
  color: var(--accent);
  text-decoration: none;
  font-weight: 500;
}

.post-list a:hover { text-decoration: underline; }

.list-meta {
  font-size: 0.88rem;
  color: #64748b;
}

.article-header {
  margin-bottom: 0.75rem;
  font-size: 0.88rem;
  line-height: 1.5;
}

.crumb {
  color: var(--accent);
  text-decoration: none;
  font-weight: 500;
  transition: color 0.15s;
}

.crumb:hover {
  color: var(--accent-deep);
  text-decoration: none;
}

.crumb-sep { color: #94a3b8; margin: 0 0.2rem; }

.article-title-block h1 {
  margin: 0;
  font-size: clamp(1.45rem, 3vw, 1.92rem);
  font-weight: 700;
  letter-spacing: -0.035em;
  line-height: 1.22;
}

.article-meta-line {
  margin: 0.42rem 0 1.05rem;
  color: var(--text-muted);
  font-size: 0.86rem;
  line-height: 1.5;
  white-space: nowrap;
  overflow-x: auto;
}

.article-meta-line .article-plan-time {
  font-weight: 700;
  color: var(--text-primary);
}

.content p { margin: 0.75rem 0; }

.content figure { margin: 1rem 0; }

.article-figure {
  scroll-margin-top: 1rem;
}

.content img {
  max-width: 100%;
  height: auto;
  border-radius: var(--radius-md);
  box-shadow: 0 10px 36px rgba(21, 28, 40, 0.09);
}

/* 降低拖动另存、长按菜单（无法杜绝截图或手动下载 URL） */
.layout-main img,
.layout-tags img {
  -webkit-user-drag: none;
  user-select: none;
  -moz-user-select: none;
  -webkit-user-select: none;
  -ms-user-select: none;
  -webkit-touch-callout: none;
}

.content figcaption {
  font-size: 0.85rem;
  color: #64748b;
  margin-top: 0.35rem;
}

.article-page {
  position: relative;
  min-width: 0;
}

/*
  收起条固定在视口：纵向可与页脚/右侧栏底部大致对齐，
  横向紧贴主栏右缘（约在第三栏左侧），不随正文长度上下移动。
*/
.rev-dock {
  position: fixed;
  z-index: 90;
  display: flex;
  flex-direction: column-reverse;
  align-items: stretch;
  bottom: calc(env(safe-area-inset-bottom, 0px) + var(--site-footer-occupy) + 1.75rem);
  right: calc(
    max(0px, (100vw - var(--layout-shell-max)) / 2) + var(--layout-shell-pad-x) + var(--layout-col-tags-max) + 0.85rem
  );
  width: min(320px, calc(100vw - 280px));
  max-width: min(320px, calc(100vw - 280px));
  filter: drop-shadow(0 8px 22px rgba(21, 28, 40, 0.1));
}

.rev-dock-panel-wrap {
  display: grid;
  grid-template-rows: 0fr;
  transition: grid-template-rows 0.22s cubic-bezier(0.33, 1, 0.55, 1);
}

.rev-dock[open] .rev-dock-panel-wrap {
  grid-template-rows: 1fr;
}

.rev-dock-panel-wrap > .rev-aside {
  min-height: 0;
  overflow: hidden;
}

/* 展开动画进行中不设 auto，避免高度变化时出现滚动条；结束后由脚本加上 .rev-aside--scrollable */
.rev-aside.rev-aside--scrollable {
  overflow-y: auto;
  overflow-x: auto;
}

@media (prefers-reduced-motion: reduce) {
  .rev-dock-panel-wrap {
    transition: grid-template-rows 0.06s linear;
  }
}

.rev-dock-summary {
  list-style: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
  padding: 0.48rem 0.65rem;
  border-radius: var(--radius-md);
  border: 1px solid var(--border);
  background: linear-gradient(165deg, #ffffff 0%, #f5f3fb 100%);
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--accent-deep);
  user-select: none;
}

.rev-dock[open] .rev-dock-summary {
  border-top-left-radius: 0;
  border-top-right-radius: 0;
}

.rev-dock-summary::-webkit-details-marker {
  display: none;
}

.rev-dock-title {
  letter-spacing: -0.02em;
}

.rev-dock-summary::after {
  content: "展开";
  font-size: 0.68rem;
  font-weight: 500;
  color: var(--text-soft);
}

.rev-dock[open] .rev-dock-summary::after {
  content: "收起";
}

.rev-aside {
  max-width: 100%;
  max-height: min(52vh, 380px);
  border: 1px solid var(--border);
  border-bottom: none;
  border-radius: var(--radius-lg) var(--radius-lg) 0 0;
  padding: 0.75rem 0.85rem 0.85rem;
  background: linear-gradient(165deg, #ffffff 0%, #f8f7fc 42%, #f3f1f9 100%);
  box-shadow: none;
}

.rev-summary {
  font-size: 0.78rem;
  color: #475569;
  margin: 0 0 0.65rem;
  line-height: 1.45;
}

.rev-latest {
  font-size: 0.82rem;
  color: #1e40af;
  margin: 0 0 0.65rem;
}

.rev-list { display: flex; flex-direction: column; gap: 0.45rem; }

.rev-item {
  border: 1px solid var(--border);
  border-radius: 10px;
  background: #fff;
}

.rev-item summary {
  cursor: pointer;
  padding: 0.45rem 0.55rem;
  font-size: 0.8rem;
  color: #334155;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.35rem;
}

.rev-sum-text { flex: 1 1 auto; min-width: 12rem; }

.rev-sum-hint {
  font-size: 0.72rem;
  color: #94a3b8;
  flex: 0 0 auto;
}

.version-stat {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 0.15rem 0.45rem;
  border-radius: 6px;
  letter-spacing: 0.02em;
}

.version-stat-add {
  background: #dcfce7;
  color: #166534;
  border: 1px solid #bbf7d0;
}

.version-stat-del {
  background: #fee2e2;
  color: #991b1b;
  border: 1px solid #fecaca;
}

.version-stat-mod {
  background: #fef3c7;
  color: #92400e;
  border: 1px solid #fde68a;
}

.rev-body {
  padding: 0.55rem 0.6rem 0.7rem;
  border-top: 1px solid var(--border);
  font-size: 0.85rem;
  overflow-x: auto;
}

.diff-rev-body {
  max-height: 380px;
  overflow: auto;
  background: #f8fafc;
  border-radius: 8px;
}

.snapshot-pre {
  white-space: pre-wrap;
  word-break: break-word;
  margin: 0;
  font-family: ui-monospace, Consolas, monospace;
  font-size: 0.78rem;
  color: #334155;
}

.diff-inline {
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.65;
}

.diff-ins {
  background: #22c55e33;
  color: #14532d;
  text-decoration: none;
  border-radius: 3px;
  padding: 0 1px;
}

.diff-del {
  background: #ef444433;
  color: #7f1d1d;
  text-decoration: line-through;
  border-radius: 3px;
  padding: 0 1px;
}

.diff-same { color: #334155; }

.missing { color: #b45309; }

.page-tag .page-lead { color: #64748b; margin: 0.25rem 0 1rem; }

.page-about .hero-about {
  text-align: center;
}

.about-avatar-wrap {
  margin: 0 auto 1rem;
}

.about-avatar {
  width: 120px;
  height: 120px;
  border-radius: 999px;
  object-fit: cover;
  border: 3px solid rgba(67, 56, 202, 0.2);
  box-shadow: var(--shadow-card);
}

.page-about-signature {
  max-width: 40rem;
  margin-left: auto;
  margin-right: auto;
}

.about-body {
  max-width: 40rem;
  margin: 1.5rem auto 0;
  color: var(--text-muted);
  font-size: 0.95rem;
}

@media (max-width: 960px) {
  /* 页脚栅格纵向更松，占位高于桌面，避免内容被固定底栏挡住 */
  body.site-body {
    --site-footer-occupy: calc(var(--site-footer-h) + 1.24rem);
  }

  .site-footer-inner {
    grid-template-columns: 1fr;
    gap: 0.55rem;
    padding: 0.62rem 1rem;
    justify-items: center;
    text-align: center;
  }

  .site-footer-inner > .site-footer-links {
    justify-self: center;
  }

  .layout-shell {
    grid-template-columns: 1fr;
    max-width: none;
  }
  .layout-nav,
  .layout-tags {
    position: relative;
    max-height: none;
    border: none;
    border-bottom: 1px solid var(--border);
    border-radius: 0;
    box-shadow: none;
  }
  .layout-main {
    padding: 1.25rem 1rem 2.5rem;
  }
  .timeline::before { display: none; }
  .timeline-item {
    grid-template-columns: 1fr;
    gap: 0.35rem;
  }
  .timeline-lead { text-align: left; }
  .timeline-datetime { align-items: flex-start; }
  .timeline-clock-row { justify-content: flex-start; }
  .timeline-head { flex-direction: column; align-items: flex-start; gap: 0.2rem; }
  .timeline-bm { justify-content: flex-start; }
  .timeline-marker { display: none; }
  .rev-dock {
    left: 1rem;
    right: 1rem;
    width: auto;
    max-width: none;
    bottom: calc(var(--site-footer-occupy) + max(1rem, env(safe-area-inset-bottom)));
  }
}

@media print {
  html {
    scrollbar-gutter: auto;
  }
  body.site-body {
    padding-bottom: 0 !important;
  }
  .site-footer {
    position: static;
    bottom: auto;
    left: auto;
    right: auto;
    padding-bottom: 0;
  }
  .rev-dock {
    display: none !important;
  }
}
""";
        File.WriteAllText(path, css, Encoding.UTF8);
    }
}
