namespace MindmapBlog;

/// <summary>左侧磁盘文件夹层级（相对于扫描目录）。</summary>
internal sealed class FolderBranch
{
    public Dictionary<string, FolderBranch> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>完整 .mm 路径 → 该文件内的文章。</summary>
    public Dictionary<string, List<BlogArticle>> MindmapFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>单个 .mm 内，按 StructuralSection 路径分叉的导图节点树。</summary>
internal sealed class MapTrieNode
{
    public Dictionary<string, MapTrieNode> Segments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<BlogArticle> ArticlesHere { get; } = new();
}
