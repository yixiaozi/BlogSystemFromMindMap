using System.Text.Json;
using System.Text.Json.Serialization;

namespace MindmapBlog;

internal static class GitCommitStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string HistoryFilePath(string outputRoot) =>
        Path.Combine(outputRoot, "data", "git-commits.json");

    public static void Save(string outputRoot, GitCommitHistorySnapshot snapshot)
    {
        var path = HistoryFilePath(outputRoot);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(path, json);
    }
}
