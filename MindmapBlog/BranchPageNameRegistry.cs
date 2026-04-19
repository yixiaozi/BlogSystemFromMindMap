namespace MindmapBlog;

/// <summary>
/// 为文件夹分支页、导图分支页生成与磁盘/导图一致的中文友好文件名，并在全局 <see cref="HashSet{T}"/> 内去重。
/// </summary>
internal sealed class BranchPageNameRegistry
{
    private readonly Dictionary<string, string> _folderPages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mmPrefixPages = new(StringComparer.Ordinal);

    private BranchPageNameRegistry()
    {
    }

    public static BranchPageNameRegistry Build(
        IReadOnlyList<BlogArticle> sortedArticles,
        string scanRootFull,
        HashSet<string> reservedHtmlFileNames)
    {
        var reg = new BranchPageNameRegistry();

        void RegisterFolder(List<string> pathSegs, string stem)
        {
            var dirWeb = SitePathHelper.FolderSegmentsToWebDir(pathSegs);
            var fn = SlugUtility.AllocateWebPath(dirWeb, stem, reservedHtmlFileNames);
            reg._folderPages[FolderKey(pathSegs)] = fn;
        }

        void EmitFolders(FolderBranch fb, List<string> pathSegs)
        {
            var arts = NavTreeBuilder.CollectFolderSubtreeArticles(fb);
            if (arts.Count > 0)
            {
                var stem = pathSegs.Count == 0 ? "浏览-全部文章" : "分支列表";
                RegisterFolder(pathSegs, stem);
            }

            foreach (var kv in fb.Dirs.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                EmitFolders(kv.Value, pathSegs.Concat(new[] { kv.Key }).ToList());
        }

        var folderRoot = NavTreeBuilder.BuildFolderTree(sortedArticles, scanRootFull);
        EmitFolders(folderRoot, []);

        foreach (var grp in sortedArticles.GroupBy(a => a.SourceMmPath))
        {
            var mmPath = grp.Key;
            var trie = NavTreeBuilder.BuildMapTrie(grp.ToList());

            void Visit(MapTrieNode node, List<string> prefix)
            {
                var arts = NavTreeBuilder.CollectSubtreeArticles(node);
                if (arts.Count == 0)
                    return;

                var joined = string.Join("/", prefix);
                var mmFull = Path.GetFullPath(mmPath);
                var parentDir = Path.GetDirectoryName(mmFull);
                var parentName = string.IsNullOrEmpty(parentDir) ? "根" : Path.GetFileName(parentDir);
                var parentTok = SlugUtility.FileNameToken(parentName);
                var fileTok = SlugUtility.FileNameToken(Path.GetFileNameWithoutExtension(mmFull));

                string stem;
                if (string.IsNullOrEmpty(joined))
                    stem = $"导图-{parentTok}-{fileTok}";
                else
                {
                    var parts = joined.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(SlugUtility.FileNameToken);
                    var prefixSlug = string.Join("-", parts);
                    stem = $"导图-{parentTok}-{fileTok}-分支-{prefixSlug}";
                }

                var dirWeb = SitePathHelper.GetMmParentWebDir(scanRootFull, mmPath);
                var fn = SlugUtility.AllocateWebPath(dirWeb, stem, reservedHtmlFileNames);
                reg._mmPrefixPages[MmPrefixKey(mmFull, joined)] = fn;

                foreach (var kv in node.Segments.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    Visit(kv.Value, prefix.Concat(new[] { kv.Key }).ToList());
            }

            Visit(trie, []);
        }

        return reg;
    }

    private static string FolderKey(IReadOnlyList<string> pathSegs) =>
        string.Join('\x1E', pathSegs);

    private static string MmPrefixKey(string mmFullPath, string prefixJoined) =>
        Path.GetFullPath(mmFullPath) + "\x1E" + prefixJoined;

    public string GetFolderListPage(IReadOnlyList<string> pathSegs) =>
        _folderPages[FolderKey(pathSegs)];

    public string GetMmPrefixListPage(string mmFullPath, string structuralPrefixJoined) =>
        _mmPrefixPages[MmPrefixKey(mmFullPath, structuralPrefixJoined)];
}
