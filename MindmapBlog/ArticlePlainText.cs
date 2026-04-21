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

    internal static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
    }
}
