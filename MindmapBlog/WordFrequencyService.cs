using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JiebaNet.Segmenter;

namespace MindmapBlog;

internal static class WordFrequencyService
{
    private static readonly Regex LatinLetters = new(@"[a-zA-Z]", RegexOptions.Compiled);

    /// <summary>聚合全部文章正文、标题、书签等文本后的词频（jieba 精确模式分词）。</summary>
    public static WordFrequencyResult Compute(IReadOnlyList<BlogArticle> articles, int maxTerms)
    {
        var segmenter = new JiebaSegmenter();
        var stop = LoadStopwords();
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
                totalHits++;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        var ranked = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, maxTerms))
            .Select(kv => new WordFrequencyItem(kv.Key, kv.Value))
            .ToList();

        var maxCount = ranked.Count > 0 ? ranked[0].Count : 0;
        var minCount = ranked.Count > 0 ? ranked[^1].Count : 0;

        return new WordFrequencyResult(
            ranked,
            totalHits,
            counts.Count,
            articles.Count,
            maxCount,
            minCount);
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
}

internal sealed record WordFrequencyItem(string Token, int Count);

internal sealed record WordFrequencyResult(
    IReadOnlyList<WordFrequencyItem> TopTerms,
    int TotalTokenOccurrences,
    int UniqueTokens,
    int ArticleCount,
    int MaxCount,
    int MinCount);
