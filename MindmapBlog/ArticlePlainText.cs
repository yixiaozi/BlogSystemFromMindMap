using System.Text;

namespace MindmapBlog;

/// <summary>用于版本对比的纯文本快照（段落 + 图片占位，与展示顺序一致）。</summary>
internal static class ArticlePlainText
{
    public static string Build(BlogArticle article)
    {
        var sb = new StringBuilder();
        foreach (var block in article.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    sb.AppendLine(p.Text);
                    break;
                case RichParagraphBlock rp:
                    sb.AppendLine(rp.PlainText);
                    break;
                case NoteBlock n:
                    sb.AppendLine("[注释]");
                    sb.AppendLine(n.PlainText);
                    break;
                case ImageBlock img:
                    sb.Append("[图片: ");
                    sb.Append(string.IsNullOrEmpty(img.AltText) ? img.RelativeUri : img.AltText);
                    sb.Append(" | ");
                    sb.Append(img.RelativeUri);
                    sb.AppendLine("]");
                    break;
            }
        }

        var raw = sb.ToString().TrimEnd();
        return Normalize(raw);
    }

    /// <summary>时间轴卡片摘要：仅含超过 <paramref name="minChars"/> 字的段落，至少 <paramref name="minLines"/> 行，最多 <paramref name="maxChars"/> 字。</summary>
    public static string BuildTimelineExcerpt(BlogArticle article, int minLines = 3, int maxChars = 280, int minChars = 10)
    {
        var lines = new List<string>();
        var total = 0;
        foreach (var block in article.Blocks)
        {
            var text = block switch
            {
                ParagraphBlock p => CollapseInline(p.Text),
                RichParagraphBlock rp => CollapseInline(rp.PlainText),
                _ => null,
            };
            if (string.IsNullOrWhiteSpace(text) || text.Length <= minChars)
                continue;

            lines.Add(text);
            total += text.Length;
            if (lines.Count >= minLines)
                break;
            if (total >= maxChars)
                break;
        }

        if (lines.Count == 0)
            return "";

        var excerpt = string.Join('\n', lines);
        if (excerpt.Length <= maxChars)
            return excerpt;
        return excerpt[..maxChars].TrimEnd() + "…";
    }

    private static string CollapseInline(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
    }
}
