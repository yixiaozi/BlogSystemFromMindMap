using System.Diagnostics;
using System.Text;

namespace MindmapBlog;

/// <summary>
/// 在 Git sparse / partial clone（blob:none）工作区中，按需检出文件内容。
/// 本地完整工作区时文件已存在，此步骤为空操作。
/// </summary>
internal static class GitWorkingTreeMaterializer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".svg", ".bmp",
    };

    public static void EnsureArticleImages(string scanRoot, IEnumerable<BlogArticle> articles)
    {
        var paths = articles
            .SelectMany(a => a.Blocks.OfType<ImageBlock>())
            .Select(b => b.ResolvedSourcePath);
        EnsureFiles(scanRoot, paths);
    }

    /// <summary>按仓库内路径名猜测头像文件并检出（不依赖本地目录枚举）。</summary>
    public static void EnsureLikelyAvatars(string scanRoot)
    {
        var repoRoot = TryGetRepoRoot(scanRoot);
        if (repoRoot == null)
            return;

        var listed = RunGit(repoRoot, ["ls-files", "-z"], timeoutMs: 120_000);
        if (string.IsNullOrEmpty(listed))
            return;

        var hits = new List<string>();
        foreach (var rel in listed.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var norm = rel.Replace('\\', '/');
            var ext = Path.GetExtension(norm);
            if (!ImageExtensions.Contains(ext))
                continue;

            var fileName = Path.GetFileName(norm);
            var dirHint = Path.GetDirectoryName(norm)?.Replace('\\', '/') ?? "";
            var looksAvatar =
                fileName.Contains("avatar", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains("avator", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("site-avatar", StringComparison.OrdinalIgnoreCase)
                || dirHint.Contains("/Avator", StringComparison.OrdinalIgnoreCase)
                || dirHint.Contains("/Avatar", StringComparison.OrdinalIgnoreCase)
                || dirHint.EndsWith("Avator", StringComparison.OrdinalIgnoreCase)
                || dirHint.EndsWith("Avatar", StringComparison.OrdinalIgnoreCase);

            if (!looksAvatar)
                continue;

            hits.Add(Path.Combine(repoRoot, norm.Replace('/', Path.DirectorySeparatorChar)));
            if (hits.Count >= 40)
                break;
        }

        EnsureFiles(scanRoot, hits);
    }

    public static void EnsureFiles(string scanRoot, IEnumerable<string?> absolutePaths)
    {
        var repoRoot = TryGetRepoRoot(scanRoot);
        if (repoRoot == null)
            return;

        var missingRels = new List<string>();
        foreach (var abs in absolutePaths)
        {
            if (string.IsNullOrWhiteSpace(abs))
                continue;

            string full;
            try
            {
                full = Path.GetFullPath(abs);
            }
            catch
            {
                continue;
            }

            if (File.Exists(full))
            {
                try
                {
                    if (new FileInfo(full).Length > 0)
                        continue;
                }
                catch
                {
                    // treat as missing
                }
            }

            string rel;
            try
            {
                rel = Path.GetRelativePath(repoRoot, full);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrEmpty(rel) || rel == "." || rel.StartsWith("..", StringComparison.Ordinal))
                continue;
            if (Path.IsPathRooted(rel))
                continue;

            missingRels.Add(rel.Replace('\\', '/'));
        }

        var distinct = missingRels
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (distinct.Count == 0)
            return;

        Console.WriteLine($"Git materialize: {distinct.Count} file(s) from sparse/partial checkout…");

        foreach (var chunk in Chunk(distinct, 60))
        {
            // sparse 模式下先纳入规则，再 checkout 以拉取 blob:none 的内容
            _ = RunGit(repoRoot, ["sparse-checkout", "add", "--skip-checks", ..chunk], timeoutMs: 120_000);
            _ = RunGit(repoRoot, ["checkout", "HEAD", "--", ..chunk], timeoutMs: 300_000);
        }
    }

    private static IEnumerable<string[]> Chunk(IReadOnlyList<string> items, int size)
    {
        for (var i = 0; i < items.Count; i += size)
        {
            var n = Math.Min(size, items.Count - i);
            var part = new string[n];
            for (var j = 0; j < n; j++)
                part[j] = items[i + j];
            yield return part;
        }
    }

    private static string? TryGetRepoRoot(string path)
    {
        var root = RunGit(path, ["rev-parse", "--show-toplevel"])?.Trim();
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private static string? RunGit(string workingDirectory, string[] args, int timeoutMs = 60_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(stderr))
                    Console.Error.WriteLine($"git {args[0]}: {stderr.Trim()}");
                return null;
            }

            return stdout;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"git failed: {ex.Message}");
            return null;
        }
    }
}
