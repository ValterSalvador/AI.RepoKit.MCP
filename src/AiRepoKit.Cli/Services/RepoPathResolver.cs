namespace AiRepoKit.Cli.Services;

public sealed class RepoPathResolver
{
    public string Resolve(string? repoPath_, string command_)
    {
        if (!string.IsNullOrWhiteSpace(repoPath_))
        {
            return this.ResolveRoot(Path.GetFullPath(repoPath_), command_) ?? Path.GetFullPath(repoPath_);
        }

        string currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        string? currentRoot = this.ResolveRoot(currentDirectory, command_);
        if (!string.IsNullOrWhiteSpace(currentRoot))
        {
            return currentRoot;
        }

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            string executableDirectory = Path.GetFullPath(Path.GetDirectoryName(processPath) ?? string.Empty);
            string? executableRoot = this.ResolveRoot(executableDirectory, command_);
            if (!string.IsNullOrWhiteSpace(executableRoot))
            {
                return executableRoot;
            }
        }

        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        string? baseRoot = this.ResolveRoot(baseDirectory, command_);
        if (!string.IsNullOrWhiteSpace(baseRoot))
        {
            return baseRoot;
        }

        if (string.Equals(command_, "efficiency", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command_, "token-report", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command_, "context-efficiency", StringComparison.OrdinalIgnoreCase))
        {
            return currentDirectory;
        }

        throw new InvalidOperationException("Could not find a target repository from the current directory or executable folder. Pass --repo <path>.");
    }

    public string? ResolveRoot(string startPath_, string command_)
    {
        string startPath = Path.GetFullPath(startPath_);
        if (string.Equals(command_, "sample", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(startPath)
            && !Directory.EnumerateFileSystemEntries(startPath).Any())
        {
            return startPath;
        }

        string? gitRoot = new GitService().TryGetRepoRoot(startPath);
        if (!string.IsNullOrWhiteSpace(gitRoot))
        {
            return gitRoot;
        }

        DirectoryInfo? current = new(startPath);
        if (!Directory.Exists(startPath))
        {
            current = Directory.GetParent(startPath);
        }

        while (current is not null)
        {
            if (this.LooksLikeTargetRepository(current.FullName, command_))
            {
                return Path.GetFullPath(current.FullName);
            }

            current = current.Parent;
        }

        return null;
    }

    public bool LooksLikeTargetRepository(string path_, string command_)
    {
        if (!Directory.Exists(path_))
        {
            return false;
        }

        if (string.Equals(command_, "sample", StringComparison.OrdinalIgnoreCase)
            && !Directory.EnumerateFileSystemEntries(path_).Any())
        {
            return true;
        }

        return Directory.EnumerateFiles(path_, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(path_, "*.slnx", SearchOption.TopDirectoryOnly).Any()
            || Directory.Exists(Path.Combine(path_, ".git"))
            || Directory.Exists(Path.Combine(path_, ".ai"))
            || Directory.Exists(Path.Combine(path_, "Tools", "AiContextMcp"))
            || File.Exists(Path.Combine(path_, "Directory.Build.props"))
            || File.Exists(Path.Combine(path_, "Directory.Build.targets"))
            || File.Exists(Path.Combine(path_, "package.json"))
            || File.Exists(Path.Combine(path_, "tsconfig.json"))
            || File.Exists(Path.Combine(path_, "pyproject.toml"))
            || File.Exists(Path.Combine(path_, "requirements.txt"))
            || File.Exists(Path.Combine(path_, "composer.json"))
            || this.HasRootOrImmediateProject(path_);
    }

    private bool HasRootOrImmediateProject(string path_)
    {
        if (Directory.EnumerateFiles(path_, "*.csproj", SearchOption.TopDirectoryOnly).Any())
        {
            return true;
        }

        foreach (string directory in Directory.EnumerateDirectories(path_, "*", SearchOption.TopDirectoryOnly))
        {
            if (Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
        }

        return false;
    }
}
