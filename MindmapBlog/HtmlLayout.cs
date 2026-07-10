using System.Globalization;
using System.Net;
using System.Text;

namespace MindmapBlog;

internal static class HtmlLayout
{
    public const string SiteFooterCopyrightLine = "© 2026 WangYang. All rights reserved.";

    /// <summary>时间轴列表页静态壳（标题与列表由 timeline-page.js + JSON 填充）。</summary>
    public const string TimelinePageShellInner =
        """
        <div id="timeline-page-root" class="page-with-timeline">
        <header class="hero">
        <h1 class="page-title" id="timeline-page-title">…</h1>
        <p class="page-lead" id="timeline-page-sub" hidden></p>
        <p class="page-lead" id="timeline-page-lead" hidden></p>
        </header>
        <div id="timeline-page-host" class="site-page-loading">加载时间轴…</div>
        </div>
        """;

    /// <summary>词频页静态壳（统计与词云由 word-frequency-page.js + JSON 填充）。</summary>
    public const string WordFrequencyPageShellInner =
        """
        <div class="page-wordfreq" id="wordfreq-page-root">
        <header class="hero">
        <h1 class="page-title">词频</h1>
        <p class="page-lead">基于全部文章与独立页（关于我、提交记录等）的标题与正文；中文使用 jieba 精确模式分词。过滤常见虚词（停用词表）与导图「变量 → 词频过滤」；出现 2 次及以下的词默认不展示；「变量 → 词频强制」中的词条始终出现。气泡大小表示相对频次。</p>
        </header>
        <div id="wordfreq-page-host" class="site-page-loading">加载词频…</div>
        </div>
        """;

    public static string BuildDocument(
        string pageTitle,
        string headExtra,
        string innerMain,
        string? currentPageWebPath,
        string? rssFeedWebPath,
        SiteFileNames? siteNames = null,
        string? activeHtmlFile = null,
        string? highlightTag = null,
        bool isSearchPage = false,
        bool timelinePageShell = false,
        bool wordFrequencyPageShell = false)
    {
        var sb = new StringBuilder();
        var cssHref = SitePathHelper.RelFromTo(currentPageWebPath, "site.css");
        var chromeScriptHref = SitePathHelper.RelFromTo(currentPageWebPath, "site-chrome.js");
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\"/>");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        sb.AppendLine("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\"/>");
        sb.AppendLine("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin=\"\"/>");
        sb.AppendLine(
            "<link href=\"https://fonts.googleapis.com/css2?family=Noto+Sans+SC:wght@400;500;600;700&display=swap\" rel=\"stylesheet\"/>");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(pageTitle)).AppendLine("</title>");
        sb.Append("<link rel=\"stylesheet\" href=\"").Append(WebUtility.HtmlEncode(cssHref)).AppendLine("\"/>");
        // 站点图标：优先使用生成阶段发布到 media/site-avatar.* 的头像文件。
        foreach (var ext in new[] { "png", "jpg", "jpeg", "webp", "gif", "avif" })
        {
            var iconHref = SitePathHelper.RelFromTo(currentPageWebPath, $"media/site-avatar.{ext}");
            sb.Append("<link rel=\"icon\" href=\"")
                .Append(WebUtility.HtmlEncode(iconHref))
                .Append("\" type=\"image/")
                .Append(ext is "jpg" ? "jpeg" : ext)
                .AppendLine("\"/>");
        }
        if (!string.IsNullOrEmpty(rssFeedWebPath))
        {
            var rssHref = SitePathHelper.RelFromTo(currentPageWebPath, rssFeedWebPath);
            sb.Append("<link rel=\"alternate\" type=\"application/rss+xml\" title=\"RSS 订阅\" href=\"")
                .Append(WebUtility.HtmlEncode(rssHref)).AppendLine("\"/>");
        }

        if (!string.IsNullOrEmpty(headExtra))
            sb.AppendLine(headExtra);
        AppendBaiduAnalytics(sb);
        sb.AppendLine("</head>");
        sb.Append("<body class=\"site-body\"");
        if (!string.IsNullOrEmpty(currentPageWebPath))
            sb.Append(" data-page-path=\"").Append(WebUtility.HtmlEncode(currentPageWebPath.Replace('\\', '/'))).Append("\"");
        if (!string.IsNullOrEmpty(activeHtmlFile))
            sb.Append(" data-active-article=\"").Append(WebUtility.HtmlEncode(activeHtmlFile.Replace('\\', '/'))).Append("\"");
        if (!string.IsNullOrEmpty(highlightTag))
            sb.Append(" data-highlight-tag=\"").Append(WebUtility.HtmlEncode(highlightTag)).Append("\"");
        if (isSearchPage)
            sb.Append(" data-is-search-page=\"1\"");
        sb.AppendLine(">");
        var homeHref = string.IsNullOrEmpty(currentPageWebPath)
            ? "index.html"
            : SitePathHelper.RelFromTo(currentPageWebPath, "index.html");
        sb.AppendLine("<header class=\"site-topbar\" role=\"banner\">");
        sb.AppendLine("<div class=\"site-topbar-inner\">");
        sb.Append("<a class=\"site-topbar-brand\" href=\"").Append(WebUtility.HtmlEncode(homeHref)).AppendLine("\">")
            .Append(WebUtility.HtmlEncode(SiteProfile.BlogTitle)).AppendLine("</a>");
        sb.AppendLine("<div class=\"site-topbar-mobile-actions\">");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-mobile-btn\" id=\"site-mobile-nav-toggle\" aria-expanded=\"false\" aria-controls=\"layout-nav\">目录</button>");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-mobile-btn\" id=\"site-mobile-aside-toggle\" aria-expanded=\"false\" aria-controls=\"layout-tags\">书签</button>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class=\"site-topbar-controls\" role=\"group\" aria-label=\"强调色、灰度与夜间模式\">");
        sb.AppendLine("<span class=\"site-topbar-swatches\" aria-hidden=\"false\">");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-topbar-swatch\" data-accent=\"#4338ca\" style=\"--site-swatch:#4338ca\" title=\"靛青\" aria-label=\"强调色：靛青\"></button>");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-topbar-swatch\" data-accent=\"#7c3aed\" style=\"--site-swatch:#7c3aed\" title=\"紫\" aria-label=\"强调色：紫\"></button>");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-topbar-swatch\" data-accent=\"#0d9488\" style=\"--site-swatch:#0d9488\" title=\"青绿\" aria-label=\"强调色：青绿\"></button>");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-topbar-swatch\" data-accent=\"#c2410c\" style=\"--site-swatch:#c2410c\" title=\"琥珀\" aria-label=\"强调色：琥珀\"></button>");
        sb.AppendLine("</span>");
        sb.AppendLine("<label class=\"site-topbar-color-label\" title=\"自选强调色\">");
        sb.AppendLine("<span class=\"visually-hidden\">自选强调色</span>");
        sb.AppendLine("<input type=\"color\" id=\"site-accent-picker\" class=\"site-topbar-color-input\" value=\"#4338ca\" aria-label=\"自选强调色\"/>");
        sb.AppendLine("</label>");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-topbar-chip site-topbar-bw\" id=\"site-theme-bw-toggle\" aria-pressed=\"false\" title=\"灰度模式：全站去色，适合长文阅读\">灰度</button>");
        sb.AppendLine(
            "<button type=\"button\" class=\"site-topbar-chip site-topbar-night\" id=\"site-theme-dark-toggle\" aria-pressed=\"false\" title=\"夜间模式\">夜间</button>");
        sb.AppendLine("</div></div></header>");
        sb.AppendLine("<div class=\"site-mobile-backdrop\" id=\"site-mobile-backdrop\" hidden aria-hidden=\"true\"></div>");
        sb.AppendLine("<div class=\"layout-shell\">");
        sb.AppendLine("<aside class=\"layout-nav\" id=\"layout-nav\" aria-label=\"目录与导图导航\">");
        sb.AppendLine("<p class=\"site-chrome-placeholder\" id=\"site-chrome-nav-host\">加载目录…</p>");
        sb.AppendLine("</aside>");
        sb.AppendLine("<main class=\"layout-main\">");
        sb.Append(innerMain);
        sb.AppendLine("</main>");
        sb.AppendLine("<aside class=\"layout-tags\" id=\"layout-tags\" aria-label=\"书签、图册与搜索\">");
        sb.AppendLine("<p class=\"site-chrome-placeholder\" id=\"site-chrome-aside-host\">加载侧栏…</p>");
        sb.AppendLine("</aside>");
        sb.AppendLine("</div>");
        if (siteNames != null)
            sb.Append(BuildSiteFooter(currentPageWebPath, siteNames));
        sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(chromeScriptHref)).AppendLine("\" defer></script>");
        if (timelinePageShell)
        {
            var timelineScriptHref = SitePathHelper.RelFromTo(currentPageWebPath, "timeline-page.js");
            sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(timelineScriptHref)).AppendLine("\" defer></script>");
        }
        if (wordFrequencyPageShell)
        {
            var wfScriptHref = SitePathHelper.RelFromTo(currentPageWebPath, "word-frequency-page.js");
            sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(wfScriptHref)).AppendLine("\" defer></script>");
        }
        if (!string.IsNullOrEmpty(activeHtmlFile))
        {
            var outlineScriptHref = SitePathHelper.RelFromTo(currentPageWebPath, "article-outline.js");
            sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(outlineScriptHref)).AppendLine("\" defer></script>");
        }
        AppendNavAccordionScript(sb);
        AppendMobileShellScript(sb);
        AppendImageProtectionScript(sb);
        AppendRevisionDockScript(sb);
        AppendTopbarThemeScript(sb);
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendBaiduAnalytics(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine("var _hmt = _hmt || [];");
        sb.AppendLine("(function() {");
        sb.AppendLine("  var hm = document.createElement(\"script\");");
        sb.AppendLine("  hm.src = \"https://hm.baidu.com/hm.js?dced051aaa84ab850909e5607f874760\";");
        sb.AppendLine("  var s = document.getElementsByTagName(\"script\")[0];");
        sb.AppendLine("  s.parentNode.insertBefore(hm, s);");
        sb.AppendLine("})();");
        sb.AppendLine("</script>");
    }

    /// <summary>
    /// 同级 details 手风琴；用 sessionStorage 记住展开节点与左侧滚动条位置，整页跳转后恢复。
    /// </summary>
    private static void AppendNavAccordionScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine(
            """
            (function () {
              window.MindmapBlogInitNav = function () {
              var KEY_OPEN = "mindmapblog-nav-open-details";
              var KEY_SCROLL = "mindmapblog-nav-scroll";

              function depth(d) {
                var n = 0;
                for (var p = d.parentElement; p; p = p.parentElement) {
                  if (p.matches && p.matches(".nav-tree details")) n++;
                }
                return n;
              }

              function persistOpen() {
                var ids = [];
                document.querySelectorAll(".nav-tree details[open]").forEach(function (d) {
                  if (d.id) ids.push(d.id);
                });
                try { sessionStorage.setItem(KEY_OPEN, JSON.stringify(ids)); } catch (e) {}
              }

              function restoreOpen() {
                var raw = sessionStorage.getItem(KEY_OPEN);
                if (!raw) return;
                var ids;
                try { ids = JSON.parse(raw); } catch (e) { return; }
                if (!Array.isArray(ids)) return;
                ids
                  .filter(function (id) {
                    var el = document.getElementById(id);
                    return el && el.tagName === "DETAILS";
                  })
                  .sort(function (a, b) {
                    return depth(document.getElementById(a)) - depth(document.getElementById(b));
                  })
                  .forEach(function (id) {
                    var d = document.getElementById(id);
                    if (d) d.open = true;
                  });
              }

              function saveScroll() {
                var aside = document.querySelector(".layout-nav");
                if (!aside) return;
                try { sessionStorage.setItem(KEY_SCROLL, String(aside.scrollTop)); } catch (e) {}
              }

              function restoreScroll() {
                var aside = document.querySelector(".layout-nav");
                var raw = sessionStorage.getItem(KEY_SCROLL);
                if (!aside || raw == null) return;
                var top = parseInt(raw, 10);
                if (isNaN(top)) return;
                requestAnimationFrame(function () {
                  aside.scrollTop = top;
                  requestAnimationFrame(function () {
                    aside.scrollTop = top;
                  });
                });
              }

              function bindNavTree() {
                restoreOpen();
                persistOpen();
                restoreScroll();

                var aside = document.querySelector(".layout-nav");
                if (aside) {
                  var scrollTimer;
                  aside.addEventListener(
                    "scroll",
                    function () {
                      clearTimeout(scrollTimer);
                      scrollTimer = setTimeout(saveScroll, 120);
                    },
                    { passive: true }
                  );
                  aside.addEventListener("click", function (e) {
                    if (e.target.closest && e.target.closest("a")) saveScroll();
                  });
                }
                window.addEventListener("beforeunload", saveScroll);

                document.querySelectorAll(".nav-tree details").forEach(function (d) {
                  d.addEventListener("toggle", function () {
                    if (d.open) {
                      var p = d.parentElement;
                      if (p) {
                        p.querySelectorAll(":scope > details").forEach(function (sib) {
                          if (sib !== d) sib.open = false;
                        });
                      }
                    }
                    persistOpen();
                  });
                });
              }

              bindNavTree();
              };
            })();
            """
        );
        sb.AppendLine("</script>");
    }

    /// <summary>窄屏：目录/书签抽屉、遮罩与正文优先展示。</summary>
    private static void AppendMobileShellScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine(
            """
            (function () {
              var MQ = window.matchMedia("(max-width: 960px)");
              var navBtn = document.getElementById("site-mobile-nav-toggle");
              var asideBtn = document.getElementById("site-mobile-aside-toggle");
              var backdrop = document.getElementById("site-mobile-backdrop");
              var nav = document.getElementById("layout-nav");
              var tags = document.getElementById("layout-tags");
              if (!navBtn || !asideBtn) return;

              function setBackdrop(on) {
                if (!backdrop) return;
                backdrop.hidden = !on;
                backdrop.setAttribute("aria-hidden", on ? "false" : "true");
              }

              function closePanels() {
                document.body.classList.remove("mobile-nav-open", "mobile-aside-open", "mobile-panel-open");
                navBtn.classList.remove("is-active");
                asideBtn.classList.remove("is-active");
                navBtn.setAttribute("aria-expanded", "false");
                asideBtn.setAttribute("aria-expanded", "false");
                setBackdrop(false);
              }

              function openNav() {
                document.body.classList.add("mobile-nav-open", "mobile-panel-open");
                document.body.classList.remove("mobile-aside-open");
                navBtn.classList.add("is-active");
                asideBtn.classList.remove("is-active");
                navBtn.setAttribute("aria-expanded", "true");
                asideBtn.setAttribute("aria-expanded", "false");
                setBackdrop(true);
              }

              function openAside() {
                document.body.classList.add("mobile-aside-open", "mobile-panel-open");
                document.body.classList.remove("mobile-nav-open");
                asideBtn.classList.add("is-active");
                navBtn.classList.remove("is-active");
                asideBtn.setAttribute("aria-expanded", "true");
                navBtn.setAttribute("aria-expanded", "false");
                setBackdrop(true);
              }

              navBtn.addEventListener("click", function () {
                if (!MQ.matches) return;
                if (document.body.classList.contains("mobile-nav-open")) closePanels();
                else openNav();
              });

              asideBtn.addEventListener("click", function () {
                if (!MQ.matches) return;
                if (document.body.classList.contains("mobile-aside-open")) closePanels();
                else openAside();
              });

              if (backdrop) backdrop.addEventListener("click", closePanels);

              document.addEventListener("keydown", function (e) {
                if (e.key === "Escape") closePanels();
              });

              if (typeof MQ.addEventListener === "function") {
                MQ.addEventListener("change", function (e) {
                  if (!e.matches) closePanels();
                });
              } else if (typeof MQ.addListener === "function") {
                MQ.addListener(function (e) {
                  if (!e.matches) closePanels();
                });
              }

              if (nav) {
                nav.addEventListener("click", function (e) {
                  if (MQ.matches && e.target.closest && e.target.closest("a")) closePanels();
                });
              }

              if (tags) {
                tags.addEventListener("click", function (e) {
                  if (!MQ.matches || !e.target.closest || !e.target.closest("a")) return;
                  if (e.target.closest(".search-aside-wrap input")) return;
                  closePanels();
                });
              }
            })();
            """
        );
        sb.AppendLine("</script>");
    }

    /// <summary>
    /// 降低右键另存、拖拽拖走图片的概率（无法防止截图或开发者工具下载）。
    /// </summary>
    private static void AppendImageProtectionScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine(
            """
            (function () {
              function isImg(el) {
                return el && el.tagName === "IMG";
              }
              document.addEventListener(
                "contextmenu",
                function (e) {
                  if (isImg(e.target)) e.preventDefault();
                },
                false
              );
              document.addEventListener(
                "dragstart",
                function (e) {
                  if (isImg(e.target)) e.preventDefault();
                },
                false
              );
            })();
            """
        );
        sb.AppendLine("</script>");
    }

    /// <summary>
    /// 顶栏主题：强调色（预设 + 取色器）、灰度模式与夜间模式，偏好写入 localStorage。
    /// </summary>
    private static void AppendTopbarThemeScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine(
            """
            (function () {
              var KEY_ACCENT = "mindmapblog-accent";
              var KEY_BW = "mindmapblog-bw";
              var KEY_DARK = "mindmapblog-dark";
              var DEFAULT_ACCENT = "#4338ca";

              function hexToRgb(hex) {
                hex = (hex || "").replace(/^#/, "");
                if (hex.length === 3)
                  hex = hex.split("").map(function (c) { return c + c; }).join("");
                var n = parseInt(hex, 16);
                if (isNaN(n) || hex.length !== 6) return { r: 67, g: 56, b: 202 };
                return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
              }

              function rgbToHex(r, g, b) {
                return (
                  "#" +
                  [r, g, b]
                    .map(function (x) {
                      var h = Math.max(0, Math.min(255, Math.round(x))).toString(16);
                      return h.length === 1 ? "0" + h : h;
                    })
                    .join("")
                );
              }

              function shadeHex(hex, factor) {
                var c = hexToRgb(hex);
                return rgbToHex(c.r * factor, c.g * factor, c.b * factor);
              }

              function accentSoftRgba(hex, a) {
                var c = hexToRgb(hex);
                return "rgba(" + c.r + "," + c.g + "," + c.b + "," + a + ")";
              }

              function applyAccent(hex) {
                var root = document.documentElement;
                if (!/^#[0-9A-Fa-f]{6}$/.test(hex)) hex = DEFAULT_ACCENT;
                root.style.setProperty("--accent", hex);
                root.style.setProperty("--accent-deep", shadeHex(hex, 0.82));
                root.style.setProperty("--accent-soft", accentSoftRgba(hex, 0.11));
                root.style.setProperty("--accent-glow", accentSoftRgba(hex, 0.35));
                var picker = document.getElementById("site-accent-picker");
                if (picker) picker.value = hex;
                try {
                  localStorage.setItem(KEY_ACCENT, hex);
                } catch (e) {}
              }

              function applyBw(on) {
                var root = document.documentElement;
                if (on) root.classList.add("theme-bw");
                else root.classList.remove("theme-bw");
                var btn = document.getElementById("site-theme-bw-toggle");
                if (btn) {
                  btn.setAttribute("aria-pressed", on ? "true" : "false");
                  btn.classList.toggle("is-active", on);
                }
                try {
                  localStorage.setItem(KEY_BW, on ? "1" : "0");
                } catch (e) {}
              }

              function applyDark(on) {
                var root = document.documentElement;
                if (on) root.classList.add("theme-dark");
                else root.classList.remove("theme-dark");
                var btn = document.getElementById("site-theme-dark-toggle");
                if (btn) {
                  btn.setAttribute("aria-pressed", on ? "true" : "false");
                  btn.classList.toggle("is-active", on);
                }
                try {
                  localStorage.setItem(KEY_DARK, on ? "1" : "0");
                } catch (e) {}
              }

              document.addEventListener("DOMContentLoaded", function () {
                var accent = DEFAULT_ACCENT;
                try {
                  accent = localStorage.getItem(KEY_ACCENT) || DEFAULT_ACCENT;
                } catch (e) {}
                applyAccent(accent);

                var bw = false;
                try {
                  bw = localStorage.getItem(KEY_BW) === "1";
                } catch (e) {}
                applyBw(bw);

                var dark = false;
                try {
                  dark = localStorage.getItem(KEY_DARK) === "1";
                } catch (e) {}
                applyDark(dark);

                document.querySelectorAll(".site-topbar-swatch").forEach(function (btn) {
                  btn.addEventListener("click", function () {
                    var h = btn.getAttribute("data-accent");
                    if (h) applyAccent(h);
                  });
                });

                var picker = document.getElementById("site-accent-picker");
                if (picker) {
                  picker.addEventListener("input", function () {
                    applyAccent(picker.value);
                  });
                  picker.addEventListener("change", function () {
                    applyAccent(picker.value);
                  });
                }

                var bwBtn = document.getElementById("site-theme-bw-toggle");
                if (bwBtn) {
                  bwBtn.addEventListener("click", function () {
                    applyBw(!document.documentElement.classList.contains("theme-bw"));
                  });
                }

                var darkBtn = document.getElementById("site-theme-dark-toggle");
                if (darkBtn) {
                  darkBtn.addEventListener("click", function () {
                    applyDark(!document.documentElement.classList.contains("theme-dark"));
                  });
                }
              });
            })();
            """
        );
        sb.AppendLine("</script>");
    }

    /// <summary>
    /// 修订面板展开动画结束后才开启 aside 滚动，避免 grid 高度过渡期间出现滚动条闪烁。
    /// </summary>
    private static void AppendRevisionDockScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine(
            """
            (function () {
              var DELAY_MS = 240;
              document.querySelectorAll(".rev-dock").forEach(function (dock) {
                var aside = dock.querySelector(".rev-aside");
                if (!aside) return;

                function clearScroll() {
                  aside.classList.remove("rev-aside--scrollable");
                }

                dock.addEventListener("toggle", function () {
                  clearScroll();
                  if (!dock.open) return;
                  window.setTimeout(function () {
                    if (dock.open) aside.classList.add("rev-aside--scrollable");
                  }, DELAY_MS);
                });
              });
            })();
            """
        );
        sb.AppendLine("</script>");
    }

    public static Dictionary<string, int> CountBookmarks(IReadOnlyList<BlogArticle> articles)
    {
        var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in articles)
        {
            foreach (var bm in a.Bookmarks)
                d[bm] = d.GetValueOrDefault(bm) + 1;
        }

        return d;
    }

    /// <summary>左侧：「文件导图节点」树；其下为计划日期。分支标题链到该分支文章列表页。</summary>
    public static string BuildLeftNavTree(
        IReadOnlyList<BlogArticle> articles,
        string scanRoot,
        string? activeHtmlFile,
        string? currentPageWebPath,
        SiteFileNames names)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<nav class=\"nav-tree\">");
        sb.AppendLine("<section class=\"nav-filetree-section\" aria-label=\"文件导图节点\">");
        sb.AppendLine("<h3 class=\"aside-module-title\">文件导图节点</h3>");
        sb.AppendLine("<div class=\"nav-root\">");
        var tree = NavTreeBuilder.BuildFolderTree(articles, scanRoot);
        RenderFolderBranch(sb, tree, scanRoot, [], activeHtmlFile, currentPageWebPath, names.BranchPages);
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");
        sb.AppendLine("<hr class=\"nav-major-divider\" />");
        AppendCalendarNav(sb, articles, activeHtmlFile, currentPageWebPath, names);
        sb.AppendLine("</nav>");
        return sb.ToString();
    }

    /// <summary>按提醒日期的 年→月→日 树；日下一级为当天带提醒的文章链接。</summary>
    private static void AppendCalendarNav(
        StringBuilder sb,
        IReadOnlyList<BlogArticle> articles,
        string? activeHtmlFile,
        string? currentPageWebPath,
        SiteFileNames names)
    {
        var planned = articles.Where(a => a.ReminderAt.HasValue).ToList();
        if (planned.Count == 0)
            return;

        sb.AppendLine("<section class=\"nav-cal-section\" aria-label=\"计划日期\">");
        sb.AppendLine("<details class=\"nav-cal-fold\" open>");
        sb.AppendLine("<summary class=\"nav-cal-fold-summary\"><h3 class=\"aside-module-title\">计划日期（提醒）</h3></summary>");
        sb.AppendLine("<div class=\"nav-cal-fold-body\">");
        sb.AppendLine("<div class=\"nav-cal-root\">");

        foreach (var yg in planned.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Year).OrderBy(g => g.Key))
        {
            var year = yg.Key;
            var yHref = SitePathHelper.RelFromTo(currentPageWebPath, names.GetCalendarYearPage(year));
            var yId = BranchNav.CalendarYearDetailsId(year);
            sb.Append("<details class=\"nav-cal nav-cal-year\" id=\"").Append(WebUtility.HtmlEncode(yId)).AppendLine("\">");
            sb.Append("<summary class=\"nav-cal-summary nav-folder-summary\"><a class=\"nav-branch-title\" href=\"")
                .Append(WebUtility.HtmlEncode(yHref))
                .Append("\" onclick=\"event.stopPropagation()\">")
                .Append(year).Append("年")
                .AppendLine("</a></summary>");
            sb.AppendLine("<div class=\"nav-cal-body\">");

            foreach (var mg in yg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Month).OrderBy(g => g.Key))
            {
                var month = mg.Key;
                var mHref = SitePathHelper.RelFromTo(currentPageWebPath, names.GetCalendarMonthPage(year, month));
                var mId = BranchNav.CalendarMonthDetailsId(year, month);
                sb.Append("<details class=\"nav-cal nav-cal-month\" id=\"").Append(WebUtility.HtmlEncode(mId)).AppendLine("\">");
                sb.Append("<summary class=\"nav-cal-summary nav-folder-summary\"><a class=\"nav-branch-title\" href=\"")
                    .Append(WebUtility.HtmlEncode(mHref))
                    .Append("\" onclick=\"event.stopPropagation()\">")
                    .Append(month).Append("月")
                    .AppendLine("</a></summary>");
                sb.AppendLine("<div class=\"nav-cal-body\">");

                foreach (var dg in mg.GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Date).OrderBy(g => g.Key))
                {
                    var date = dg.Key;
                    var dHref = SitePathHelper.RelFromTo(
                        currentPageWebPath,
                        names.GetCalendarDayPage(date.Year, date.Month, date.Day));
                    var dId = BranchNav.CalendarDayDetailsId(date.Year, date.Month, date.Day);
                    sb.Append("<details class=\"nav-cal nav-cal-day\" id=\"").Append(WebUtility.HtmlEncode(dId)).AppendLine("\">");
                    sb.Append("<summary class=\"nav-cal-summary nav-folder-summary\"><a class=\"nav-branch-title\" href=\"")
                        .Append(WebUtility.HtmlEncode(dHref))
                        .Append("\" onclick=\"event.stopPropagation()\">")
                        .Append(date.Day).Append("日")
                        .AppendLine("</a></summary>");
                    sb.AppendLine("<div class=\"nav-cal-body\">");
                    sb.AppendLine("<ul class=\"nav-articles nav-cal-articles\">");
                    foreach (var art in dg.OrderBy(a => a.ReminderAt))
                    {
                        var hf = art.HtmlFileName;
                        var relArt = SitePathHelper.RelFromTo(currentPageWebPath, hf);
                        var cls = string.Equals(activeHtmlFile, hf, StringComparison.OrdinalIgnoreCase)
                            ? " class=\"is-active\""
                            : "";
                        var clock = art.ReminderAt!.Value.ToLocalTime().ToString("HH:mm");
                        sb.Append("  <li").Append(cls).Append("><span class=\"nav-cal-time\">")
                            .Append(WebUtility.HtmlEncode(clock))
                            .Append("</span><a href=\"").Append(WebUtility.HtmlEncode(relArt)).Append("\">")
                            .Append(WebUtility.HtmlEncode(art.Title))
                            .AppendLine("</a></li>");
                    }

                    sb.AppendLine("</ul>");
                    sb.AppendLine("</div>");
                    sb.AppendLine("</details>");
                }

                sb.AppendLine("</div>");
                sb.AppendLine("</details>");
            }

            sb.AppendLine("</div>");
            sb.AppendLine("</details>");
        }

        sb.AppendLine("</div>");
        AppendCalendarVisualNav(sb, planned, currentPageWebPath, names);
        sb.AppendLine("</div>");
        sb.AppendLine("</details>");
        sb.AppendLine("</section>");
        sb.AppendLine(
            """
            <script>
            (function () {
              var fold = document.querySelector(".nav-cal-fold");
              if (!fold) return;
              try {
                if (window.matchMedia && window.matchMedia("(max-width: 960px)").matches) {
                  fold.open = false;
                } else {
                  fold.open = true;
                }
              } catch (e) {}
            })();
            </script>
            """
        );
    }

    private static void AppendCalendarVisualNav(
        StringBuilder sb,
        IReadOnlyList<BlogArticle> planned,
        string? currentPageWebPath,
        SiteFileNames names)
    {
        var dayMap = planned
            .GroupBy(a => a.ReminderAt!.Value.ToLocalTime().Date)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());
        if (dayMap.Count == 0)
            return;

        var ymList = dayMap.Keys
            .Select(d => (d.Year, d.Month))
            .Distinct()
            .OrderBy(t => t.Year).ThenBy(t => t.Month)
            .ToList();
        var latest = ymList[^1];
        var yDefault = latest.Year;
        var mDefault = latest.Month;

        sb.AppendLine("<div class=\"nav-cal-visual\" data-default-mode=\"month\">");
        sb.AppendLine("<div class=\"nav-cal-visual-head\">");
        sb.AppendLine("<p class=\"nav-cal-visual-title\">日历视图</p>");
        sb.AppendLine("<div class=\"nav-cal-mode-switch\" role=\"tablist\" aria-label=\"计划视图切换\">");
        sb.AppendLine("<button type=\"button\" class=\"nav-cal-mode-btn is-active\" data-mode=\"month\">月视图</button>");
        sb.AppendLine("<button type=\"button\" class=\"nav-cal-mode-btn\" data-mode=\"year\">年视图</button>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class=\"nav-cal-controls\" data-mode-panel=\"month\">");
        sb.AppendLine("<label class=\"nav-cal-select-label\" for=\"nav-cal-year-sel\">年份</label>");
        sb.AppendLine("<select id=\"nav-cal-year-sel\" class=\"nav-cal-select\">");
        foreach (var y in ymList.Select(t => t.Year).Distinct().OrderBy(y => y))
        {
            sb.Append("<option value=\"").Append(y).Append("\"")
                .Append(y == yDefault ? " selected" : "")
                .Append(">").Append(y).AppendLine("年</option>");
        }

        sb.AppendLine("</select>");
        sb.AppendLine("<label class=\"nav-cal-select-label\" for=\"nav-cal-month-sel\">月份</label>");
        sb.AppendLine("<div class=\"nav-cal-month-stepper\">");
        sb.AppendLine(
            "<button type=\"button\" class=\"nav-cal-step-btn\" id=\"nav-cal-prev-month\" aria-label=\"上一有计划的月份\">‹</button>");
        sb.AppendLine("<select id=\"nav-cal-month-sel\" class=\"nav-cal-select\">");
        foreach (var ym in ymList)
        {
            var key = $"{ym.Year:D4}-{ym.Month:D2}";
            sb.Append("<option value=\"").Append(WebUtility.HtmlEncode(key)).Append("\" data-year=\"")
                .Append(ym.Year).Append("\"")
                .Append(ym.Year == yDefault && ym.Month == mDefault ? " selected" : "")
                .Append(">").Append(ym.Month).AppendLine("月</option>");
        }

        sb.AppendLine("</select>");
        sb.AppendLine(
            "<button type=\"button\" class=\"nav-cal-step-btn\" id=\"nav-cal-next-month\" aria-label=\"下一有计划的月份\">›</button>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<div class=\"nav-cal-visual-panels\">");
        foreach (var ym in ymList)
        {
            var ymKey = $"{ym.Year:D4}-{ym.Month:D2}";
            var isActive = ym.Year == yDefault && ym.Month == mDefault;
            sb.Append("<div class=\"nav-cal-month-panel")
                .Append(isActive ? " is-active" : "")
                .Append("\" data-ym=\"").Append(ymKey).Append("\" data-mode=\"month\"")
                .Append(isActive ? "" : " hidden")
                .AppendLine(">");
            sb.Append("<p class=\"nav-cal-panel-title\">").Append(ym.Month).AppendLine("月</p>");
            AppendMonthGrid(sb, dayMap, ym.Year, ym.Month, currentPageWebPath, names);
            sb.AppendLine("</div>");
        }

        foreach (var y in ymList.Select(t => t.Year).Distinct().OrderBy(y => y))
        {
            var isActive = y == yDefault;
            sb.Append("<div class=\"nav-cal-year-panel")
                .Append(isActive ? " is-active" : "")
                .Append("\" data-year=\"").Append(y).Append("\" data-mode=\"year\"")
                .Append(" hidden")
                .AppendLine(">");
            sb.Append("<p class=\"nav-cal-panel-title\">").Append(y).AppendLine("年计划总览</p>");
            sb.AppendLine("<div class=\"nav-cal-year-grid\">");
            for (var m = 1; m <= 12; m++)
            {
                var monthCount = dayMap.Where(kv => kv.Key.Year == y && kv.Key.Month == m).Sum(kv => kv.Value);
                if (monthCount > 0)
                {
                    var monthHref = SitePathHelper.RelFromTo(currentPageWebPath, names.GetCalendarMonthPage(y, m));
                    sb.Append("<a class=\"nav-cal-month-card has-task\" href=\"").Append(WebUtility.HtmlEncode(monthHref)).Append("\">")
                        .Append("<span class=\"m\">").Append(m).Append("月</span>")
                        .Append("<span class=\"c\">").Append(monthCount).AppendLine("项</span></a>");
                }
                else
                {
                    sb.Append("<span class=\"nav-cal-month-card\">")
                        .Append("<span class=\"m\">").Append(m).Append("月</span>")
                        .Append("<span class=\"c\">0项</span></span>");
                }
            }

            sb.AppendLine("</div></div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        AppendCalendarVisualScript(sb);
    }

    private static void AppendMonthGrid(
        StringBuilder sb,
        Dictionary<DateTime, int> dayMap,
        int year,
        int month,
        string? currentPageWebPath,
        SiteFileNames names)
    {
        var first = new DateTime(year, month, 1);
        var start = first.AddDays(-(int)first.DayOfWeek);
        var weekLabels = new[] { "日", "一", "二", "三", "四", "五", "六" };

        sb.AppendLine("<table class=\"nav-cal-grid\" aria-label=\"按日期查看任务\">");
        sb.AppendLine("<thead><tr>");
        foreach (var w in weekLabels)
            sb.Append("<th scope=\"col\">").Append(w).AppendLine("</th>");
        sb.AppendLine("</tr></thead><tbody>");
        for (var row = 0; row < 6; row++)
        {
            sb.AppendLine("<tr>");
            for (var col = 0; col < 7; col++)
            {
                var dt = start.AddDays(row * 7 + col);
                var inMonth = dt.Month == month;
                dayMap.TryGetValue(dt.Date, out var count);
                sb.Append("<td class=\"").Append(inMonth ? "in" : "out").Append("\">");
                if (count > 0)
                {
                    var dayHref = SitePathHelper.RelFromTo(currentPageWebPath, names.GetCalendarDayPage(dt.Year, dt.Month, dt.Day));
                    sb.Append("<a class=\"nav-cal-day has-task\" href=\"").Append(WebUtility.HtmlEncode(dayHref)).Append("\">")
                        .Append("<span class=\"d\">").Append(dt.Day).Append("</span>")
                        .Append("<span class=\"n\">").Append(count).Append("项</span></a>");
                }
                else
                {
                    sb.Append("<span class=\"nav-cal-day\">")
                        .Append("<span class=\"d\">").Append(dt.Day).Append("</span>")
                        .Append("<span class=\"n\">0</span></span>");
                }

                sb.AppendLine("</td>");
            }

            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");
    }

    private static void AppendCalendarVisualScript(StringBuilder sb)
    {
        sb.AppendLine("<script>");
        sb.AppendLine(
            """
            (function () {
              var root = document.querySelector(".nav-cal-visual");
              if (!root) return;
              var modeBtns = root.querySelectorAll(".nav-cal-mode-btn");
              var yearSel = root.querySelector("#nav-cal-year-sel");
              var monthSel = root.querySelector("#nav-cal-month-sel");
              var prevBtn = root.querySelector("#nav-cal-prev-month");
              var nextBtn = root.querySelector("#nav-cal-next-month");
              var monthPanels = root.querySelectorAll(".nav-cal-month-panel");
              var yearPanels = root.querySelectorAll(".nav-cal-year-panel");
              var mode = "month";

              function showMonth(ym) {
                monthPanels.forEach(function (p) {
                  p.hidden = p.getAttribute("data-ym") !== ym;
                  p.classList.toggle("is-active", !p.hidden);
                });
              }

              function showYear(y) {
                yearPanels.forEach(function (p) {
                  p.hidden = String(p.getAttribute("data-year")) !== String(y);
                  p.classList.toggle("is-active", !p.hidden);
                });
              }

              function syncMonthOptionsByYear() {
                if (!yearSel || !monthSel) return;
                var y = yearSel.value;
                var firstVisible = null;
                Array.prototype.forEach.call(monthSel.options, function (opt) {
                  var ok = opt.getAttribute("data-year") === y;
                  opt.hidden = !ok;
                  if (ok && !firstVisible) firstVisible = opt.value;
                });
                if (monthSel.options[monthSel.selectedIndex] && monthSel.options[monthSel.selectedIndex].hidden) {
                  monthSel.value = firstVisible || monthSel.value;
                }
              }

              function visibleMonthOptions() {
                if (!monthSel) return [];
                return Array.prototype.filter.call(monthSel.options, function (opt) {
                  return !opt.hidden;
                });
              }

              function stepMonth(delta) {
                if (!monthSel || mode !== "month") return;
                var opts = visibleMonthOptions();
                var cur = monthSel.options[monthSel.selectedIndex];
                var i = opts.indexOf(cur);
                if (i < 0 && opts.length) {
                  monthSel.value = opts[0].value;
                  showMonth(monthSel.value);
                  return;
                }
                var j = i + delta;
                if (j >= 0 && j < opts.length) {
                  monthSel.value = opts[j].value;
                  showMonth(monthSel.value);
                  return;
                }
                if (!yearSel) return;
                var years = Array.prototype.slice.call(yearSel.options);
                var yi = years.indexOf(yearSel.options[yearSel.selectedIndex]);
                if (delta < 0 && yi > 0) {
                  yearSel.selectedIndex = yi - 1;
                  syncMonthOptionsByYear();
                  var tail = visibleMonthOptions();
                  if (tail.length) {
                    monthSel.value = tail[tail.length - 1].value;
                    showMonth(monthSel.value);
                  }
                  showYear(yearSel.value);
                } else if (delta > 0 && yi < years.length - 1) {
                  yearSel.selectedIndex = yi + 1;
                  syncMonthOptionsByYear();
                  var head = visibleMonthOptions();
                  if (head.length) {
                    monthSel.value = head[0].value;
                    showMonth(monthSel.value);
                  }
                  showYear(yearSel.value);
                }
              }

              function applyMode(nextMode) {
                mode = nextMode;
                modeBtns.forEach(function (b) {
                  var active = b.getAttribute("data-mode") === mode;
                  b.classList.toggle("is-active", active);
                });
                var controls = root.querySelector(".nav-cal-controls");
                if (controls) controls.hidden = mode !== "month";
                monthPanels.forEach(function (p) { p.hidden = mode !== "month" || !p.classList.contains("is-active"); });
                yearPanels.forEach(function (p) { p.hidden = mode !== "year" || !p.classList.contains("is-active"); });
              }

              if (yearSel) {
                yearSel.addEventListener("change", function () {
                  syncMonthOptionsByYear();
                  if (mode === "month" && monthSel) showMonth(monthSel.value);
                  showYear(yearSel.value);
                });
              }

              if (monthSel) {
                monthSel.addEventListener("change", function () {
                  showMonth(monthSel.value);
                });
              }

              if (prevBtn) prevBtn.addEventListener("click", function () { stepMonth(-1); });
              if (nextBtn) nextBtn.addEventListener("click", function () { stepMonth(1); });

              modeBtns.forEach(function (btn) {
                btn.addEventListener("click", function () {
                  applyMode(btn.getAttribute("data-mode") || "month");
                });
              });

              syncMonthOptionsByYear();
              if (monthSel) showMonth(monthSel.value);
              if (yearSel) showYear(yearSel.value);
              applyMode(mode);
            })();
            """);
        sb.AppendLine("</script>");
    }

    private static void RenderFolderBranch(StringBuilder sb, FolderBranch branch, string scanRoot,
        List<string> folderPathPrefix, string? activeHtmlFile, string? currentPageWebPath,
        BranchPageNameRegistry branchPages)
    {
        foreach (var dir in branch.Dirs.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = folderPathPrefix.Concat(new[] { dir.Key }).ToList();
            var href = SitePathHelper.RelFromTo(currentPageWebPath, branchPages.GetFolderListPage(path));
            var detId = BranchNav.FolderBranchDetailsId(scanRoot, path);
            sb.Append("<details class=\"nav-folder\" id=\"").Append(WebUtility.HtmlEncode(detId)).AppendLine("\">");
            sb.Append("<summary class=\"nav-folder-summary\"><a class=\"nav-branch-title\" href=\"")
                .Append(WebUtility.HtmlEncode(href))
                .Append("\" onclick=\"event.stopPropagation()\">")
                .Append(WebUtility.HtmlEncode(dir.Key))
                .AppendLine("</a></summary>");
            sb.AppendLine("<div class=\"nav-folder-body\">");
            RenderFolderBranch(sb, dir.Value, scanRoot, path, activeHtmlFile, currentPageWebPath, branchPages);
            sb.AppendLine("</div>");
            sb.AppendLine("</details>");
        }

        foreach (var fileEntry in branch.MindmapFiles.OrderBy(kv => Path.GetFileName(kv.Key), StringComparer.OrdinalIgnoreCase))
        {
            var mmPath = fileEntry.Key;
            var fileLabel = Path.GetFileName(mmPath);
            var trie = NavTreeBuilder.BuildMapTrie(fileEntry.Value);
            var mmHref = SitePathHelper.RelFromTo(currentPageWebPath, branchPages.GetMmPrefixListPage(mmPath, ""));
            var mmFileId = BranchNav.MmFileDetailsId(mmPath);
            sb.Append("<details class=\"nav-mmfile\" id=\"").Append(WebUtility.HtmlEncode(mmFileId)).AppendLine("\">");
            sb.Append("<summary class=\"nav-mmfile-summary\"><a class=\"nav-branch-title\" href=\"")
                .Append(WebUtility.HtmlEncode(mmHref))
                .Append("\" onclick=\"event.stopPropagation()\">")
                .Append(WebUtility.HtmlEncode(fileLabel))
                .AppendLine("</a></summary>");
            sb.AppendLine("<div class=\"nav-mmfile-body\">");
            RenderMapTrie(sb, trie, mmPath, [], activeHtmlFile, currentPageWebPath, branchPages);
            sb.AppendLine("</div>");
            sb.AppendLine("</details>");
        }
    }

    private static void RenderMapTrie(StringBuilder sb, MapTrieNode node, string mmPath,
        List<string> prefixSegments, string? activeHtmlFile, string? currentPageWebPath,
        BranchPageNameRegistry branchPages)
    {
        foreach (var seg in node.Segments.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var nextPrefix = new List<string>(prefixSegments) { seg.Key };
            var joined = string.Join("/", nextPrefix);
            var href = SitePathHelper.RelFromTo(
                currentPageWebPath,
                branchPages.GetMmPrefixListPage(mmPath, joined));
            var nodeDetId = BranchNav.MmNodeDetailsId(mmPath, joined);

            sb.Append("<details class=\"nav-mmnod\" id=\"").Append(WebUtility.HtmlEncode(nodeDetId)).AppendLine("\">");
            sb.Append("<summary class=\"nav-mmnod-summary\"><a class=\"nav-branch-title\" href=\"")
                .Append(WebUtility.HtmlEncode(href))
                .Append("\" onclick=\"event.stopPropagation()\">")
                .Append(WebUtility.HtmlEncode(seg.Key))
                .AppendLine("</a></summary>");
            sb.AppendLine("<div class=\"nav-mmnod-body\">");
            RenderMapTrie(sb, seg.Value, mmPath, nextPrefix, activeHtmlFile, currentPageWebPath, branchPages);
            sb.AppendLine("</div>");
            sb.AppendLine("</details>");
        }

        if (node.ArticlesHere.Count == 0)
            return;

        sb.AppendLine("<ul class=\"nav-articles\">");
        foreach (var art in node.ArticlesHere.OrderByDescending(a => a.Modified))
        {
            var hf = art.HtmlFileName;
            var rel = SitePathHelper.RelFromTo(currentPageWebPath, hf);
            var cls = string.Equals(activeHtmlFile, hf, StringComparison.OrdinalIgnoreCase) ? " class=\"is-active\"" : "";
            sb.Append("  <li").Append(cls).Append("><a href=\"")
                .Append(WebUtility.HtmlEncode(rel)).Append("\">")
                .Append(WebUtility.HtmlEncode(art.Title))
                .AppendLine("</a></li>");
        }

        sb.AppendLine("</ul>");
    }

    public static string BuildTagAside(
        IReadOnlyList<BlogArticle> articles,
        string? highlightTag,
        SiteFileNames names,
        string? currentPageWebPath)
    {
        var counts = CountBookmarks(articles);
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"tag-aside-inner\">");
        sb.AppendLine("<h3 class=\"aside-module-title\">书签</h3>");

        if (counts.Count == 0)
        {
            sb.AppendLine("<p class=\"tag-aside-empty\">暂无书签</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        var maxC = counts.Values.Max();
        var minC = counts.Values.Min();

        sb.AppendLine("<div class=\"tag-cloud\" role=\"navigation\" aria-label=\"书签词云\">");
        foreach (var tag in counts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var file = SitePathHelper.RelFromTo(currentPageWebPath, names.TagPageFile(tag));
            var cnt = counts[tag];
            var active = highlightTag != null &&
                         string.Equals(tag, highlightTag, StringComparison.OrdinalIgnoreCase);
            var rem = TagCloudFontRem(cnt, minC, maxC).ToString("0.###", CultureInfo.InvariantCulture);
            var title = cnt + " 篇：" + tag;
            sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(file)).Append("\" class=\"tag-cloud-link")
                .Append(active ? " is-active" : "")
                .Append("\" style=\"font-size:")
                .Append(rem)
                .Append("rem\" title=\"")
                .Append(WebUtility.HtmlEncode(title))
                .Append("\">")
                .Append(WebUtility.HtmlEncode(tag))
                .AppendLine("</a>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    /// <summary>词云字号：篇数越多越大（对数缩放）。</summary>
    private static double TagCloudFontRem(int count, int minCount, int maxCount)
    {
        const double lo = 0.72;
        const double hi = 1.14;
        if (maxCount <= minCount)
            return (lo + hi) / 2;
        var logMin = Math.Log(Math.Max(1, minCount));
        var logMax = Math.Log(Math.Max(1, maxCount));
        var logC = Math.Log(Math.Max(1, count));
        var t = logMax <= logMin ? 1 : (logC - logMin) / (logMax - logMin);
        if (t < 0)
            t = 0;
        if (t > 1)
            t = 1;
        return lo + t * (hi - lo);
    }

    private static string BuildProfileAside(
        string aboutPageWebPath,
        string? currentPageWebPath,
        string? avatarSitePathFromRoot)
    {
        if (string.IsNullOrWhiteSpace(aboutPageWebPath))
            return "";

        var aboutHref = SitePathHelper.RelFromTo(currentPageWebPath, aboutPageWebPath);
        var sb = new StringBuilder();
        sb.AppendLine("<section class=\"aside-profile-wrap\" aria-label=\"站长\">");
        sb.Append("<a class=\"aside-profile-card\" href=\"").Append(WebUtility.HtmlEncode(aboutHref)).AppendLine("\">");
        sb.AppendLine("<span class=\"aside-profile-visual\">");
        if (!string.IsNullOrEmpty(avatarSitePathFromRoot))
        {
            var src = SitePathHelper.RelFromTo(currentPageWebPath, avatarSitePathFromRoot);
            sb.Append("<img class=\"aside-profile-avatar\" src=\"").Append(WebUtility.HtmlEncode(src))
                .Append("\" alt=\"\" width=\"80\" height=\"80\" decoding=\"async\"/>");
        }
        else
        {
            sb.AppendLine("<span class=\"aside-profile-placeholder\" aria-hidden=\"true\"></span>");
        }

        sb.AppendLine("</span>");
        sb.Append("<span class=\"aside-profile-quote\">")
            .Append(WebUtility.HtmlEncode(SiteProfile.Signature))
            .AppendLine("</span>");
        sb.AppendLine("<span class=\"aside-profile-cta\">关于我</span>");
        sb.AppendLine("</a>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    public const string GenerationHistoryPageFileName = "generation-history.html";
    public const string GitCommitHistoryPageFileName = "提交记录.html";

    /// <summary>右侧：站长卡片、书签词云、图册与全文搜索。</summary>
    public static string BuildRightAside(
        IReadOnlyList<BlogArticle> articles,
        string? highlightTag,
        SiteFileNames names,
        string? currentPageWebPath,
        IReadOnlyList<ArticleGalleryItem> galleryEntries,
        string? avatarSitePathFromRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"right-aside-stack\">");
        sb.AppendLine("<div class=\"aside-main-blocks\">");
        sb.AppendLine(BuildProfileAside(names.AboutPageWebPath, currentPageWebPath, avatarSitePathFromRoot));
        sb.AppendLine(BuildTagAside(articles, highlightTag, names, currentPageWebPath));
        sb.AppendLine(BuildGalleryAside(galleryEntries, names.GalleryPageWebPath, currentPageWebPath));
        if (!string.Equals(currentPageWebPath, names.SearchPageWebPath, StringComparison.OrdinalIgnoreCase))
            sb.AppendLine(BuildSearchAside(currentPageWebPath, names.SearchPageWebPath));
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string BuildGalleryAside(
        IReadOnlyList<ArticleGalleryItem> entries,
        string galleryPageWebPath,
        string? currentPageWebPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"gallery-aside-inner\">");
        sb.AppendLine("<h3 class=\"aside-module-title\">图册</h3>");
        sb.AppendLine("<p class=\"gallery-aside-lead\">正文里的图片 · 点此进入对应文章位置</p>");
        var galleryHref = SitePathHelper.RelFromTo(currentPageWebPath, galleryPageWebPath);

        if (entries.Count == 0)
        {
            sb.AppendLine("<p class=\"gallery-aside-hint\">导图文章正文内插入图片并重新生成后，缩略图会出现在这里；点击图片将打开该文章并定位到图中。</p>");
            sb.Append("<p class=\"gallery-aside-more-wrap\"><a href=\"")
                .Append(WebUtility.HtmlEncode(galleryHref))
                .Append("\" class=\"gallery-aside-more\">图册索引</a></p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        var preview = Math.Min(8, entries.Count);
        sb.AppendLine("<div class=\"gallery-aside-preview\" aria-label=\"文章配图预览\">");
        for (var i = 0; i < preview; i++)
        {
            var e = entries[i];
            var src = SitePathHelper.RelFromTo(currentPageWebPath, e.MediaPathFromSiteRoot);
            var articleHref = SitePathHelper.RelFromTo(currentPageWebPath, e.ArticleWebPath);
            var jump = articleHref + "#img-" + e.ImageIndexInArticle;
            sb.Append("<a class=\"gallery-aside-thumb\" href=\"")
                .Append(WebUtility.HtmlEncode(jump))
                .Append("\" title=\"")
                .Append(WebUtility.HtmlEncode(e.Caption))
                .Append("\"><img src=\"")
                .Append(WebUtility.HtmlEncode(src))
                .Append("\" alt=\"")
                .Append(WebUtility.HtmlEncode(e.Caption))
                .AppendLine("\" loading=\"lazy\" /></a>");
        }

        sb.AppendLine("</div>");
        sb.Append("<p class=\"gallery-aside-more-wrap\"><a href=\"")
            .Append(WebUtility.HtmlEncode(galleryHref))
            .Append("\" class=\"gallery-aside-more\">图册索引 · ")
            .Append(entries.Count)
            .AppendLine(" 张</a></p>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    public static string BuildSearchPageMain(string? currentPageWebPath, string searchPageWebPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"page-search\">");
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<h1 class=\"page-title\">搜索</h1>");
        sb.AppendLine("<p class=\"page-lead\">搜索全部内容：文章标题、正文、图片说明、书签、分区与导图文件名。</p>");
        sb.AppendLine("</header>");
        sb.AppendLine(BuildSearchAside(currentPageWebPath, searchPageWebPath));
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private static string BuildSearchAside(string? currentPageWebPath, string searchPageWebPath)
    {
        var pagePath = string.IsNullOrEmpty(currentPageWebPath?.Trim())
            ? "index.html"
            : currentPageWebPath!.Trim().Replace('\\', '/');

        var indexHref = SitePathHelper.RelFromTo(currentPageWebPath, "data/search-index.json");
        var scriptHref = SitePathHelper.RelFromTo(currentPageWebPath, "search-aside.js");

        var sb = new StringBuilder();
        sb.Append("<section class=\"search-aside-wrap\" id=\"site-search-aside\" aria-label=\"全文搜索\"");
        sb.Append(" data-index-href=\"").Append(WebUtility.HtmlEncode(indexHref)).Append("\"");
        sb.Append(" data-page-path=\"").Append(WebUtility.HtmlEncode(pagePath)).AppendLine("\">");
        var searchPageHref = SitePathHelper.RelFromTo(currentPageWebPath, searchPageWebPath);
        sb.Append("<h3 class=\"aside-module-title\"><a class=\"search-aside-title-link\" href=\"")
            .Append(WebUtility.HtmlEncode(searchPageHref))
            .AppendLine("\">搜索</a></h3>");
        sb.AppendLine("<p class=\"search-aside-lead\">标题 · 正文 · 书签 · 配图说明 · 分区 · 导图文件名</p>");
        sb.AppendLine("<label class=\"search-aside-label visually-hidden\" for=\"site-search-q\">搜索文章</label>");
        sb.AppendLine(
            "<input type=\"search\" id=\"site-search-q\" class=\"search-aside-input\" autocomplete=\"off\" placeholder=\"输入关键词…\" />");
        sb.AppendLine("<p class=\"search-aside-status\" id=\"site-search-status\" aria-live=\"polite\"></p>");
        sb.AppendLine("<ul class=\"search-aside-list\" id=\"site-search-list\" hidden></ul>");
        sb.AppendLine("</section>");
        sb.Append("<script src=\"").Append(WebUtility.HtmlEncode(scriptHref)).AppendLine("\" defer></script>");
        return sb.ToString();
    }

    /// <summary>页脚：居中版权；右侧「网站生成 · 提交记录 · 词频 · RSS」。</summary>
    private static string BuildSiteFooter(string? currentPageWebPath, SiteFileNames names)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<footer class=\"site-footer\" role=\"contentinfo\">");
        sb.AppendLine("<div class=\"site-footer-inner\">");
        sb.AppendLine("<div class=\"site-footer-leading\"></div>");
        sb.Append("<p class=\"site-footer-copy\">").Append(WebUtility.HtmlEncode(SiteFooterCopyrightLine))
            .AppendLine("</p>");
        sb.Append(BuildUtilityLinksRow(currentPageWebPath, names, "site-footer-links gen-aside-links"));
        sb.AppendLine("</div>");
        sb.AppendLine("</footer>");
        return sb.ToString();
    }

    private static string BuildUtilityLinksRow(string? currentPageWebPath, SiteFileNames names, string wrapperClass)
    {
        var sb = new StringBuilder();
        var genMoreHref = SitePathHelper.RelFromTo(currentPageWebPath, GenerationHistoryPageFileName);
        var gitHref = SitePathHelper.RelFromTo(currentPageWebPath, GitCommitHistoryPageFileName);
        var wordFreqHref = SitePathHelper.RelFromTo(currentPageWebPath, names.WordFrequencyPageWebPath);
        var rssHref = SitePathHelper.RelFromTo(currentPageWebPath, names.RssFeedWebPath);
        sb.Append("<div class=\"").Append(wrapperClass).AppendLine("\">");
        sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(genMoreHref))
            .Append("\" class=\"gen-aside-main-link\">网站生成</a>");
        sb.Append("<span class=\"gen-aside-sep\" aria-hidden=\"true\">·</span>");
        sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(gitHref))
            .Append("\" class=\"gen-aside-main-link\" title=\"扫描目录 Git 提交历史\">提交记录</a>");
        sb.Append("<span class=\"gen-aside-sep\" aria-hidden=\"true\">·</span>");
        sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(wordFreqHref))
            .Append("\" class=\"gen-aside-main-link\" title=\"全文分词词频\">词频</a>");
        sb.Append("<span class=\"gen-aside-sep\" aria-hidden=\"true\">·</span>");
        sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(rssHref))
            .Append("\" class=\"gen-aside-rss-link\" title=\"RSS 订阅\">RSS</a>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    /// <summary>生成独立图册页主体：按文章分组，缩略图链到正文内锚点。</summary>
    public static string BuildGalleryPageMain(
        IReadOnlyList<ArticleGalleryItem> entries,
        IReadOnlyList<BlogArticle> sortedArticles,
        string galleryPageWebPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"page-gallery\">");
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<h1 class=\"page-title\">图册</h1>");
        sb.AppendLine("<p class=\"page-lead\">汇总各篇文章正文中的配图。缩略图链到对应文章页面，并自动滚动到文中的图片位置。</p>");
        sb.AppendLine("</header>");

        if (entries.Count == 0)
        {
            sb.AppendLine("<p class=\"gallery-empty\">暂无配图。请在思维导图文章节点正文中插入图片并重新生成站点。</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        var titleByPath = sortedArticles.ToDictionary(a => a.HtmlFileName, a => a.Title, StringComparer.OrdinalIgnoreCase);

        foreach (var grp in entries.GroupBy(e => e.ArticleWebPath, StringComparer.OrdinalIgnoreCase))
        {
            var pageTitle = titleByPath.GetValueOrDefault(grp.Key) ?? grp.Key;
            sb.Append("<h2 class=\"gallery-group-title\">")
                .Append(WebUtility.HtmlEncode(pageTitle))
                .AppendLine("</h2>");
            sb.AppendLine("<div class=\"gallery-grid\">");
            foreach (var item in grp.OrderBy(x => x.ImageIndexInArticle))
            {
                var thumb = SitePathHelper.RelFromTo(galleryPageWebPath, item.MediaPathFromSiteRoot);
                var toArticle = SitePathHelper.RelFromTo(galleryPageWebPath, item.ArticleWebPath) + "#img-" +
                                item.ImageIndexInArticle;
                sb.AppendLine("<figure class=\"gallery-card\">");
                sb.Append("<a class=\"gallery-card-link\" href=\"")
                    .Append(WebUtility.HtmlEncode(toArticle))
                    .Append("\">");
                sb.Append("<img src=\"")
                    .Append(WebUtility.HtmlEncode(thumb))
                    .Append("\" alt=\"")
                    .Append(WebUtility.HtmlEncode(item.Caption))
                    .AppendLine("\" loading=\"lazy\" /></a>");
                sb.Append("<figcaption class=\"gallery-card-cap\">")
                    .Append(WebUtility.HtmlEncode(item.Caption))
                    .AppendLine("</figcaption>");
                sb.AppendLine("</figure>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        return sb.ToString();
    }
}
