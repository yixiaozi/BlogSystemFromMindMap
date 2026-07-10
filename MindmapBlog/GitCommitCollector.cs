using System.Diagnostics;
using System.Text;

namespace MindmapBlog;

internal static class GitCommitCollector
{
    private const char FieldSep = '\x1f';
    private const char RecordSep = '\x1e';

    public static GitCommitHistorySnapshot Collect(string scanRoot)
    {
        var scanFull = Path.GetFullPath(scanRoot);
        var snapshot = new GitCommitHistorySnapshot
        {
            ScanRoot = scanFull,
            CollectedAtUtc = DateTimeOffset.UtcNow,
        };

        var repoRoot = TryGetRepoRoot(scanFull);
        if (repoRoot == null)
            return snapshot;

        snapshot.IsGitRepo = true;
        snapshot.RepoRoot = repoRoot;
        snapshot.Branch = RunGit(repoRoot, ["rev-parse", "--abbrev-ref", "HEAD"])?.Trim();

        var scopeRel = GetLogScopeRelative(repoRoot, scanFull);
        snapshot.LogScopeRelative = scopeRel;

        var logArgs = new List<string>
        {
            "log",
            "--no-merges",
            "--date=iso-strict",
            $"--pretty=format:%H{FieldSep}%aI{FieldSep}%s{FieldSep}%b{RecordSep}",
        };
        if (!string.IsNullOrEmpty(scopeRel))
        {
            logArgs.Add("--");
            logArgs.Add(scopeRel.Replace('\\', '/'));
        }

        var logOutput = RunGit(repoRoot, [.. logArgs], timeoutMs: 120_000);
        if (string.IsNullOrWhiteSpace(logOutput))
            return snapshot;

        foreach (var record in logOutput.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = record.Trim('\r', '\n', ' ', '\t');
            if (trimmed.Length == 0)
                continue;

            var parts = trimmed.Split(FieldSep, 4);
            if (parts.Length < 3)
                continue;

            if (!DateTimeOffset.TryParse(parts[1], out var committedAt))
                continue;

            var subject = parts[2].Trim();
            if (IsMergeCommitSubject(subject))
                continue;

            var body = parts.Length >= 4 ? parts[3].Trim() : "";

            snapshot.Commits.Add(new GitCommitRecord
            {
                Hash = parts[0],
                CommittedAt = committedAt,
                Subject = subject,
                Body = body,
            });
        }

        return snapshot;
    }

    /// <summary>兜底过滤：即使未带 <c>--no-merges</c>，也跳过常见合并提交说明。</summary>
    internal static bool IsMergeCommitSubject(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var s = subject.Trim();
        return s.StartsWith("Merge branch ", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Merge pull request ", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("Merge remote-tracking branch ", StringComparison.OrdinalIgnoreCase)
               || s.Equals("Merge branch", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLogScopeRelative(string repoRoot, string scanFull)
    {
        try
        {
            var rel = Path.GetRelativePath(repoRoot, scanFull);
            if (string.IsNullOrEmpty(rel) || rel == ".")
                return null;
            return rel;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetRepoRoot(string path)
    {
        var root = RunGit(path, ["rev-parse", "--show-toplevel"])?.Trim();
        return string.IsNullOrWhiteSpace(root) ? null : root;
    }

    private static string? RunGit(string workingDirectory, string[] args, int timeoutMs = 30_000)
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
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            return process.ExitCode == 0 ? stdout : null;
        }
        catch
        {
            return null;
        }
    }
}
