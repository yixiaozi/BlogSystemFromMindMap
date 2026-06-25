using System.Text.Json.Serialization;

namespace MindmapBlog;

/// <summary>写入 <c>data/git-commits.json</c> 的 Git 提交快照。</summary>
public sealed class GitCommitHistorySnapshot
{
    [JsonPropertyName("isGitRepo")]
    public bool IsGitRepo { get; set; }

    [JsonPropertyName("scanRoot")]
    public string ScanRoot { get; set; } = "";

    [JsonPropertyName("repoRoot")]
    public string? RepoRoot { get; set; }

    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    [JsonPropertyName("logScopeRelative")]
    public string? LogScopeRelative { get; set; }

    [JsonPropertyName("collectedAtUtc")]
    public DateTimeOffset CollectedAtUtc { get; set; }

    [JsonPropertyName("commits")]
    public List<GitCommitRecord> Commits { get; set; } = new();
}

public sealed class GitCommitRecord
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("committedAt")]
    public DateTimeOffset CommittedAt { get; set; }

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonIgnore]
    public string ShortHash => Hash.Length >= 7 ? Hash[..7] : Hash;
}
