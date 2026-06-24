namespace MindmapBlog;

/// <summary>导图「变量」节点解析出的站点配置项。</summary>
public sealed record SiteVariables(
    string SourceFile,
    string? BlogTitle,
    string? Signature,
    string? AboutBody,
    string? AboutBodyHtml = null);
