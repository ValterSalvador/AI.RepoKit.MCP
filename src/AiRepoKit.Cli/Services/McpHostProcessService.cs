namespace AiRepoKit.Cli.Services;

public sealed record McpHostProcessStopResult(
    bool Supported,
    bool Success,
    int CandidateCount,
    IReadOnlyList<int> StoppedProcessIds,
    string Message);

public sealed class McpHostProcessService
{
    private static readonly HashSet<string> CompatibleHostNames =
        new(
            StringComparer.OrdinalIgnoreCase)
        {
            "dotnet",
            "dotnet.exe",
            "AiRepo.ContextMcp",
            "AiRepo.ContextMcp.exe"
        };

    private readonly IPlatformAccessor _platformAccessor;
    private readonly IMcpHostProcessAccessor _processAccessor;

    public McpHostProcessService()
        : this(
            new PlatformAccessor(),
            new WindowsMcpHostProcessAccessor())
    {
    }

    public McpHostProcessService(
        IPlatformAccessor platformAccessor_,
        IMcpHostProcessAccessor processAccessor_)
    {
        this._platformAccessor =
            platformAccessor_
            ?? throw new ArgumentNullException(
                nameof(platformAccessor_));

        this._processAccessor =
            processAccessor_
            ?? throw new ArgumentNullException(
                nameof(processAccessor_));
    }

    public McpHostProcessStopResult StopStaleHostsForRepo(
        string repoPath_)
    {
        if (!this._platformAccessor.IsWindows)
        {
            return new McpHostProcessStopResult(
                false,
                false,
                0,
                [],
                "--stop-stale-mcp-hosts currently supports Windows only. " +
                "Close MCP clients and rerun.");
        }

        string repoPath =
            Path.GetFullPath(
                repoPath_);

        string normalizedRepoRoot =
            NormalizePath(
                repoPath);

        string normalizedMcpRoot =
            NormalizePath(
                Path.Combine(
                    repoPath,
                    "Tools",
                    "AiContextMcp"));

        IReadOnlyList<McpHostProcessInfo> processes =
            this._processAccessor.GetProcesses();

        List<McpHostProcessInfo> candidates =
            processes
                .Where(
                    process_ =>
                        IsCandidate(
                            process_,
                            normalizedRepoRoot,
                            normalizedMcpRoot))
                .ToList();

        List<int> stopped = [];

        foreach (McpHostProcessInfo candidate in candidates)
        {
            if (candidate.ProcessId <= 0 ||
                candidate.ProcessId == Environment.ProcessId)
            {
                continue;
            }

            if (this._processAccessor.TryStopProcess(
                candidate.ProcessId))
            {
                stopped.Add(
                    candidate.ProcessId);
            }
        }

        string message =
            candidates.Count == 0
                ? "No stale MCP host processes for this repository were found."
                : $"Stopped {stopped.Count} of {candidates.Count} " +
                  $"stale MCP host process(es) for this repository. " +
                  $"PIDs: {string.Join(", ", stopped)}.";

        return new McpHostProcessStopResult(
            true,
            stopped.Count == candidates.Count,
            candidates.Count,
            stopped,
            ProcessRunner.Redact(
                message));
    }

    private static bool IsCandidate(
        McpHostProcessInfo process_,
        string normalizedRepoRoot_,
        string normalizedMcpRoot_)
    {
        if (string.IsNullOrWhiteSpace(
                process_.CommandLine) ||
            !CompatibleHostNames.Contains(
                process_.Name ?? string.Empty))
        {
            return false;
        }

        string commandLine =
            NormalizeCommandLine(
                process_.CommandLine);

        bool hasMcpMarker =
            commandLine.Contains(
                "airepo.contextmcp",
                StringComparison.OrdinalIgnoreCase)
            ||
            commandLine.Contains(
                "ai_repo_context",
                StringComparison.OrdinalIgnoreCase);

        bool hasRepoMarker =
            IsCommandLinePathTokenMatch(
                commandLine,
                normalizedRepoRoot_)
            ||
            IsCommandLinePathTokenMatch(
                commandLine,
                normalizedMcpRoot_);

        return hasMcpMarker &&
            hasRepoMarker;
    }

    private static bool IsCommandLinePathTokenMatch(
        string commandLine_,
        string normalizedPath_)
    {
        if (string.IsNullOrWhiteSpace(
                commandLine_) ||
            string.IsNullOrWhiteSpace(
                normalizedPath_))
        {
            return false;
        }

        string commandLine =
            NormalizeCommandLine(
                commandLine_);

        string normalizedPath =
            NormalizePath(
                normalizedPath_);

        int startIndex = 0;

        while (startIndex < commandLine.Length)
        {
            int matchIndex =
                commandLine.IndexOf(
                    normalizedPath,
                    startIndex,
                    StringComparison.OrdinalIgnoreCase);

            if (matchIndex < 0)
            {
                return false;
            }

            int afterIndex =
                matchIndex +
                normalizedPath.Length;

            if (HasPathTokenStartBoundary(
                    commandLine,
                    matchIndex) &&
                HasPathTokenEndBoundary(
                    commandLine,
                    afterIndex))
            {
                return true;
            }

            startIndex =
                matchIndex + 1;
        }

        return false;
    }

    private static bool HasPathTokenStartBoundary(
        string value_,
        int index_)
    {
        if (index_ == 0)
        {
            return true;
        }

        char previous =
            value_[index_ - 1];

        return char.IsWhiteSpace(
                previous)
            || previous == '"'
            || previous == '\''
            || previous == '=';
    }

    private static bool HasPathTokenEndBoundary(
        string value_,
        int index_)
    {
        if (index_ >= value_.Length)
        {
            return true;
        }

        char next =
            value_[index_];

        return next == '/'
            || char.IsWhiteSpace(
                next)
            || next == '"'
            || next == '\''
            || next == ';';
    }

    private static string NormalizePath(
        string path_)
    {
        return Path.GetFullPath(
                path_)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            .Replace(
                '\\',
                '/')
            .ToLowerInvariant();
    }

    private static string NormalizeCommandLine(
        string commandLine_)
    {
        return commandLine_
            .Replace(
                '\\',
                '/')
            .ToLowerInvariant();
    }
}
