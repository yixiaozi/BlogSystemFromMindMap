namespace MindmapBlog;

/// <summary>
/// 一篇文章内的一张已发布图片（用于右侧图册与图册索引页）。
/// </summary>
internal sealed record ArticleGalleryItem(
    /// <summary>文章 HTML 路径（相对站点根）。</summary>
    string ArticleWebPath,
    /// <summary>媒体文件路径（相对站点根），如 <c>media/xxx.jpg</c>。</summary>
    string MediaPathFromSiteRoot,
    /// <summary>展示用说明（多为图片 alt）。</summary>
    string Caption,
    /// <summary>在该篇文章内的序号，与正文 <c>&lt;figure id="img-{n}"&gt;</c> 一致。</summary>
    int ImageIndexInArticle);
