using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class McpBuildService
{
    private static readonly string[] InputExtensions = [".cs", ".csproj", ".props", ".targets", ".json"];
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj"
    };

    public McpBuildResult Execute(BootstrapOptions options_)
    {
        string repoPath = Path.GetFullPath(options_.RepoPath);
        string projectPath = Path.Combine(repoPath, options_.McpProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(projectPath))
        {
            return new McpBuildResult("Failed", $"Missing {options_.McpProjectRelativePath}.", GetDllPath(repoPath, options_));
        }

        string dllPath = GetDllPath(repoPath, options_);
        if (!options_.Strict && this.IsOutputCurrent(projectPath, dllPath))
        {
            return new McpBuildResult("SkippedCurrent", "Release MCP build skipped because the output DLL is current.", dllPath);
        }

        ProcessResult build = new ProcessRunner().Run("dotnet", ["build", options_.McpProjectRelativePath, "-c", "Release"], repoPath);
        if (build.Success)
        {
            return new McpBuildResult("Built", "Release MCP build passed.", dllPath, null, build);
        }

        string message = McpBuildFailureDiagnostics.IsLockedDllFailure(build)
            ? McpBuildFailureDiagnostics.LockedDllMessage
            : GetProcessMessage(build);
        string? hint = McpBuildFailureDiagnostics.IsLockedDllFailure(build)
            ? McpBuildFailureDiagnostics.LockedDllHint
            : null;
        return new McpBuildResult("Failed", message, dllPath, hint, build);
    }

    public static McpBuildResult CreateLockedSmokePassed(McpBuildResult failed_)
    {
        ArgumentNullException.ThrowIfNull(failed_);
        return failed_ with
        {
            State = "SkippedLockedSmokePassed",
            Message = McpBuildFailureDiagnostics.LockedDllMessage + " JSON-RPC smoke test passed, so rebuild was skipped outside strict validation."
        };
    }

    public static string GetDllPath(string repoPath_, BootstrapOptions options_)
    {
        string repoPath = Path.GetFullPath(repoPath_);
        string projectPath = Path.Combine(repoPath, options_.McpProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? repoPath;
        return Path.Combine(projectDirectory, "bin", "Release", options_.TargetFramework, options_.McpAssemblyName + ".dll");
    }

    private bool IsOutputCurrent(string projectPath_, string dllPath_)
    {
        if (!File.Exists(dllPath_))
        {
            return false;
        }

        try
        {
            DateTime outputWriteTime = File.GetLastWriteTimeUtc(dllPath_);
            foreach (string input in this.EnumerateInputs(projectPath_))
            {
                if (File.GetLastWriteTimeUtc(input) > outputWriteTime)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private IEnumerable<string> EnumerateInputs(string projectPath_)
    {
        yield return projectPath_;

        string projectDirectory = Path.GetDirectoryName(projectPath_) ?? Directory.GetCurrentDirectory();
        Stack<string> pending = new();
        pending.Push(projectDirectory);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (string directory in directories)
            {
                if (IgnoredDirectories.Contains(Path.GetFileName(directory)))
                {
                    continue;
                }

                pending.Push(directory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                if (InputExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static string GetProcessMessage(ProcessResult process_)
    {
        if (process_.Success)
        {
            return $"Exit code {process_.ExitCode}.";
        }

        string output = string.Join(" ", $"{process_.StandardOutput}{Environment.NewLine}{process_.StandardError}"
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .TakeLast(4)
            .Select(line_ => line_.Trim()));
        return string.IsNullOrWhiteSpace(output) ? $"Exit code {process_.ExitCode}." : $"Exit code {process_.ExitCode}. {output}";
    }
}
