using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JiebaNet.Segmenter;

namespace MindmapBlog;

internal static class WordFrequencyService
{
    private static readonly Regex LatinLetters = new(@"[a-zA-Z]", RegexOptions.Compiled);
    private static readonly Regex SentenceSplitter = new(@"(?<=[。！？!?；;])\s*|\r?\n+", RegexOptions.Compiled);

    /// <summary>聚合全部文章正文、标题、书签等文本后的词频（jieba 精确模式分词）。</summary>
    /// <param name="minOccurrences">至少出现多少次才计入结果（默认 3，即排除 2 次及以内）。</param>
    public static WordFrequencyResult Compute(
        IReadOnlyList<BlogArticle> articles,
        int maxTerms,
        IReadOnlyCollection<string>? extraStopwords = null,
        int minOccurrences = 3)
    {
        var segmenter = new JiebaSegmenter();
        var stop = LoadStopwords();
        var customFilter = BuildCustomFilterSet(extraStopwords);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalHits = 0;

        foreach (var article in articles)
        {
            var blob = CollectPlainText(article);
            if (string.IsNullOrWhiteSpace(blob))
                continue;

            foreach (var raw in segmenter.Cut(blob))
            {
                if (!TryNormalizeToken(raw, stop, out var key))
                    continue;
                if (customFilter.Contains(key))
                    continue;
                totalHits++;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var minCount = Math.Max(1, minOccurrences);
        var eligible = counts
            .Where(kv => kv.Value >= minCount && !customFilter.Contains(kv.Key))
            .ToList();

        var ranked = eligible
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, maxTerms))
            .Select(kv => new WordFrequencyItem(kv.Key, kv.Value))
            .ToList();

        var maxRanked = ranked.Count > 0 ? ranked[0].Count : 0;
        var minRanked = ranked.Count > 0 ? ranked[^1].Count : 0;

        return new WordFrequencyResult(
            ranked,
            totalHits,
            eligible.Count,
            articles.Count,
            maxRanked,
            minRanked);
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
        IReadOnlyList<BlogArticle> articles,
        IReadOnlyList<WordFrequencyItem> topTerms,
        int maxSnippetsPerArticle = 2)
    {
        var termSet = new HashSet<string>(topTerms.Select(t => t.Token), StringComparer.Ordinal);
        var result = topTerms.ToDictionary(t => t.Token, _ => new List<WordFrequencyArticleHit>(), StringComparer.Ordinal);

        foreach (var article in articles)
        {
            var snippets = CollectCandidateSnippets(article);
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

                result[term].Add(new WordFrequencyArticleHit(article.Title, article.HtmlFileName, hitSnippets));
            }
        }

        return result;
    }

    private static string CollectPlainText(BlogArticle article)
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

    private static List<string> CollectCandidateSnippets(BlogArticle article)
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

        Add(article.Title);
        Add(article.StructuralSection);
        foreach (var bm in article.Bookmarks)
            Add(bm);

        foreach (var block in article.Blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    Add(p.Text);
                    break;
                case RichParagraphBlock rp:
                    Add(rp.PlainText);
                    break;
                case NoteBlock n:
                    Add(n.PlainText);
                    break;
                case ImageBlock img when !string.IsNullOrWhiteSpace(img.AltText):
                    Add(img.AltText);
                    break;
            }
        }

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
    int ArticleCount,
    int MaxCount,
    int MinCount);

internal sealed record WordFrequencyArticleHit(
    string Title,
    string HtmlFileName,
    IReadOnlyList<string> Snippets);
