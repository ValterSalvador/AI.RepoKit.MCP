using AiRepoKit.Cli.Models.ChangedFiles;
using AiRepoKit.Cli.Services.CodeIndex;

namespace AiRepoKit.Cli.Services.ChangedFiles;

public sealed class ChangedFilesService
{
    private readonly GitService git = new();
    private readonly CodeFileDiscoveryService discovery = new();

    public ChangedFilesResult GetChangedFiles(string repoRoot_, string since_ = "")
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        List<string> warnings = [];
        if (!this.git.IsGitAvailable())
        {
            warnings.Add("Git is not available; changed-files detection used a conservative empty fallback.");
            return this.Create(repoRoot, since_, false, [], [], [], [], warnings);
        }

        if (this.git.TryGetRepoRoot(repoRoot) is null)
        {
            warnings.Add("Target path is not inside a Git repository; changed-files detection used a conservative empty fallback.");
            return this.Create(repoRoot, since_, true, [], [], [], [], warnings);
        }

        IReadOnlyList<string> staged = this.Filter(repoRoot, this.git.GetStagedFiles(repoRoot));
        IReadOnlyList<string> unstaged = this.Filter(repoRoot, this.git.GetUnstagedFiles(repoRoot));
        IReadOnlyList<string> untracked = this.Filter(repoRoot, this.git.GetUntrackedFiles(repoRoot));
        IReadOnlyList<string> branch = this.Filter(repoRoot, this.git.GetFilesChangedSince(repoRoot, since_));
        if (!string.IsNullOrWhiteSpace(since_) && branch.Count == 0)
        {
            warnings.Add($"No files were found for branch diff `{since_}...HEAD`.");
        }

        return this.Create(repoRoot, since_, true, staged, unstaged, untracked, branch, warnings);
    }

    private ChangedFilesResult Create(
        string repoRoot_,
        string since_,
        bool gitAvailable_,
        IReadOnlyList<string> staged_,
        IReadOnlyList<string> unstaged_,
        IReadOnlyList<string> untracked_,
        IReadOnlyList<string> branch_,
        IReadOnlyList<string> warnings_)
    {
        Dictionary<string, ChangedFileItem> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in staged_)
        {
            files[path] = Merge(files.GetValueOrDefault(path), path, staged: true, unstaged: false, untracked: false);
        }

        foreach (string path in unstaged_)
        {
            files[path] = Merge(files.GetValueOrDefault(path), path, staged: false, unstaged: true, untracked: false);
        }

        foreach (string path in untracked_)
        {
            files[path] = Merge(files.GetValueOrDefault(path), path, staged: false, unstaged: false, untracked: true);
        }

        foreach (string path in branch_)
        {
            files[path] = Merge(files.GetValueOrDefault(path), path, staged: false, unstaged: true, untracked: false);
        }

        return new ChangedFilesResult(
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            repoRoot_,
            since_,
            gitAvailable_,
            files.Values.OrderBy(file_ => file_.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings_);
    }

    private IReadOnlyList<string> Filter(string repoRoot_, IReadOnlyList<string> files_)
    {
        return files_
            .Select(path_ => path_.Replace('\\', '/').TrimStart('/'))
            .Where(path_ => !string.IsNullOrWhiteSpace(path_))
            .Where(path_ => !this.IsRestricted(path_))
            .Where(path_ => !this.discovery.IsIgnoredDirectory(Path.GetDirectoryName(path_)?.Replace('\\', '/') ?? string.Empty))
            .Where(path_ => !this.discovery.IsIgnoredFile(path_))
            .Where(path_ => !this.git.IsIgnored(repoRoot_, path_))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private bool IsRestricted(string relativePath_)
    {
        string path = relativePath_.Replace('\\', '/').TrimStart('/');
        string[] restricted =
        [
            ".git/",
            ".vs/",
            "bin/",
            "obj/",
            "artifacts/",
            "packages/",
            "node_modules/",
            ".ai/generated/",
            ".tmp/",
            ".cache/"
        ];
        return restricted.Any(entry_ => path.StartsWith(entry_, StringComparison.OrdinalIgnoreCase)
            || path.Contains("/" + entry_, StringComparison.OrdinalIgnoreCase));
    }

    private static ChangedFileItem Merge(ChangedFileItem? existing_, string path_, bool staged, bool unstaged, bool untracked)
    {
        bool isStaged = staged || existing_?.Staged == true;
        bool isUnstaged = unstaged || existing_?.Unstaged == true;
        bool isUntracked = untracked || existing_?.Untracked == true;
        string status = string.Join("+", new[]
        {
            isStaged ? "staged" : string.Empty,
            isUnstaged ? "unstaged" : string.Empty,
            isUntracked ? "untracked" : string.Empty
        }.Where(value_ => !string.IsNullOrWhiteSpace(value_)));
        return new ChangedFileItem(path_, status, isStaged, isUnstaged, isUntracked);
    }
}
