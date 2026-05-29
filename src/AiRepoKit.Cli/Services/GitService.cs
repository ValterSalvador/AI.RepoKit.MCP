using System.Diagnostics;
using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class GitService
{
    public bool IsGitAvailable()
    {
        try
        {
            using Process process = new();
            process.StartInfo.FileName = "git";
            process.StartInfo.ArgumentList.Add("--version");
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public string? TryGetRepoRoot(string path_)
    {
        try
        {
            string workingDirectory = Directory.Exists(path_)
                ? Path.GetFullPath(path_)
                : Path.GetFullPath(Path.GetDirectoryName(path_) ?? Directory.GetCurrentDirectory());
            ProcessResult result = new ProcessRunner().Run("git", ["rev-parse", "--show-toplevel"], workingDirectory);
            if (!result.Success)
            {
                return null;
            }

            string root = result.StandardOutput.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
            return string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) ? null : Path.GetFullPath(root);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetVisibleFiles(string repoRoot_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        if (!Directory.Exists(repoRoot))
        {
            return [];
        }

        try
        {
            ProcessResult result = new ProcessRunner().Run("git", ["ls-files", "--cached", "--others", "--exclude-standard"], repoRoot);
            if (!result.Success)
            {
                return [];
            }

            return result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path_ => path_.Replace('\\', '/').TrimStart('/'))
                .Where(path_ => !string.IsNullOrWhiteSpace(path_))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<string> GetStagedFiles(string repoRoot_)
    {
        return this.RunGitLines(repoRoot_, ["diff", "--name-only", "--cached", "--diff-filter=ACMRT"]);
    }

    public IReadOnlyList<string> GetUnstagedFiles(string repoRoot_)
    {
        return this.RunGitLines(repoRoot_, ["diff", "--name-only", "--diff-filter=ACMRT"]);
    }

    public IReadOnlyList<string> GetUntrackedFiles(string repoRoot_)
    {
        return this.RunGitLines(repoRoot_, ["ls-files", "--others", "--exclude-standard"]);
    }

    public IReadOnlyList<string> GetFilesChangedSince(string repoRoot_, string since_)
    {
        if (string.IsNullOrWhiteSpace(since_))
        {
            return [];
        }

        return this.RunGitLines(repoRoot_, ["diff", "--name-only", $"{since_}...HEAD", "--"]);
    }

    public bool IsIgnored(string repoRoot_, string relativePath_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        if (!Directory.Exists(repoRoot))
        {
            return false;
        }

        try
        {
            ProcessResult result = new ProcessRunner().Run("git", ["check-ignore", "-q", "--", relativePath_.Replace('\\', '/')], repoRoot);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private IReadOnlyList<string> RunGitLines(string repoRoot_, IReadOnlyList<string> arguments_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        if (!Directory.Exists(repoRoot))
        {
            return [];
        }

        try
        {
            ProcessResult result = new ProcessRunner().Run("git", arguments_, repoRoot);
            if (!result.Success)
            {
                return [];
            }

            return result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path_ => path_.Replace('\\', '/').TrimStart('/'))
                .Where(path_ => !string.IsNullOrWhiteSpace(path_))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }
}
