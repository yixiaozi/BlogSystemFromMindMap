using System.Net;
using System.Text;
using DiffPlex;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace MindmapBlog;

internal static class TextDiffHelper
{
    /// <summary>基于 DiffPlex 字符级 diff 统计增删字数。</summary>
    public static (int Added, int Removed, int ModifiedEstimate) ComputeCharStats(string? oldText, string newText)
    {
        oldText ??= "";
        newText ??= "";

        var differ = Differ.Instance;
        var diff = differ.CreateCharacterDiffs(oldText, newText, ignoreWhitespace: false);

        var removed = 0;
        var added = 0;
        foreach (var block in diff.DiffBlocks)
        {
            removed += block.DeleteCountA;
            added += block.InsertCountB;
        }

        var modifiedEst = Math.Min(added, removed);
        return (added, removed, modifiedEst);
    }

    /// <summary>生成可嵌入页面的内联差异 HTML（插入/删除着色）。</summary>
    public static string BuildInlineDiffHtml(string? oldText, string newText)
    {
        oldText ??= "";
        newText ??= "";

        var differ = Differ.Instance;
        var model = InlineDiffBuilder.Diff(differ, oldText, newText, ignoreWhiteSpace: false,
            ignoreCase: false, CharacterChunker.Instance);

        var sb = new StringBuilder();
        sb.Append("<div class=\"diff-inline\" role=\"region\" aria-label=\"文本差异\">");
        foreach (var line in model.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    sb.Append("<ins class=\"diff-ins\">").Append(WebUtility.HtmlEncode(line.Text)).Append("</ins>");
                    break;
                case ChangeType.Deleted:
                    sb.Append("<del class=\"diff-del\">").Append(WebUtility.HtmlEncode(line.Text)).Append("</del>");
                    break;
                default:
                    sb.Append("<span class=\"diff-same\">").Append(WebUtility.HtmlEncode(line.Text)).Append("</span>");
                    break;
            }
        }

        sb.Append("</div>");
        return sb.ToString();
    }
}
