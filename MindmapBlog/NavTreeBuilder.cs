namespace MindmapBlog;

internal static class NavTreeBuilder
{
    public static FolderBranch BuildFolderTree(IReadOnlyList<BlogArticle> articles, string scanRoot)
    {
        var root = new FolderBranch();
        foreach (var grp in articles.GroupBy(a => a.SourceMmPath))
        {
            var full = grp.Key;
            var rel = Path.GetRelativePath(scanRoot, full);
            var dir = Path.GetDirectoryName(rel);
            var segs = string.IsNullOrEmpty(dir)
                ? Array.Empty<string>()
                : dir.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

            var node = root;
            foreach (var seg in segs)
            {
                if (!node.Dirs.TryGetValue(seg, out var next))
                {
                    next = new FolderBranch();
                    node.Dirs[seg] = next;
                }

                node = next;
            }

            node.MindmapFiles[full] = grp.OrderByDescending(a => a.Modified).ToList();
        }

        return root;
    }

    public static MapTrieNode BuildMapTrie(List<BlogArticle> articlesInFile)
    {
        var root = new MapTrieNode();
        foreach (var a in articlesInFile)
            AddToMapTrie(root, a);
        return root;
    }

    public static void AddToMapTrie(MapTrieNode root, BlogArticle a)
    {
        var parts = SplitStructuralPath(a.StructuralSection);
        var n = root;
        foreach (var p in parts)
        {
            if (!n.Segments.TryGetValue(p, out var nx))
            {
                nx = new MapTrieNode();
                n.Segments[p] = nx;
            }

            n = nx;
        }

        n.ArticlesHere.Add(a);
    }

    public static string[] SplitStructuralPath(string structuralSection)
    {
        var s = structuralSection.Trim();
        if (string.IsNullOrEmpty(s) || string.Equals(s, "未分区", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        return s.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    public static List<BlogArticle> CollectSubtreeArticles(MapTrieNode node)
    {
        var list = new List<BlogArticle>(node.ArticlesHere);
        foreach (var c in node.Segments.Values)
            list.AddRange(CollectSubtreeArticles(c));
        return list;
    }

    public static List<BlogArticle> CollectFolderSubtreeArticles(FolderBranch fb)
    {
        var list = new List<BlogArticle>();
        foreach (var arts in fb.MindmapFiles.Values)
            list.AddRange(arts);
        foreach (var sub in fb.Dirs.Values)
            list.AddRange(CollectFolderSubtreeArticles(sub));
        return list;
    }
}
