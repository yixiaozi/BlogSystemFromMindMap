using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace MindmapBlog;

internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Regex ScriptTagRegex = new(
        @"<script\b[\s\S]*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiWhitespaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    public static bool IsMarkdownFormatValue(string? format) =>
        !string.IsNullOrWhiteSpace(format)
        && format.Contains("markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>首行（忽略空行）以 <c>#</c> 开头则视为 Markdown 源码。</summary>
    public static bool LooksLikeMarkdown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
                continue;
            return trimmed.StartsWith('#');
        }

        return false;
    }

    public static bool ShouldRenderAsMarkdown(string? text, bool formatIsMarkdown = false) =>
        formatIsMarkdown || LooksLikeMarkdown(text);

    public static string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var html = Markdown.ToHtml(markdown.Trim(), Pipeline);
        return ScriptTagRegex.Replace(html, "").Trim();
    }

    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        var html = ToHtml(markdown);
        return HtmlToPlain(html);
    }

    public static string HtmlToPlain(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        var noScript = ScriptTagRegex.Replace(html, "");
        var noTags = Regex.Replace(noScript, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(noTags) ?? noTags;
        return MultiWhitespaceRegex.Replace(decoded, " ").Trim();
    }
}
