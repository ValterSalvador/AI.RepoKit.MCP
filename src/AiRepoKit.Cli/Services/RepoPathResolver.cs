namespace AiRepoKit.Cli.Services;

public sealed class RepoPathResolver
{
    public string Resolve(string? repoPath_, string command_)
    {
        if (!string.IsNullOrWhiteSpace(repoPath_))
        {
            return Path.GetFullPath(repoPath_);
        }

        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (this.LooksLikeTargetRepository(baseDirectory, command_))
        {
            return baseDirectory;
        }

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            string executableDirectory = Path.GetFullPath(Path.GetDirectoryName(processPath) ?? string.Empty);
            if (this.LooksLikeTargetRepository(executableDirectory, command_))
            {
                return executableDirectory;
            }
        }

        string currentDirectory = Path.GetFullPath(Directory.GetCurrentDirectory());
        if (this.LooksLikeTargetRepository(currentDirectory, command_))
        {
            return currentDirectory;
        }

        if (string.Equals(command_, "efficiency", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command_, "token-report", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command_, "context-efficiency", StringComparison.OrdinalIgnoreCase))
        {
            return currentDirectory;
        }

        throw new InvalidOperationException("Could not find a target repository from the executable folder or current directory. Pass --repo <path>.");
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
