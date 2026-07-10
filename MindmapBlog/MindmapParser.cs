using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MindmapBlog;

/// <summary>
/// 解析 FreeMind / Docear 的 .mm。
/// 在图册根节点之下的<strong>任意深度</strong>查找带互联网图标（BUILTIN=internet）的节点作为文章根；
/// 带「不发布」图标（由导图「变量」→「不发布的图标」配置，默认 closed）的节点及其子树跳过，不作为文章也不写入正文（含图册根节点）；
/// 名为「变量」的节点仅作站点配置，不发布为文章。
/// 分区路径为从根到该节点父链上的节点标题（如 2026 / 5 / 1）；
/// 书签从节点明细中解析 #标签；无 # 时仅用图册根节点标题归类（不再用路径作伪标签）。
/// </summary>
public static class MindmapParser
{
    private static readonly XName NodeName = XName.Get("node");
    private static readonly XName HookName = XName.Get("hook");
    private static readonly XName MapName = XName.Get("map");
    private static readonly XName RichContentName = XName.Get("richcontent");
    private static readonly XName IconName = XName.Get("icon");
    private static readonly Regex SentenceCountRegex = new(@"[。！？!?；;]+", RegexOptions.Compiled);
    private static readonly Regex ScriptTagRegex = new(@"<script\b[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VisualStyleRegex = new(
        @"(<font\b)|(<span\b)|color\s*:|font-weight\s*:|font-style\s*:|text-decoration\s*:|background\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineBlockOpenRegex = new(@"<(p|div|section|article|li|ul|ol)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineBlockCloseRegex = new(@"</(p|div|section|article|li|ul|ol)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineBrRegex = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    internal const string SiteVariablesNodeLabel = "变量";
    internal const string UnpublishIconsVariableLabel = "不发布的图标";

    private static IReadOnlySet<string> UnpublishIconSet => SiteProfile.UnpublishIcons;

    private static IReadOnlyDictionary<string, string>? _activeStyleFormats;

    public static IReadOnlyList<BlogArticle> ExtractArticles(string mmFilePath)
    {
        var fullPath = Path.GetFullPath(mmFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("找不到 .mm 文件。", fullPath);

        var doc = MindmapXmlLoader.Load(fullPath);
        var map = doc.Root ?? throw new InvalidOperationException("无效的 .mm：缺少根元素。");
        if (map.Name != MapName)
            throw new InvalidOperationException("无效的 .mm：根元素应为 map。");

        var notebookRoot = map.Elements(NodeName).FirstOrDefault()
            ?? throw new InvalidOperationException("无效的 .mm：map 下缺少根 node。");

        var notebookTitle = DecodeText(notebookRoot.Attribute("TEXT")?.Value) ?? "(未命名)";
        var mmDir = Path.GetDirectoryName(fullPath) ?? "";
        var styleFormats = ParseStyleFormats(map);

        var articles = new List<BlogArticle>();

        _activeStyleFormats = styleFormats;
        try
        {
            foreach (var articleRoot in notebookRoot.Descendants(NodeName))
            {
                if (ReferenceEquals(articleRoot, notebookRoot))
                    continue;
                if (!HasInternetPublishIcon(articleRoot))
                    continue;
                if (IsWithinUnpublishSubtree(articleRoot, notebookRoot))
                    continue;
                if (IsSiteVariablesNode(articleRoot))
                    continue;

                var structuralSection = BuildStructuralPath(notebookRoot, articleRoot);
                var title = ResolveArticleTitle(articleRoot);
                var id = articleRoot.Attribute("ID")?.Value ?? "";
                var created = ParseMindTime(articleRoot.Attribute("CREATED")?.Value) ?? DateTimeOffset.UtcNow;
                var modified = ParseMindTime(articleRoot.Attribute("MODIFIED")?.Value) ?? created;

                var bookmarks = ExtractBookmarks(articleRoot, notebookTitle);
                var blocks = BuildBodyBlocks(articleRoot, mmDir);
                var reminderAt = ExtractReminderAt(articleRoot);
                articles.Add(new BlogArticle
                {
                    SourceMmPath = fullPath,
                    NotebookTitle = notebookTitle,
                    StructuralSection = structuralSection,
                    Bookmarks = bookmarks,
                    Title = title,
                    ArticleNodeId = id,
                    Created = created,
                    Modified = modified,
                    ReminderAt = reminderAt,
                    Blocks = blocks,
                });
            }
        }
        finally
        {
            _activeStyleFormats = null;
        }

        return articles;
    }

    /// <summary>从任意 .mm 中首个名为「变量」的节点读取站点配置；找不到则返回 null。</summary>
    public static SiteVariables? TryFindSiteVariables(IEnumerable<string> mmFilePaths)
    {
        foreach (var file in mmFilePaths.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(file);
                if (!File.Exists(fullPath))
                    continue;

                var doc = MindmapXmlLoader.Load(fullPath);
                var map = doc.Root;
                if (map?.Name != MapName)
                    continue;

                _activeStyleFormats = ParseStyleFormats(map);
                try
                {
                    var variablesNode = map.Descendants(NodeName)
                        .FirstOrDefault(IsSiteVariablesNode);
                    if (variablesNode == null)
                        continue;

                    string? blogTitle = null;
                    string? signature = null;
                    string? aboutBody = null;
                    string? aboutBodyHtml = null;
                    List<string>? wordFrequencyFilter = null;
                    List<string>? wordFrequencyForce = null;
                    List<string>? unpublishIcons = null;

                    foreach (var child in variablesNode.Elements(NodeName))
                    {
                        var label = GetNodeLabel(child);
                        if (string.IsNullOrEmpty(label))
                            continue;

                        switch (label)
                        {
                            case "博客标题":
                                blogTitle = ExtractVariablePlainText(child);
                                break;
                            case "我的个性签名":
                                signature = ExtractVariablePlainText(child);
                                break;
                            case "关于我":
                            {
                                var chunks = child.Elements(NodeName)
                                    .Select(ParseNodeContent)
                                    .Where(c => c != null)
                                    .Cast<ParsedNodeContent>()
                                    .ToList();
                                if (chunks.Count == 0)
                                {
                                    var single = ParseNodeContent(child);
                                    if (single != null)
                                        chunks.Add(single);
                                }

                                if (chunks.Count > 0)
                                {
                                    aboutBody = string.Join("\n", chunks.Select(c => c.PlainText));
                                    if (chunks.Any(c => c.IsRichHtml))
                                        aboutBodyHtml = string.Join("\n", chunks.Select(c => c.Html));
                                }

                                break;
                            }
                            case "词频过滤":
                                wordFrequencyFilter = child.Elements(NodeName)
                                    .Select(GetNodeLabel)
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Select(s => s.Trim())
                                    .ToList();
                                break;
                            case "词频强制":
                                wordFrequencyForce = child.Elements(NodeName)
                                    .Select(GetNodeLabel)
                                    .Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Select(s => s.Trim())
                                    .ToList();
                                break;
                            case UnpublishIconsVariableLabel:
                                unpublishIcons = ExtractUnpublishIcons(child);
                                break;
                        }
                    }

                    return new SiteVariables(
                        fullPath,
                        blogTitle,
                        signature,
                        aboutBody,
                        aboutBodyHtml,
                        wordFrequencyFilter,
                        wordFrequencyForce,
                        unpublishIcons);
                }
                finally
                {
                    _activeStyleFormats = null;
                }
            }
            catch
            {
                // 跳过无法解析的文件，继续扫描
            }
        }

        return null;
    }

    private static string? ExtractVariablePlainText(XElement keyNode)
    {
        var parts = keyNode.Elements(NodeName)
            .Select(n => ParseNodeContent(n)?.PlainText)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        if (parts.Count > 0)
            return string.Join("\n", parts);

        return ParseNodeContent(keyNode)?.PlainText;
    }

    /// <summary>从「不发布的图标」子树收集各节点上的 icon BUILTIN 值。</summary>
    private static List<string> ExtractUnpublishIcons(XElement sectionNode)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectIconBuiltins(sectionNode, set);
        foreach (var n in sectionNode.Descendants(NodeName))
        {
            if (ReferenceEquals(n, sectionNode))
                continue;
            CollectIconBuiltins(n, set);
        }

        return set.Count > 0 ? set.ToList() : [];
    }

    private static void CollectIconBuiltins(XElement node, HashSet<string> set)
    {
        var iconAttr = node.Attribute("ICON_BUILTIN")?.Value
            ?? node.Attribute("ICON")?.Value;
        if (!string.IsNullOrWhiteSpace(iconAttr))
            set.Add(iconAttr.Trim());

        foreach (var icon in node.Elements(IconName))
        {
            var b = icon.Attribute("BUILTIN")?.Value;
            if (!string.IsNullOrWhiteSpace(b))
                set.Add(b.Trim());
        }
    }

    private static IReadOnlyDictionary<string, string> ParseStyleFormats(XElement map)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stylenode in map.Descendants().Where(e => e.Name.LocalName == "stylenode"))
        {
            var format = stylenode.Attribute("FORMAT")?.Value;
            if (string.IsNullOrWhiteSpace(format))
                continue;

            var name = stylenode.Attribute("LOCALIZED_TEXT")?.Value
                ?? stylenode.Attribute("NAME")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
                dict[name.Trim()] = format.Trim();
        }

        return dict;
    }

    private static bool IsMarkdownNode(XElement node)
    {
        if (MarkdownRenderer.IsMarkdownFormatValue(node.Attribute("FORMAT")?.Value))
            return true;

        var styleRef = node.Attribute("LOCALIZED_STYLE_REF")?.Value
            ?? node.Attribute("STYLE_REF")?.Value;
        return styleRef != null
            && _activeStyleFormats != null
            && _activeStyleFormats.TryGetValue(styleRef, out var styleFormat)
            && MarkdownRenderer.IsMarkdownFormatValue(styleFormat);
    }

    private sealed record ParsedNodeContent(string PlainText, string Html, bool IsRichHtml, bool IsMarkdown = false);

    private static ParsedNodeContent? ParseNodeContent(XElement node)
    {
        var rawText = DecodeText(node.Attribute("TEXT")?.Value)?.Trim();
        var nodeRc = node.Elements(RichContentName)
            .FirstOrDefault(rc => string.Equals(rc.Attribute("TYPE")?.Value, "NODE", StringComparison.OrdinalIgnoreCase));

        var sourceText = nodeRc != null
            ? ExtractRichContentSourceText(nodeRc)
            : rawText;
        if (string.IsNullOrWhiteSpace(sourceText) && !string.IsNullOrWhiteSpace(rawText))
            sourceText = rawText;
        if (string.IsNullOrWhiteSpace(sourceText))
            return null;

        if (MarkdownRenderer.ShouldRenderAsMarkdown(sourceText, IsMarkdownNode(node)))
        {
            var mdHtml = MarkdownRenderer.ToHtml(sourceText);
            var mdPlain = MarkdownRenderer.ToPlainText(sourceText);
            return new ParsedNodeContent(mdPlain, mdHtml, IsRichHtml: true, IsMarkdown: true);
        }

        if (nodeRc == null)
            return new ParsedNodeContent(sourceText, WebUtility.HtmlEncode(sourceText), IsRichHtml: false);

        var html = ExtractRichContentHtml(nodeRc);
        if (string.IsNullOrWhiteSpace(html))
            html = WebUtility.HtmlEncode(sourceText);

        var hasRich = html.Contains("style=", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<font", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<span", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<b", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<i", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<h", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<ul", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<ol", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<table", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<blockquote", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<pre", StringComparison.OrdinalIgnoreCase);
        return new ParsedNodeContent(sourceText, html, hasRich);
    }

    private static string ExtractRichContentSourceText(XElement richContent)
    {
        var body = richContent.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase));
        if (body == null)
            return DecodeText(richContent.Value)?.Trim() ?? "";

        var parts = new List<string>();
        foreach (var child in body.Nodes())
        {
            switch (child)
            {
                case XElement el when el.Name.LocalName.Equals("p", StringComparison.OrdinalIgnoreCase):
                {
                    var line = ExtractElementInnerSource(el);
                    if (!string.IsNullOrWhiteSpace(line))
                        parts.Add(line);
                    break;
                }
                case XElement el when el.Name.LocalName.Equals("pre", StringComparison.OrdinalIgnoreCase):
                    parts.Add("```\n" + el.Value.Trim() + "\n```");
                    break;
                case XElement el:
                {
                    var line = ExtractElementInnerSource(el);
                    if (!string.IsNullOrWhiteSpace(line))
                        parts.Add(line);
                    break;
                }
                case XText t when !string.IsNullOrWhiteSpace(t.Value):
                    parts.Add(t.Value.Trim());
                    break;
            }
        }

        return string.Join("\n\n", parts).Trim();
    }

    private static string ExtractElementInnerSource(XElement el)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var node in el.Nodes())
        {
            switch (node)
            {
                case XText t:
                    sb.Append(WebUtility.HtmlDecode(t.Value));
                    break;
                case XElement child when child.Name.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase):
                    sb.Append('\n');
                    break;
                default:
                    sb.Append(WebUtility.HtmlDecode(node.ToString(SaveOptions.DisableFormatting)));
                    break;
            }
        }

        return InlineBrRegex.Replace(sb.ToString(), "\n").Trim();
    }

    /// <summary>从笔记本根到文章根父级，用 “ / ” 连接各层节点标题，作导航分区与无 # 时的书签回退。</summary>
    private static string BuildStructuralPath(XElement notebookRoot, XElement articleRoot)
    {
        var parts = new List<string>();
        for (var p = articleRoot.Parent; p != null && !ReferenceEquals(p, notebookRoot); p = p.Parent)
        {
            if (p.Name != NodeName)
                continue;
            var t = DecodeText(p.Attribute("TEXT")?.Value);
            if (!string.IsNullOrWhiteSpace(t))
                parts.Insert(0, t.Trim());
        }

        return parts.Count > 0 ? string.Join(" / ", parts) : "未分区";
    }

    /// <summary>节点上包含「不发布」图标（由「变量」→「不发布的图标」配置，默认 closed）。</summary>
    public static bool HasUnpublishIcon(XElement node)
    {
        var iconAttr = node.Attribute("ICON_BUILTIN")?.Value
            ?? node.Attribute("ICON")?.Value;
        if (!string.IsNullOrEmpty(iconAttr) && UnpublishIconSet.Contains(iconAttr))
            return true;

        foreach (var icon in node.Elements(IconName))
        {
            var b = icon.Attribute("BUILTIN")?.Value;
            if (!string.IsNullOrEmpty(b) && UnpublishIconSet.Contains(b))
                return true;
        }

        return false;
    }

    /// <summary>节点自身、任一祖先或图册根节点带「不发布」图标。</summary>
    private static bool IsWithinUnpublishSubtree(XElement node, XElement notebookRoot)
    {
        for (var p = node; p != null; p = p.Parent)
        {
            if (p.Name != NodeName)
                continue;
            if (HasUnpublishIcon(p))
                return true;
            if (ReferenceEquals(p, notebookRoot))
                break;
        }

        return false;
    }

    /// <summary>节点上包含互联网图标（FreeMind：icon BUILTIN=internet）。</summary>
    public static bool HasInternetPublishIcon(XElement node)
    {
        var iconAttr = node.Attribute("ICON_BUILTIN")?.Value
            ?? node.Attribute("ICON")?.Value;
        if (!string.IsNullOrEmpty(iconAttr)
            && (string.Equals(iconAttr, "internet", StringComparison.OrdinalIgnoreCase)
                || iconAttr.EndsWith("internet", StringComparison.OrdinalIgnoreCase)))
            return true;

        foreach (var icon in node.Elements(IconName))
        {
            var b = icon.Attribute("BUILTIN")?.Value;
            if (string.Equals(b, "internet", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>从节点明细 DETAILS（及 NOTE）中的 HTML 文本解析 #书签；无 # 时用图册根标题（非路径）。</summary>
    private static IReadOnlyList<string> ExtractBookmarks(XElement articleRoot, string notebookRootTitleFallback)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rc in articleRoot.Elements(RichContentName))
        {
            var type = rc.Attribute("TYPE")?.Value ?? "";
            if (!type.Equals("DETAILS", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("NOTE", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = rc.Value;
            foreach (Match m in BookmarkHashRegex.Matches(text))
            {
                var raw = m.Groups[1].Value.Trim();
                var tag = DecodeText(raw);
                if (!string.IsNullOrWhiteSpace(tag))
                    set.Add(tag);
            }
        }

        if (set.Count == 0 && !string.IsNullOrWhiteSpace(notebookRootTitleFallback))
            set.Add(notebookRootTitleFallback.Trim());

        return set.Count > 0 ? set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string> { "未分类" };
    }

    /// <summary>读取 Docear 时间管理等插件写入的 <c>REMINDUSERAT</c>（节点下 <c>hook</c> → <c>Parameters</c>）。</summary>
    private static DateTimeOffset? ExtractReminderAt(XElement articleRoot)
    {
        foreach (var hook in articleRoot.Elements(HookName))
        {
            foreach (var el in hook.DescendantsAndSelf())
            {
                var ra = el.Attribute("REMINDUSERAT")?.Value;
                var parsed = ParseMindTime(ra);
                if (parsed.HasValue)
                    return parsed;
            }
        }

        return null;
    }

    private static readonly Regex BookmarkHashRegex = new(@"#([^\s#]+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static List<BodyBlock> BuildBodyBlocks(XElement articleRoot, string mmDirectory)
    {
        var list = new List<BodyBlock>();
        foreach (var child in articleRoot.Elements(NodeName))
            AppendNodeBlocks(child, mmDirectory, list, depth: 1);
        return list;
    }

    private static void AppendNodeBlocks(XElement node, string mmDirectory, List<BodyBlock> list, int depth)
    {
        if (HasUnpublishIcon(node))
            return;

        if (TryAppendDateSubtree(node, mmDirectory, list, depth))
            return;

        AppendNodeContent(node, mmDirectory, list, depth);

        foreach (var child in node.Elements(NodeName))
            AppendNodeBlocks(child, mmDirectory, list, depth + 1);
    }

    /// <summary>若节点为年/月/日结构则整段处理并返回 true。</summary>
    private static bool TryAppendDateSubtree(XElement node, string mmDirectory, List<BodyBlock> list, int depth)
    {
        var label = GetNodeLabel(node);
        if (!TryParseYear(label, out var year))
            return false;

        var children = node.Elements(NodeName).ToList();
        if (children.Count == 0)
        {
            AddDateLine(list, depth, year, null, null);
            return true;
        }

        if (children.All(c => TryParseMonth(GetNodeLabel(c), out _)))
        {
            foreach (var monthNode in children)
            {
                TryParseMonth(GetNodeLabel(monthNode), out var month);
                AppendMonthBranch(year, month, monthNode, mmDirectory, list, depth);
            }

            return true;
        }

        if (children.Count == 1)
            return TryAppendYearMonthDayChain(node, mmDirectory, list, depth, year, children[0]);

        // 年节点下混有月份与非月份子节点：输出年份标题，其余按正常层级继续
        AddDateLine(list, depth, year, null, null);
        foreach (var child in children)
            AppendNodeBlocks(child, mmDirectory, list, depth + 1);
        return true;
    }

    private static bool TryAppendYearMonthDayChain(
        XElement yearNode,
        string mmDirectory,
        List<BodyBlock> list,
        int depth,
        int year,
        XElement monthNode)
    {
        if (!TryParseMonth(GetNodeLabel(monthNode), out var month))
            return false;

        var dayChildren = monthNode.Elements(NodeName).ToList();
        if (dayChildren.Count == 0)
        {
            AddDateLine(list, depth, year, month, null);
            return true;
        }

        if (dayChildren.Count == 1 && TryParseDay(GetNodeLabel(dayChildren[0]), out var singleDay))
        {
            AddDateLine(list, depth, year, month, singleDay);
            foreach (var contentChild in dayChildren[0].Elements(NodeName))
                AppendNodeBlocks(contentChild, mmDirectory, list, depth + 1);
            return true;
        }

        if (dayChildren.All(c => TryParseDay(GetNodeLabel(c), out _)))
        {
            foreach (var dayNode in dayChildren)
            {
                TryParseDay(GetNodeLabel(dayNode), out var dayNum);
                AddDateLine(list, depth, year, month, dayNum);
                foreach (var contentChild in dayNode.Elements(NodeName))
                    AppendNodeBlocks(contentChild, mmDirectory, list, depth + 1);
            }

            return true;
        }

        AddDateLine(list, depth, year, month, null);
        foreach (var child in dayChildren)
            AppendNodeBlocks(child, mmDirectory, list, depth + 1);
        return true;
    }

    private static void AppendMonthBranch(
        int year,
        int month,
        XElement monthNode,
        string mmDirectory,
        List<BodyBlock> list,
        int depth)
    {
        var children = monthNode.Elements(NodeName).ToList();
        if (children.Count == 0)
        {
            AddDateLine(list, depth, year, month, null);
            return;
        }

        if (children.Count == 1 && TryParseDay(GetNodeLabel(children[0]), out var singleDay))
        {
            AddDateLine(list, depth, year, month, singleDay);
            foreach (var contentChild in children[0].Elements(NodeName))
                AppendNodeBlocks(contentChild, mmDirectory, list, depth + 1);
            return;
        }

        if (children.All(c => TryParseDay(GetNodeLabel(c), out _)))
        {
            foreach (var dayNode in children)
            {
                TryParseDay(GetNodeLabel(dayNode), out var dayNum);
                AddDateLine(list, depth, year, month, dayNum);
                foreach (var contentChild in dayNode.Elements(NodeName))
                    AppendNodeBlocks(contentChild, mmDirectory, list, depth + 1);
            }

            return;
        }

        AddDateLine(list, depth, year, month, null);
        foreach (var child in children)
            AppendNodeBlocks(child, mmDirectory, list, depth + 1);
    }

    private static void AddDateLine(List<BodyBlock> list, int depth, int year, int? month, int? day)
    {
        list.Add(new ParagraphBlock(FormatMindDate(year, month, day), depth, IsDateLine: true));
    }

    internal static string FormatMindDate(int year, int? month, int? day)
    {
        if (month is null or < 1)
            return $"{year}年";
        if (day is null or < 1)
            return $"{year}年{month}月";
        return $"{year}年{month}月{day}日";
    }

    /// <summary>
    /// 文章标题：若根节点为「主题 → 年 → 月 → 日」中的纯日节点，
    /// 则合成「{年}年{月}月{日}日{主题去编号}」；否则用节点原文。
    /// </summary>
    private static string ResolveArticleTitle(XElement articleRoot)
    {
        var raw = GetNodeLabel(articleRoot);
        if (string.IsNullOrEmpty(raw))
            return "无标题";

        if (!TryParseDay(raw, out var day))
            return raw;

        var monthNode = articleRoot.Parent;
        if (monthNode == null || monthNode.Name != NodeName)
            return raw;
        if (!TryParseMonth(GetNodeLabel(monthNode), out var month))
            return raw;

        var yearNode = monthNode.Parent;
        if (yearNode == null || yearNode.Name != NodeName)
            return raw;
        if (!TryParseYear(GetNodeLabel(yearNode), out var year))
            return raw;

        var topicNode = yearNode.Parent;
        if (topicNode == null || topicNode.Name != NodeName)
            return raw;

        var topic = GetNodeLabel(topicNode);
        if (string.IsNullOrWhiteSpace(topic))
            return raw;
        // 主题本身不能是年/月/日数字（导图根也可以是主题，如根节点「01每日复盘」）
        if (TryParseYear(topic, out _) || TryParseMonth(topic, out _) || TryParseDay(topic, out _))
            return raw;

        var suffix = StripLeadingNumber(topic);
        return FormatMindDate(year, month, day) + suffix;
    }

    /// <summary>去掉开头的序号前缀（如 01、1.、①），保留后面正文；去掉后为空则返回原文。</summary>
    internal static string StripLeadingNumber(string topic)
    {
        var t = topic.Trim();
        if (t.Length == 0)
            return t;

        // ①–⑳、⑴–⒇、㈠–㈩ 等带圈/括号数字
        var circled = new Regex(
            @"^[\u2460-\u2473\u2474-\u2487\u3220-\u3229\u3251-\u325F\u32B1-\u32BF]+",
            RegexOptions.Compiled);
        // 01 / 1. / A) 等，编号与正文之间可无分隔符（如 01每日复盘）
        var ascii = new Regex(
            @"^(?:\d{1,3}|[A-Za-z])[.\u3001\uFF0E)）\]】\-—_\s]*",
            RegexOptions.Compiled);

        var m = circled.Match(t);
        if (!m.Success)
            m = ascii.Match(t);
        if (!m.Success)
            return t;

        var stripped = t[m.Length..].Trim();
        return stripped.Length > 0 ? stripped : t;
    }

    private static string GetNodeLabel(XElement node) => DecodeText(node.Attribute("TEXT")?.Value)?.Trim() ?? "";

    private static bool IsSiteVariablesNode(XElement node) =>
        string.Equals(GetNodeLabel(node), SiteVariablesNodeLabel, StringComparison.Ordinal);

    private static bool TryParseYear(string text, out int year)
    {
        year = 0;
        if (!Regex.IsMatch(text, @"^\d{4}$"))
            return false;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out year))
            return false;
        return year is >= 1900 and <= 2100;
    }

    private static bool TryParseMonth(string text, out int month)
    {
        month = 0;
        if (!Regex.IsMatch(text, @"^\d{1,2}$"))
            return false;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out month))
            return false;
        return month is >= 1 and <= 12;
    }

    private static bool TryParseDay(string text, out int day)
    {
        day = 0;
        if (!Regex.IsMatch(text, @"^\d{1,2}$"))
            return false;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out day))
            return false;
        return day is >= 1 and <= 31;
    }

    private static void AppendNodeContent(XElement node, string mmDirectory, List<BodyBlock> list, int depth)
    {
        var nodeText = ExtractNodeText(node);
        var note = ExtractNodeNote(node);
        var hasLongNote = note != null && (CountSentences(note.PlainText) > 2 || note.PlainText.Contains('\n'));
        var hook = node.Elements(HookName)
            .FirstOrDefault(h => string.Equals(h.Attribute("NAME")?.Value, "ExternalObject", StringComparison.OrdinalIgnoreCase));

        if (hook != null)
        {
            var uri = hook.Attribute("URI")?.Value ?? "";
            var alt = nodeText?.PlainText ?? "";
            var resolved = ResolveUri(mmDirectory, uri);
            list.Add(new ImageBlock(uri, alt, resolved, depth));
            return;
        }

        if (nodeText != null)
            AppendNoteAwareTextBlocks(list, nodeText, note, hasLongNote, hasImage: false, depth);
        else if (note != null)
            AppendNoteAwareTextBlocks(list, null, note, hasLongNote, hasImage: false, depth);
    }

    private static void AppendNoteAwareTextBlocks(
        List<BodyBlock> list,
        NodeText? nodeText,
        NodeNote? note,
        bool hasLongNote,
        bool hasImage,
        int depth)
    {
        if (hasImage)
            return;
        if (note == null || string.IsNullOrWhiteSpace(note.PlainText))
        {
            if (nodeText != null)
                AddNodeTextBlock(list, nodeText, depth);
            return;
        }

        if (!hasLongNote)
        {
            var inlineHtml = NormalizeInlineHtml(note.Html);
            if (nodeText != null)
                AddNodeTextBlock(
                    list,
                    nodeText,
                    depth,
                    plainSuffix: $"（{note.PlainText.Trim()}）",
                    htmlSuffix: $"（<span class=\"note-inline\">{inlineHtml}</span>）");
            else
                list.Add(new NoteBlock(note.PlainText.Trim(), inlineHtml, Inline: true, PrefixText: null, depth));
            return;
        }

        if (nodeText != null)
            AddNodeTextBlock(list, nodeText, depth);
        list.Add(new NoteBlock(note.PlainText.Trim(), note.Html, Inline: !hasLongNote, PrefixText: null, depth));
    }

    private static NodeNote? ExtractNodeNote(XElement node)
    {
        foreach (var rc in node.Elements(RichContentName))
        {
            var type = rc.Attribute("TYPE")?.Value ?? "";
            if (!type.Equals("NOTE", StringComparison.OrdinalIgnoreCase))
                continue;

            if (MarkdownRenderer.ShouldRenderAsMarkdown(ExtractRichContentSourceText(rc), IsMarkdownNode(node)))
            {
                var source = ExtractRichContentSourceText(rc);
                if (string.IsNullOrWhiteSpace(source))
                    continue;

                var mdHtml = MarkdownRenderer.ToHtml(source);
                var mdPlain = MarkdownRenderer.ToPlainText(source);
                return new NodeNote(mdPlain, mdHtml, HasRichStyle: true);
            }

            var plain = DecodeText(rc.Value)?.Trim();
            if (string.IsNullOrWhiteSpace(plain))
                continue;

            var htmlFromNote = ExtractRichContentHtml(rc);
            if (string.IsNullOrWhiteSpace(htmlFromNote))
                htmlFromNote = WebUtility.HtmlEncode(plain);
            return new NodeNote(
                plain,
                htmlFromNote,
                HasRichStyle: HasVisualStyle(htmlFromNote));
        }

        return null;
    }

    private static int CountSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        var parts = SentenceCountRegex.Split(text)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (parts.Count == 0)
            return 0;
        return parts.Count;
    }

    private static string AppendInlineNote(string mainText, string? shortNote)
    {
        if (string.IsNullOrWhiteSpace(shortNote))
            return mainText;
        var note = shortNote.Trim();
        return $"{mainText}（{note}）";
    }

    private static string ExtractRichContentHtml(XElement richContent)
    {
        var htmlRoot = richContent.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase))
            ?? richContent;
        var raw = string.Concat(
            htmlRoot.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        if (string.IsNullOrWhiteSpace(raw))
            raw = WebUtility.HtmlEncode(DecodeText(richContent.Value) ?? "");
        return ScriptTagRegex.Replace(raw, "").Trim();
    }

    private static NodeText? ExtractNodeText(XElement node)
    {
        var parsed = ParseNodeContent(node);
        return parsed == null
            ? null
            : new NodeText(parsed.PlainText, parsed.Html, parsed.IsRichHtml, parsed.IsMarkdown);
    }

    private static bool HasVisualStyle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return false;
        return VisualStyleRegex.IsMatch(html);
    }

    private static string NormalizeInlineHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        var s = html;
        s = InlineBrRegex.Replace(s, " ");
        s = InlineBlockOpenRegex.Replace(s, "");
        s = InlineBlockCloseRegex.Replace(s, " ");
        s = MultiWhitespaceRegex.Replace(s, " ");
        return s.Trim();
    }

    private static void AddNodeTextBlock(
        List<BodyBlock> list,
        NodeText text,
        int depth,
        string plainSuffix = "",
        string? htmlSuffix = null)
    {
        if (text.HasRichStyle)
        {
            string html;
            if (!string.IsNullOrEmpty(htmlSuffix))
            {
                // 短注释内联时，将正文富文本扁平为行内结构后再拼接，避免括号落到块级标签外导致换行。
                var inlineMain = NormalizeInlineHtml(text.Html);
                html = $"<span>{inlineMain}{htmlSuffix}</span>";
            }
            else
            {
                html = text.Html;
                if (!string.IsNullOrEmpty(plainSuffix))
                    html += WebUtility.HtmlEncode(plainSuffix);
            }
            list.Add(new RichParagraphBlock(text.PlainText + plainSuffix, html, depth, IsMarkdown: text.IsMarkdown));
            return;
        }

        list.Add(new ParagraphBlock(text.PlainText + plainSuffix, depth));
    }

    private sealed record NodeNote(string PlainText, string Html, bool HasRichStyle);
    private sealed record NodeText(string PlainText, string Html, bool HasRichStyle, bool IsMarkdown = false);

    internal static string? DecodeText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;
        return WebUtility.HtmlDecode(raw);
    }

    internal static DateTimeOffset? ParseMindTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            return null;
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    internal static string ResolveUri(string mmDirectory, string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return "";
        var trimmed = uri.Trim();
        if (trimmed.StartsWith(".images/", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith(".images\\", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(mmDirectory, trimmed));
        if (Path.IsPathRooted(trimmed))
            return trimmed;
        return Path.GetFullPath(Path.Combine(mmDirectory, trimmed));
    }
}
