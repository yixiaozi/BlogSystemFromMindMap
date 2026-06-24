namespace MindmapBlog;

/// <summary>
/// 一篇由思维导图节点抽取的文章。
/// 书签来自节点明细（richcontent DETAILS）中的 #标签；分区为父级结构节点文字。
/// </summary>
public sealed class BlogArticle
{
    public required string SourceMmPath { get; init; }
    public required string NotebookTitle { get; init; }

    /// <summary>导图结构上，直接父节点（原「分区/标签节点」）名称，用于左侧路径展示。</summary>
    public required string StructuralSection { get; init; }

    /// <summary>从节点明细 #话题 解析的书签；至少包含一个元素（若无 # 则回退为分区名）。</summary>
    public required IReadOnlyList<string> Bookmarks { get; init; }

    public required string Title { get; init; }
    public required string ArticleNodeId { get; init; }
    public DateTimeOffset Created { get; init; }
    public DateTimeOffset Modified { get; init; }

    /// <summary>
    /// Docear 等客户端在节点上设置的「提醒/计划」时间（<c>REMINDUSERAT</c> 毫秒时间戳），未设提醒则为 <c>null</c>。
    /// </summary>
    public DateTimeOffset? ReminderAt { get; init; }

    public required IReadOnlyList<BodyBlock> Blocks { get; init; }

    /// <summary>相对站点根的路径（正斜杠），与导图所在文件夹结构一致，如 <c>日程/规划/去金海湖.html</c>。</summary>
    public string? PublishWebPath { get; set; }

    /// <summary>发布的 HTML 路径（优先 <see cref="PublishWebPath"/>）。</summary>
    public string HtmlFileName => ArticleIdentity.ResolveHtmlFileName(this);
}

public abstract record BodyBlock(int Depth = 1);

public sealed record ParagraphBlock(string Text, int Depth = 1, bool IsDateLine = false) : BodyBlock(Depth);

public sealed record RichParagraphBlock(string PlainText, string Html, int Depth = 1, bool IsDateLine = false, bool IsMarkdown = false) : BodyBlock(Depth);

public sealed record ImageBlock(string RelativeUri, string AltText, string ResolvedSourcePath, int Depth = 1) : BodyBlock(Depth);

public sealed record NoteBlock(string PlainText, string Html, bool Inline, string? PrefixText, int Depth = 1) : BodyBlock(Depth);
