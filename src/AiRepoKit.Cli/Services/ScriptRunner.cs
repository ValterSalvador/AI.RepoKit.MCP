using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class ScriptRunner : IScriptRunner
{
    private readonly IExecutableResolver _executableResolver;
    private readonly IProcessRunner _processRunner;

    public ScriptRunner(IExecutableResolver executableResolver, IProcessRunner processRunner)
    {
        _executableResolver = executableResolver ?? throw new ArgumentNullException(nameof(executableResolver));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public ProcessResult RunScript(
        ScriptDefinition definition,
        ScriptShell requestedShell,
        string repositoryRoot,
        IEnumerable<string>? scriptArguments = null,
        string? workingDirectory = null)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("Repository root must be provided.", nameof(repositoryRoot));
        }

        ResolvedScriptExecutable executable = _executableResolver.Resolve(requestedShell);

        string? relativePath = executable.Kind switch
        {
            ScriptExecutableKind.WindowsPowerShell or ScriptExecutableKind.PowerShellCore => definition.PowerShellRelativePath,
            ScriptExecutableKind.Bash => definition.BashRelativePath,
            _ => throw new InvalidOperationException($"Unsupported script executable kind: {executable.Kind}")
        };

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            string shellName = executable.Kind switch
            {
                ScriptExecutableKind.Bash => "Bash",
                _ => "PowerShell"
            };
            throw new InvalidOperationException($"Script '{definition.Name}' does not have a {shellName} implementation.");
        }

        if (IsAbsolutePath(relativePath))
        {
            throw new InvalidOperationException($"Script path '{relativePath}' is an absolute path and cannot be executed as a repository-relative path.");
        }

        string normalizedRelative = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        string fullRoot = Path.GetFullPath(repositoryRoot);
        string fullScriptPath = Path.GetFullPath(Path.Combine(fullRoot, normalizedRelative));

        if (!IsInsideRoot(fullRoot, fullScriptPath))
        {
            throw new InvalidOperationException($"Script path '{relativePath}' escapes repository root.");
        }

        if (!File.Exists(fullScriptPath))
        {
            throw new FileNotFoundException($"Script file not found: '{fullScriptPath}'.", fullScriptPath);
        }

        List<string> args = new();
        switch (executable.Kind)
        {
            case ScriptExecutableKind.WindowsPowerShell:
                args.Add("-NoProfile");
                args.Add("-NonInteractive");
                args.Add("-ExecutionPolicy");
                args.Add("Bypass");
                args.Add("-File");
                args.Add(fullScriptPath);
                break;

            case ScriptExecutableKind.PowerShellCore:
                args.Add("-NoProfile");
                args.Add("-NonInteractive");
                args.Add("-File");
                args.Add(fullScriptPath);
                break;

            case ScriptExecutableKind.Bash:
                args.Add(fullScriptPath);
                break;
        }

        if (scriptArguments != null)
        {
            args.AddRange(scriptArguments);
        }

        string targetWorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : fullRoot;

        return _processRunner.Run(executable.FileName, args, targetWorkingDirectory);
    }

    public static bool IsInsideRoot(string rootPath, string targetPath)
    {
        string fullRoot = Path.GetFullPath(rootPath);
        string fullTarget = Path.GetFullPath(targetPath);

        string relative = Path.GetRelativePath(fullRoot, fullTarget);

        if (string.Equals(relative, ".", StringComparison.Ordinal))
        {
            return true;
        }

        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        if (string.Equals(relative, "..", StringComparison.Ordinal))
        {
            return false;
        }

        if (relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string trimmed = path.Trim();

        if (Path.IsPathRooted(trimmed))
        {
            return true;
        }

        if (trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
        {
            return true;
        }

        return false;
    }
}
