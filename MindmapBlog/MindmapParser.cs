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
    private static readonly Regex ScriptTagRegex = new(@"<script\b[\s\S]*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex VisualStyleRegex = new(
        @"(<font\b)|(<span\b)|color\s*:|font-weight\s*:|font-style\s*:|text-decoration\s*:|background\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineBlockOpenRegex = new(@"<(p|div|section|article|li|ul|ol)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineBlockCloseRegex = new(@"</(p|div|section|article|li|ul|ol)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InlineBrRegex = new(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultiWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

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
        var nodeText = ExtractNodeText(node);
        var note = ExtractNodeNote(node);
        var hasLongNote = note != null && (CountSentences(note.PlainText) > 2 || note.PlainText.Contains('\n'));
        var hook = node.Elements(HookName)
            .FirstOrDefault(h => string.Equals(h.Attribute("NAME")?.Value, "ExternalObject", StringComparison.OrdinalIgnoreCase));

        var hasImage = false;
        if (hook != null)
        {
            var uri = hook.Attribute("URI")?.Value ?? "";
            var alt = nodeText?.PlainText ?? "";
            var resolved = ResolveUri(mmDirectory, uri);
            list.Add(new ImageBlock(uri, alt, resolved));
            hasImage = true;
        }

        var childNodes = node.Elements(NodeName).ToList();
        if (childNodes.Count == 0)
        {
            if (!hasImage && nodeText != null)
                AppendNoteAwareTextBlocks(list, nodeText, note, hasLongNote, hasImage);
            else if (!hasImage && note != null)
                AppendNoteAwareTextBlocks(list, null, note, hasLongNote, hasImage);
            return;
        }

        if (!hasImage && nodeText != null)
            AppendNoteAwareTextBlocks(list, nodeText, note, hasLongNote, hasImage);
        else if (!hasImage && note != null)
            AppendNoteAwareTextBlocks(list, null, note, hasLongNote, hasImage);

        foreach (var child in childNodes)
            AppendNodeBlocks(child, mmDirectory, list);
    }

    private static void AppendNoteAwareTextBlocks(
        List<BodyBlock> list,
        NodeText? nodeText,
        NodeNote? note,
        bool hasLongNote,
        bool hasImage)
    {
        if (hasImage)
            return;
        if (note == null || string.IsNullOrWhiteSpace(note.PlainText))
        {
            if (nodeText != null)
                AddNodeTextBlock(list, nodeText);
            return;
        }

        if (!hasLongNote)
        {
            var inlineHtml = NormalizeInlineHtml(note.Html);
            if (nodeText != null)
                AddNodeTextBlock(
                    list,
                    nodeText,
                    plainSuffix: $"（{note.PlainText.Trim()}）",
                    htmlSuffix: $"（<span class=\"note-inline\">{inlineHtml}</span>）");
            else
                list.Add(new NoteBlock(note.PlainText.Trim(), inlineHtml, Inline: true, PrefixText: null));
            return;
        }

        if (nodeText != null)
            AddNodeTextBlock(list, nodeText);
        list.Add(new NoteBlock(note.PlainText.Trim(), note.Html, Inline: !hasLongNote, PrefixText: null));
    }

    private static NodeNote? ExtractNodeNote(XElement node)
    {
        foreach (var rc in node.Elements(RichContentName))
        {
            var type = rc.Attribute("TYPE")?.Value ?? "";
            if (!type.Equals("NOTE", StringComparison.OrdinalIgnoreCase))
                continue;

            var plain = DecodeText(rc.Value)?.Trim();
            if (string.IsNullOrWhiteSpace(plain))
                continue;

            var html = ExtractRichContentHtml(rc);
            if (string.IsNullOrWhiteSpace(html))
                html = WebUtility.HtmlEncode(plain);
            return new NodeNote(
                plain,
                html,
                HasRichStyle: HasVisualStyle(html));
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
        var rawText = DecodeText(node.Attribute("TEXT")?.Value)?.Trim();
        var nodeRc = node.Elements(RichContentName)
            .FirstOrDefault(rc => string.Equals(rc.Attribute("TYPE")?.Value, "NODE", StringComparison.OrdinalIgnoreCase));
        if (nodeRc == null)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return null;
            return new NodeText(rawText, WebUtility.HtmlEncode(rawText), HasRichStyle: false);
        }

        var plain = DecodeText(nodeRc.Value)?.Trim();
        if (string.IsNullOrWhiteSpace(plain))
            plain = rawText;
        if (string.IsNullOrWhiteSpace(plain))
            return null;
        var html = ExtractRichContentHtml(nodeRc);
        if (string.IsNullOrWhiteSpace(html))
            html = WebUtility.HtmlEncode(plain);
        var hasRich = html.Contains("style=", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<font", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<span", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<b", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<i", StringComparison.OrdinalIgnoreCase);
        return new NodeText(plain, html, hasRich);
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
            list.Add(new RichParagraphBlock(text.PlainText + plainSuffix, html));
            return;
        }

        list.Add(new ParagraphBlock(text.PlainText + plainSuffix));
    }

    private sealed record NodeNote(string PlainText, string Html, bool HasRichStyle);
    private sealed record NodeText(string PlainText, string Html, bool HasRichStyle);

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
