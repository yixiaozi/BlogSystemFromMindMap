namespace MindmapBlog;

/// <summary>供客户端全文搜索加载的索引项（序列化为 camelCase JSON）。</summary>
internal sealed class SearchIndexRecord
{
    public string Href { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public List<string> Bookmarks { get; init; } = new();
    public List<string> ImageAlts { get; init; } = new();
    public string Section { get; init; } = "";
    public string Notebook { get; init; } = "";
    public string? Reminder { get; init; }
    public string? SourceFile { get; init; }

    /// <summary>预拼接的可检索全文（省去客户端再拼一次）。</summary>
    public string All { get; init; } = "";

    public static SearchIndexRecord FromArticle(BlogArticle article)
    {
        var body = string.Join(
            "\n",
            article.Blocks
                .Where(b => b is ParagraphBlock or NoteBoxBlock)
                .Select(b => b switch
                {
                    ParagraphBlock p => p.Text.Trim(),
                    NoteBoxBlock n => n.Text.Trim(),
                    _ => "",
                })
                .Where(t => t.Length > 0));

        var imageAlts = article.Blocks
            .OfType<ImageBlock>()
            .Select(b => string.IsNullOrWhiteSpace(b.AltText) ? "" : b.AltText.Trim())
            .Where(t => t.Length > 0)
            .ToList();

        var reminder = article.ReminderAt.HasValue
            ? article.ReminderAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : null;

        var sourceFile = Path.GetFileName(article.SourceMmPath);

        var pieces = new List<string>
        {
            article.Title,
            body,
            string.Join(" ", article.Bookmarks),
            string.Join(" ", imageAlts),
            article.StructuralSection,
            article.NotebookTitle,
        };

        if (!string.IsNullOrEmpty(reminder))
            pieces.Add(reminder);
        if (!string.IsNullOrEmpty(sourceFile))
            pieces.Add(sourceFile);

        var all = string.Join("\n", pieces.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new SearchIndexRecord
        {
            Href = article.HtmlFileName.Replace('\\', '/'),
            Title = article.Title,
            Body = body,
            Bookmarks = article.Bookmarks.ToList(),
            ImageAlts = imageAlts,
            Section = article.StructuralSection,
            Notebook = article.NotebookTitle,
            Reminder = reminder,
            SourceFile = sourceFile,
            All = all,
        };
    }
}
