using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MindmapBlog;

/// <summary>
/// 解析 FreeMind / Docear 的 .mm。
/// 在图册根节点之下的<strong>任意深度</strong>查找带互联网图标（BUILTIN=internet）的节点作为文章根；
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

    public static IReadOnlyList<BlogArticle> ExtractArticles(string mmFilePath)
    {
        var fullPath = Path.GetFullPath(mmFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("找不到 .mm 文件。", fullPath);

        var doc = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
        var map = doc.Root ?? throw new InvalidOperationException("无效的 .mm：缺少根元素。");
        if (map.Name != MapName)
            throw new InvalidOperationException("无效的 .mm：根元素应为 map。");

        var notebookRoot = map.Elements(NodeName).FirstOrDefault()
            ?? throw new InvalidOperationException("无效的 .mm：map 下缺少根 node。");

        var notebookTitle = DecodeText(notebookRoot.Attribute("TEXT")?.Value) ?? "(未命名)";
        var mmDir = Path.GetDirectoryName(fullPath) ?? "";

        var articles = new List<BlogArticle>();

        foreach (var articleRoot in notebookRoot.Descendants(NodeName))
        {
            if (ReferenceEquals(articleRoot, notebookRoot))
                continue;
            if (!HasInternetPublishIcon(articleRoot))
                continue;

            var structuralSection = BuildStructuralPath(notebookRoot, articleRoot);
            var title = DecodeText(articleRoot.Attribute("TEXT")?.Value) ?? "无标题";
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

        return articles;
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
            AppendNodeBlocks(child, mmDirectory, list);
        return list;
    }

    private static void AppendNodeBlocks(XElement node, string mmDirectory, List<BodyBlock> list)
    {
        var selfText = DecodeText(node.Attribute("TEXT")?.Value);
        var noteText = ExtractNodeNoteText(node);
        var hasLongNote = !string.IsNullOrWhiteSpace(noteText) && CountSentences(noteText) > 2;
        var shortNote = !hasLongNote ? noteText : null;
        var hook = node.Elements(HookName)
            .FirstOrDefault(h => string.Equals(h.Attribute("NAME")?.Value, "ExternalObject", StringComparison.OrdinalIgnoreCase));

        var hasImage = false;
        if (hook != null)
        {
            var uri = hook.Attribute("URI")?.Value ?? "";
            var alt = selfText ?? "";
            var resolved = ResolveUri(mmDirectory, uri);
            list.Add(new ImageBlock(uri, alt, resolved));
            hasImage = true;
        }

        var childNodes = node.Elements(NodeName).ToList();
        if (childNodes.Count == 0)
        {
            if (!hasImage && !string.IsNullOrWhiteSpace(selfText))
                list.Add(new ParagraphBlock(AppendInlineNote(selfText.Trim(), shortNote)));
            else if (!hasImage && string.IsNullOrWhiteSpace(selfText) && !string.IsNullOrWhiteSpace(shortNote))
                list.Add(new ParagraphBlock($"（{shortNote!.Trim()}）"));
            if (hasLongNote)
                list.Add(new NoteBoxBlock(noteText!.Trim()));
            return;
        }

        if (!hasImage && !string.IsNullOrWhiteSpace(selfText))
            list.Add(new ParagraphBlock(AppendInlineNote(selfText.Trim(), shortNote)));
        else if (!hasImage && string.IsNullOrWhiteSpace(selfText) && !string.IsNullOrWhiteSpace(shortNote))
            list.Add(new ParagraphBlock($"（{shortNote!.Trim()}）"));

        foreach (var child in childNodes)
            AppendNodeBlocks(child, mmDirectory, list);

        if (hasLongNote)
            list.Add(new NoteBoxBlock(noteText!.Trim()));
    }

    private static string? ExtractNodeNoteText(XElement node)
    {
        foreach (var rc in node.Elements(RichContentName))
        {
            var type = rc.Attribute("TYPE")?.Value ?? "";
            if (!type.Equals("NOTE", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("DETAILS", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = rc.Value?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            return text;
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
