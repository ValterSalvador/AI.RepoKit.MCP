using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AiRepoKit.Cli.Services;

public sealed record McpHostProcessStopResult(
    bool Supported,
    bool Success,
    int CandidateCount,
    IReadOnlyList<int> StoppedProcessIds,
    string Message);

public sealed class McpHostProcessService
{
    private static readonly HashSet<string> CompatibleHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "dotnet",
        "dotnet.exe",
        "AiRepo.ContextMcp",
        "AiRepo.ContextMcp.exe"
    };

    public McpHostProcessStopResult StopStaleHostsForRepo(string repoPath_)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new McpHostProcessStopResult(
                false,
                false,
                0,
                [],
                "--stop-stale-mcp-hosts currently supports Windows only. Close MCP clients and rerun.");
        }

        string repoPath = Path.GetFullPath(repoPath_);
        string normalizedRepoRoot = NormalizePath(repoPath);
        string normalizedMcpRoot = NormalizePath(Path.Combine(repoPath, "Tools", "AiContextMcp"));
        IReadOnlyList<Win32ProcessInfo> processes = GetWin32Processes();
        List<Win32ProcessInfo> candidates = processes
            .Where(process_ => IsCandidate(process_, normalizedRepoRoot, normalizedMcpRoot))
            .ToList();

        List<int> stopped = [];
        foreach (Win32ProcessInfo candidate in candidates)
        {
            if (candidate.ProcessId <= 0 || candidate.ProcessId == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                using Process process = Process.GetProcessById(candidate.ProcessId);
                process.Kill();
                process.WaitForExit(5000);
                stopped.Add(candidate.ProcessId);
            }
            catch
            {
            }
        }

        string message = candidates.Count == 0
            ? "No stale MCP host processes for this repository were found."
            : $"Stopped {stopped.Count} of {candidates.Count} stale MCP host process(es) for this repository. PIDs: {string.Join(", ", stopped)}.";
        return new McpHostProcessStopResult(true, stopped.Count == candidates.Count, candidates.Count, stopped, ProcessRunner.Redact(message));
    }

    private static bool IsCandidate(Win32ProcessInfo process_, string normalizedRepoRoot_, string normalizedMcpRoot_)
    {
        if (string.IsNullOrWhiteSpace(process_.CommandLine) || !CompatibleHostNames.Contains(process_.Name ?? string.Empty))
        {
            return false;
        }

        string commandLine = NormalizeCommandLine(process_.CommandLine);
        bool hasMcpMarker = commandLine.Contains("airepo.contextmcp", StringComparison.OrdinalIgnoreCase)
            || commandLine.Contains("ai_repo_context", StringComparison.OrdinalIgnoreCase);
        bool hasRepoMarker = IsCommandLinePathTokenMatch(commandLine, normalizedRepoRoot_)
            || IsCommandLinePathTokenMatch(commandLine, normalizedMcpRoot_);
        return hasMcpMarker && hasRepoMarker;
    }

    private static bool IsCommandLinePathTokenMatch(string commandLine_, string normalizedPath_)
    {
        if (string.IsNullOrWhiteSpace(commandLine_) || string.IsNullOrWhiteSpace(normalizedPath_))
        {
            return false;
        }

        string commandLine = NormalizeCommandLine(commandLine_);
        string normalizedPath = NormalizePath(normalizedPath_);
        int startIndex = 0;
        while (startIndex < commandLine.Length)
        {
            int matchIndex = commandLine.IndexOf(normalizedPath, startIndex, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                return false;
            }

            int afterIndex = matchIndex + normalizedPath.Length;
            if (HasPathTokenStartBoundary(commandLine, matchIndex)
                && HasPathTokenEndBoundary(commandLine, afterIndex))
            {
                return true;
            }

            startIndex = matchIndex + 1;
        }

        return false;
    }

    private static bool HasPathTokenStartBoundary(string value_, int index_)
    {
        if (index_ == 0)
        {
            return true;
        }

        char previous = value_[index_ - 1];
        return char.IsWhiteSpace(previous)
            || previous == '"'
            || previous == '\''
            || previous == '=';
    }

    private static bool HasPathTokenEndBoundary(string value_, int index_)
    {
        if (index_ >= value_.Length)
        {
            return true;
        }

        char next = value_[index_];
        return next == '/'
            || char.IsWhiteSpace(next)
            || next == '"'
            || next == '\''
            || next == ';';
    }

    private static IReadOnlyList<Win32ProcessInfo> GetWin32Processes()
    {
        string command = "Get-CimInstance Win32_Process | Select-Object ProcessId,Name,CommandLine | ConvertTo-Json -Compress";
        using Process process = new();
        process.StartInfo.FileName = "powershell";
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(10000) || process.ExitCode != 0 || string.IsNullOrWhiteSpace(standardOutput))
        {
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(standardOutput);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement.EnumerateArray().Select(ParseProcess).Where(process_ => process_ is not null).Cast<Win32ProcessInfo>().ToArray();
            }

            Win32ProcessInfo? single = ParseProcess(document.RootElement);
            return single is null ? [] : [single];
        }
        catch
        {
            return [];
        }
    }

    private static Win32ProcessInfo? ParseProcess(JsonElement element_)
    {
        if (!element_.TryGetProperty("ProcessId", out JsonElement processIdElement)
            || !processIdElement.TryGetInt32(out int processId))
        {
            return null;
        }

        string? name = element_.TryGetProperty("Name", out JsonElement nameElement) && nameElement.ValueKind == JsonValueKind.String
            ? nameElement.GetString()
            : null;
        string? commandLine = element_.TryGetProperty("CommandLine", out JsonElement commandLineElement) && commandLineElement.ValueKind == JsonValueKind.String
            ? commandLineElement.GetString()
            : null;
        return new Win32ProcessInfo(processId, name ?? string.Empty, commandLine ?? string.Empty);
    }

    private static string NormalizePath(string path_)
    {
        return Path.GetFullPath(path_)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/')
            .ToLowerInvariant();
    }

    private static string NormalizeCommandLine(string commandLine_)
    {
        return commandLine_.Replace('\\', '/').ToLowerInvariant();
    }

    private sealed record Win32ProcessInfo(int ProcessId, string Name, string CommandLine);
}
