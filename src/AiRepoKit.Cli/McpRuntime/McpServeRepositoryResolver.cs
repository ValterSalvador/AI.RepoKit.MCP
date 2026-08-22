namespace AiRepoKit.Cli.McpRuntime;

public static class McpServeRepositoryResolver
{
    public static (bool Success, string RepoRoot, string ErrorMessage) Resolve(string? explicitRepoPath_ = null, string? currentDirectory_ = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRepoPath_))
        {
            string fullPath = Path.GetFullPath(explicitRepoPath_);
            if (!Directory.Exists(fullPath))
            {
                return (false, string.Empty, $"Specified repository path does not exist or is not a directory: '{explicitRepoPath_}'.");
            }

            return (true, fullPath, string.Empty);
        }

        string startDirectory = Path.GetFullPath(currentDirectory_ ?? Directory.GetCurrentDirectory());
        string? gitRoot = FindNearestGitRoot(startDirectory);
        if (gitRoot is null)
        {
            return (false, string.Empty, $"No Git repository found starting from '{startDirectory}'. Specify target repository with --repo <path>.");
        }

        return (true, gitRoot, string.Empty);
    }

    public static string? FindNearestGitRoot(string startDirectory_)
    {
        DirectoryInfo? current = new(startDirectory_);
        while (current is not null)
        {
            string gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }
}
