using System.Security.Cryptography;
using System.Text;

namespace MindmapBlog;

/// <summary>
/// 跨次生成保持稳定：同一 .mm + 同一文章节点 ID 对应同一存储键；
/// 发布路径由 <see cref="AssignPublishFileNames"/> 分配（镜像扫描目录）。
/// </summary>
internal static class ArticleIdentity
{
    public static string ComputeStorageKey(string sourceMmFullPath, string articleNodeId)
    {
        var payload = $"{sourceMmFullPath}\x1E{articleNodeId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash[..12]).ToLowerInvariant();
    }

    /// <summary>生成阶段填好 <see cref="BlogArticle.PublishWebPath"/> 后返回该路径，否则回退为根目录哈希名。</summary>
    public static string ResolveHtmlFileName(BlogArticle article)
    {
        if (!string.IsNullOrEmpty(article.PublishWebPath))
            return article.PublishWebPath;

        return HashHtmlFileName(article.SourceMmPath, article.ArticleNodeId);
    }

    internal static string HashHtmlFileName(string sourceMmFullPath, string articleNodeId) =>
        $"{ComputeStorageKey(sourceMmFullPath, articleNodeId)}.html";

    internal static void AssignPublishFileNames(
        IReadOnlyList<BlogArticle> sortedArticles,
        string scanRootFull,
        HashSet<string> used)
    {
        foreach (var article in sortedArticles)
        {
            var dirWeb = SitePathHelper.GetMmParentWebDir(scanRootFull, article.SourceMmPath);
            var stem = SlugUtility.SanitizeFileStem(article.Title);
            if (string.IsNullOrEmpty(stem))
                stem = "未命名";

            article.PublishWebPath = SlugUtility.AllocateWebPath(dirWeb, stem, used);
        }
    }
}
