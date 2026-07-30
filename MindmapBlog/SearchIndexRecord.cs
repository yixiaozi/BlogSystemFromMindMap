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
                .Where(b => b is ParagraphBlock or RichParagraphBlock or NoteBlock)
                .Select(b => b switch
                {
                    ParagraphBlock p => p.Text.Trim(),
                    RichParagraphBlock rp => rp.PlainText.Trim(),
                    NoteBlock n => n.PlainText.Trim(),
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

    public static SearchIndexRecord FromDocument(WordFrequencyDocument doc)
    {
        var lines = doc.PlainText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var bodyLines = lines.Length > 0 && lines[0] == doc.Title
            ? lines.Skip(1).ToList()
            : lines.ToList();
        var body = string.Join("\n", bodyLines);

        return new SearchIndexRecord
        {
            Href = doc.HtmlFileName.Replace('\\', '/'),
            Title = doc.Title,
            Body = body,
            All = doc.PlainText,
        };
    }

    public static List<SearchIndexRecord> Build(
        IReadOnlyList<BlogArticle> articles,
        GitCommitHistorySnapshot? gitCommits = null,
        string? aboutPageWebPath = null)
    {
        var list = articles.Select(FromArticle).ToList();

        var about = WordFrequencyService.FromAboutPage(aboutPageWebPath);
        if (about != null)
            list.Add(FromDocument(about));

        foreach (var commitDoc in WordFrequencyService.FromGitCommitEntries(gitCommits))
            list.Add(FromDocument(commitDoc));

        return list;
    }
}
