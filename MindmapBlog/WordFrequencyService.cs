using System.Text;
using System.Text.RegularExpressions;
using JiebaNet.Segmenter;

namespace MindmapBlog;

/// <summary>
/// 词频语料中的一篇「文档」：普通文章，或关于我 / 提交记录等独立页。
/// 以后新增同类单据页时，往语料列表追加即可。
/// </summary>
internal sealed record WordFrequencyDocument(
    string Title,
    string HtmlFileName,
    string PlainText);

internal static class WordFrequencyService
{
    private static readonly Regex LatinLetters = new(@"[a-zA-Z]", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitter = new(@"(?<=[。！？!?；;])\s*|\r?\n+", RegexOptions.Compiled);

    /// <summary>
    /// 从文章与独立页构建词频语料。
    /// 以后新增「关于我 / 提交记录」这类单据页时，在此追加 <see cref="WordFrequencyDocument"/> 即可。
    /// </summary>
    public static List<WordFrequencyDocument> BuildCorpus(
        IReadOnlyList<BlogArticle> articles,
        GitCommitHistorySnapshot? gitCommits = null,
        string? aboutPageWebPath = null)
    {
        var docs = new List<WordFrequencyDocument>(articles.Count + 4);
        foreach (var article in articles)
            docs.Add(FromArticle(article));

        var about = FromAboutPage(aboutPageWebPath);
        if (about != null)
            docs.Add(about);

        var commits = FromGitCommitPage(gitCommits);
        if (commits != null)
            docs.Add(commits);

        return docs;
    }

    public static WordFrequencyDocument FromArticle(BlogArticle article) =>
        new(article.Title, article.HtmlFileName, CollectArticlePlainText(article));

    public static WordFrequencyDocument? FromAboutPage(string? aboutPageWebPath = null)
    {
        var plain = SiteProfile.AboutBody?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(plain) && !string.IsNullOrWhiteSpace(SiteProfile.AboutBodyHtml))
            plain = MarkdownRenderer.HtmlToPlain(SiteProfile.AboutBodyHtml);

        var signature = SiteProfile.Signature?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(plain) && string.IsNullOrWhiteSpace(signature))
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("关于我");
        if (!string.IsNullOrWhiteSpace(signature))
            sb.AppendLine(signature);
        if (!string.IsNullOrWhiteSpace(plain))
            sb.AppendLine(plain);

        var href = string.IsNullOrWhiteSpace(aboutPageWebPath) ? "关于我.html" : aboutPageWebPath.Trim();
        return new WordFrequencyDocument("关于我", href.Replace('\\', '/'), sb.ToString());
    }

    public static WordFrequencyDocument? FromGitCommitPage(GitCommitHistorySnapshot? snapshot)
    {
        if (snapshot == null || snapshot.Commits.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("提交记录");
        foreach (var c in snapshot.Commits)
        {
            if (GitCommitCollector.IsMergeCommitSubject(c.Subject))
                continue;
            if (!string.IsNullOrWhiteSpace(c.Subject))
                sb.AppendLine(c.Subject);
            if (!string.IsNullOrWhiteSpace(c.Body))
                sb.AppendLine(c.Body);
        }

        var text = sb.ToString();
        if (text.Trim().Length <= "提交记录".Length)
            return null;

        return new WordFrequencyDocument(
            "提交记录",
            HtmlLayout.GitCommitHistoryPageFileName,
            text);
    }

    /// <summary>聚合语料文本后的词频（jieba 精确模式分词）。</summary>
    /// <param name="minOccurrences">至少出现多少次才计入结果（默认 3，即排除 2 次及以内）。</param>
    /// <param name="forceInclude">导图「词频强制」词条：始终出现在结果中（不受最低次数限制；与过滤冲突时以强制为准）。</param>
    public static WordFrequencyResult Compute(
        IReadOnlyList<WordFrequencyDocument> documents,
        int maxTerms,
        IReadOnlyCollection<string>? extraStopwords = null,
        int minOccurrences = 3,
        IReadOnlyCollection<string>? forceInclude = null)
    {
        var segmenter = new JiebaSegmenter();
        var stop = LoadStopwords();
        var customFilter = BuildCustomFilterSet(extraStopwords);
        var forced = BuildCustomFilterSet(forceInclude);
        // 强制优先于过滤
        foreach (var f in forced)
            customFilter.Remove(f);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalHits = 0;

        foreach (var doc in documents)
        {
            if (string.IsNullOrWhiteSpace(doc.PlainText))
                continue;

            foreach (var raw in segmenter.Cut(doc.PlainText))
            {
                if (!TryNormalizeToken(raw, stop, out var key))
                {
                    // 强制词即使被停用词/长度规则挡下，也按原文规范化后计数
                    if (!TryNormalizeForcedToken(raw, forced, out key))
                        continue;
                }
                else if (customFilter.Contains(key) && !forced.Contains(key))
                {
                    continue;
                }

                totalHits++;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        // 强制词若 jieba 未切出整词，用子串补计（取较大值）
        foreach (var term in forced)
        {
            var substringCount = CountSubstringOccurrences(documents, term);
            if (substringCount > counts.GetValueOrDefault(term))
            {
                var prev = counts.GetValueOrDefault(term);
                totalHits += substringCount - prev;
                counts[term] = substringCount;
            }
            else if (!counts.ContainsKey(term))
            {
                counts[term] = 0;
            }
        }

        var minCount = Math.Max(1, minOccurrences);
        var eligible = counts
            .Where(kv =>
                forced.Contains(kv.Key)
                || (kv.Value >= minCount && !customFilter.Contains(kv.Key)))
            .ToList();

        var rankedNormal = eligible
            .Where(kv => !forced.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        var rankedForced = forced
            .Select(t => new KeyValuePair<string, int>(t, counts.GetValueOrDefault(t)))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        // 强制词始终保留；其余填满至 maxTerms
        var takeOthers = Math.Max(0, Math.Max(1, maxTerms) - rankedForced.Count);
        var ranked = rankedForced
            .Concat(rankedNormal.Take(takeOthers))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new WordFrequencyItem(kv.Key, kv.Value))
            .ToList();

        var maxRanked = ranked.Count > 0 ? ranked.Max(t => t.Count) : 0;
        var minRanked = ranked.Count > 0 ? ranked.Min(t => t.Count) : 0;

        return new WordFrequencyResult(
            ranked,
            totalHits,
            eligible.Count,
            documents.Count,
            maxRanked,
            minRanked);
    }

    /// <summary>兼容旧调用：仅统计文章。</summary>
    public static WordFrequencyResult Compute(
        IReadOnlyList<BlogArticle> articles,
        int maxTerms,
        IReadOnlyCollection<string>? extraStopwords = null,
        int minOccurrences = 3,
        IReadOnlyCollection<string>? forceInclude = null) =>
        Compute(BuildCorpus(articles), maxTerms, extraStopwords, minOccurrences, forceInclude);

    private static bool TryNormalizeForcedToken(string raw, HashSet<string> forced, out string key)
    {
        key = "";
        if (forced.Count == 0)
            return false;
        var w = raw.Trim();
        if (w.Length == 0)
            return false;
        var hasHan = w.Any(static c => c is >= '\u4e00' and <= '\u9fff');
        key = hasHan ? w : w.ToLowerInvariant();
        return forced.Contains(key);
    }

    private static int CountSubstringOccurrences(
        IReadOnlyList<WordFrequencyDocument> documents,
        string term)
    {
        if (string.IsNullOrEmpty(term))
            return 0;
        var hasHan = term.Any(static c => c is >= '\u4e00' and <= '\u9fff');
        var comparison = hasHan ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var total = 0;
        foreach (var doc in documents)
        {
            var text = doc.PlainText;
            if (string.IsNullOrEmpty(text))
                continue;
            var idx = 0;
            while (idx <= text.Length - term.Length)
            {
                var found = text.IndexOf(term, idx, comparison);
                if (found < 0)
                    break;
                total++;
                idx = found + Math.Max(1, term.Length);
            }
        }

        return total;
    }

    private static HashSet<string> BuildCustomFilterSet(IReadOnlyCollection<string>? extraStopwords)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (extraStopwords == null)
            return set;

        foreach (var raw in extraStopwords)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var w = raw.Trim();
            var hasHan = w.Any(static c => c is >= '\u4e00' and <= '\u9fff');
            set.Add(hasHan ? w : w.ToLowerInvariant());
        }

        return set;
    }

    public static Dictionary<string, List<WordFrequencyArticleHit>> BuildTopTermHits(
        IReadOnlyList<WordFrequencyDocument> documents,
        IReadOnlyList<WordFrequencyItem> topTerms,
        int maxSnippetsPerArticle = 2)
    {
        var termSet = new HashSet<string>(topTerms.Select(t => t.Token), StringComparer.Ordinal);
        var result = topTerms.ToDictionary(t => t.Token, _ => new List<WordFrequencyArticleHit>(), StringComparer.Ordinal);

        foreach (var doc in documents)
        {
            var snippets = CollectCandidateSnippets(doc.PlainText, doc.Title);
            foreach (var term in termSet)
            {
                var hitSnippets = new List<string>();
                foreach (var s in snippets)
                {
                    if (!ContainsToken(s, term))
                        continue;
                    hitSnippets.Add(s);
                    if (hitSnippets.Count >= Math.Max(1, maxSnippetsPerArticle))
                        break;
                }

                if (hitSnippets.Count == 0)
                    continue;

                result[term].Add(new WordFrequencyArticleHit(doc.Title, doc.HtmlFileName, hitSnippets));
            }
        }

        return result;
    }

    public static Dictionary<string, List<WordFrequencyArticleHit>> BuildTopTermHits(
        IReadOnlyList<BlogArticle> articles,
        IReadOnlyList<WordFrequencyItem> topTerms,
        int maxSnippetsPerArticle = 2) =>
        BuildTopTermHits(BuildCorpus(articles), topTerms, maxSnippetsPerArticle);

    private static string CollectArticlePlainText(BlogArticle article)
    {
        var sb = new StringBuilder();
        sb.Append(article.Title).Append('\n');
        sb.Append(article.NotebookTitle).Append('\n');
        sb.Append(article.StructuralSection).Append('\n');
        foreach (var bm in article.Bookmarks)
            sb.Append(bm).Append('\n');

        foreach (var block in article.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    sb.Append(p.Text).Append('\n');
                    break;
                case RichParagraphBlock rp:
                    sb.Append(rp.PlainText).Append('\n');
                    break;
                case NoteBlock n:
                    sb.Append(n.PlainText).Append('\n');
                    break;
                case ImageBlock img when !string.IsNullOrWhiteSpace(img.AltText):
                    sb.Append(img.AltText.Trim()).Append('\n');
                    break;
            }
        }

        return sb.ToString();
    }

    private static HashSet<string> LoadStopwords()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "stopwords.txt");
            if (!File.Exists(path))
                return set;

            foreach (var line in File.ReadLines(path))
            {
                var s = line.Trim();
                if (s.Length > 0)
                    set.Add(s);
            }
        }
        catch
        {
            // 忽略，仅用长度与字符过滤
        }

        return set;
    }

    private static bool TryNormalizeToken(string raw, HashSet<string> stop, out string key)
    {
        key = "";
        var w = raw.Trim();
        if (w.Length == 0)
            return false;

        if (stop.Count > 0 && stop.Contains(w))
            return false;

        if (w.All(char.IsDigit))
            return false;

        var hasHan = w.Any(static c => c is >= '\u4e00' and <= '\u9fff');

        if (!hasHan)
        {
            if (w.Length < 3 || !LatinLetters.IsMatch(w))
                return false;
            key = w.ToLowerInvariant();
            return true;
        }

        if (w.Length < 2)
            return false;

        key = w;
        return true;
    }

    private static List<string> CollectCandidateSnippets(string plainText, string? title = null)
    {
        var list = new List<string>();
        void Add(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;
            foreach (var part in SentenceSplitter.Split(text))
            {
                var s = part.Trim();
                if (s.Length < 4)
                    continue;
                if (s.Length > 120)
                    s = s[..120] + "…";
                list.Add(s);
            }
        }

        Add(title);
        Add(plainText);
        return list;
    }

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
            return false;
        var hasHan = token.Any(static c => c is >= '\u4e00' and <= '\u9fff');
        return hasHan
            ? text.Contains(token, StringComparison.Ordinal)
            : text.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record WordFrequencyItem(string Token, int Count);

internal sealed record WordFrequencyResult(
    IReadOnlyList<WordFrequencyItem> TopTerms,
    int TotalTokenOccurrences,
    int UniqueTokens,
    int DocumentCount,
    int MaxCount,
    int MinCount);

internal sealed record WordFrequencyArticleHit(
    string Title,
    string HtmlFileName,
    IReadOnlyList<string> Snippets);
