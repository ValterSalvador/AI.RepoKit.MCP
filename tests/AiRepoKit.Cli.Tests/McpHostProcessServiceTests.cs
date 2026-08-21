using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class McpHostProcessServiceTests
{
    [Fact]
    public void StopStaleHostsForRepo_NonWindows_IsExplicitlyUnsupported()
    {
        FakeProcessAccessor processAccessor =
            new([]);

        McpHostProcessService service =
            CreateService(
                false,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                CreateRepoRoot());

        Assert.False(
            result.Supported);

        Assert.False(
            result.Success);

        Assert.Equal(
            0,
            result.CandidateCount);

        Assert.Empty(
            result.StoppedProcessIds);

        Assert.Contains(
            "supports Windows only",
            result.Message);

        Assert.Equal(
            0,
            processAccessor.GetProcessesCallCount);

        Assert.Empty(
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_NoCandidates_Succeeds()
    {
        FakeProcessAccessor processAccessor =
            new([]);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                CreateRepoRoot());

        Assert.True(
            result.Supported);

        Assert.True(
            result.Success);

        Assert.Equal(
            0,
            result.CandidateCount);

        Assert.Empty(
            result.StoppedProcessIds);

        Assert.Contains(
            "No stale MCP host processes",
            result.Message);

        Assert.Equal(
            1,
            processAccessor.GetProcessesCallCount);

        Assert.Empty(
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_MatchingRepoHost_IsStopped()
    {
        string repoRoot =
            CreateRepoRoot();

        int processId = 41001;

        FakeProcessAccessor processAccessor =
            new(
            [
                new McpHostProcessInfo(
                    processId,
                    "dotnet.exe",
                    CreateMcpCommandLine(
                        repoRoot))
            ]);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                repoRoot);

        Assert.True(
            result.Supported);

        Assert.True(
            result.Success);

        Assert.Equal(
            1,
            result.CandidateCount);

        Assert.Equal(
            [processId],
            result.StoppedProcessIds);

        Assert.Equal(
            [processId],
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_MatchingMcpRoot_IsStopped()
    {
        string repoRoot =
            CreateRepoRoot();

        string mcpRoot =
            Path.Combine(
                repoRoot,
                "Tools",
                "AiContextMcp");

        int processId = 41002;

        FakeProcessAccessor processAccessor =
            new(
            [
                new McpHostProcessInfo(
                    processId,
                    "AiRepo.ContextMcp.exe",
                    $"AiRepo.ContextMcp.exe \"{mcpRoot}\"")
            ]);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                repoRoot);

        Assert.True(
            result.Success);

        Assert.Equal(
            1,
            result.CandidateCount);

        Assert.Equal(
            [processId],
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_PathPrefixCollision_IsIgnored()
    {
        string repoRoot =
            CreateRepoRoot();

        string sibling =
            repoRoot + "-other";

        FakeProcessAccessor processAccessor =
            new(
            [
                new McpHostProcessInfo(
                    41003,
                    "dotnet",
                    CreateMcpCommandLine(
                        sibling))
            ]);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                repoRoot);

        Assert.True(
            result.Success);

        Assert.Equal(
            0,
            result.CandidateCount);

        Assert.Empty(
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_IncompatibleProcessesAreIgnored()
    {
        string repoRoot =
            CreateRepoRoot();

        FakeProcessAccessor processAccessor =
            new(
            [
                new McpHostProcessInfo(
                    41004,
                    "node.exe",
                    CreateMcpCommandLine(
                        repoRoot)),
                new McpHostProcessInfo(
                    41005,
                    "dotnet.exe",
                    $"dotnet \"{repoRoot}/something.dll\""),
                new McpHostProcessInfo(
                    41006,
                    "dotnet.exe",
                    "dotnet AiRepo.ContextMcp.dll")
            ]);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                repoRoot);

        Assert.True(
            result.Success);

        Assert.Equal(
            0,
            result.CandidateCount);

        Assert.Empty(
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_StopFailure_IsReported()
    {
        string repoRoot =
            CreateRepoRoot();

        int processId = 41007;

        FakeProcessAccessor processAccessor =
            new(
            [
                new McpHostProcessInfo(
                    processId,
                    "dotnet",
                    CreateMcpCommandLine(
                        repoRoot))
            ]);

        processAccessor.SetStopResult(
            processId,
            false);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                repoRoot);

        Assert.True(
            result.Supported);

        Assert.False(
            result.Success);

        Assert.Equal(
            1,
            result.CandidateCount);

        Assert.Empty(
            result.StoppedProcessIds);

        Assert.Equal(
            [processId],
            processAccessor.StopAttempts);
    }

    [Fact]
    public void StopStaleHostsForRepo_Windows_CurrentProcess_IsNeverStopped()
    {
        string repoRoot =
            CreateRepoRoot();

        FakeProcessAccessor processAccessor =
            new(
            [
                new McpHostProcessInfo(
                    Environment.ProcessId,
                    "dotnet",
                    CreateMcpCommandLine(
                        repoRoot))
            ]);

        McpHostProcessService service =
            CreateService(
                true,
                processAccessor);

        McpHostProcessStopResult result =
            service.StopStaleHostsForRepo(
                repoRoot);

        Assert.True(
            result.Supported);

        Assert.False(
            result.Success);

        Assert.Equal(
            1,
            result.CandidateCount);

        Assert.Empty(
            result.StoppedProcessIds);

        Assert.Empty(
            processAccessor.StopAttempts);
    }

    private static McpHostProcessService CreateService(
        bool isWindows_,
        FakeProcessAccessor processAccessor_)
    {
        return new McpHostProcessService(
            new FakePlatformAccessor(
                isWindows_),
            processAccessor_);
    }

    private static string CreateRepoRoot()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "airepo-p04-tests",
            Guid.NewGuid().ToString(
                "N"));
    }

    private static string CreateMcpCommandLine(
        string repoRoot_)
    {
        string assembly =
            Path.Combine(
                repoRoot_,
                "Tools",
                "AiContextMcp",
                "bin",
                "Release",
                "net10.0",
                "AiRepo.ContextMcp.dll");

        return
            $"dotnet \"{assembly}\" " +
            "--server ai_repo_context";
    }

    private sealed class FakePlatformAccessor(
        bool isWindows_) :
        IPlatformAccessor
    {
        public bool IsWindows { get; } =
            isWindows_;
    }

    private sealed class FakeProcessAccessor :
        IMcpHostProcessAccessor
    {
        private readonly IReadOnlyList<McpHostProcessInfo> _processes;

        private readonly Dictionary<int, bool> _stopResults =
            [];

        public FakeProcessAccessor(
            IReadOnlyList<McpHostProcessInfo> processes_)
        {
            this._processes =
                processes_;
        }

        public int GetProcessesCallCount { get; private set; }

        public List<int> StopAttempts { get; } =
            [];

        public IReadOnlyList<McpHostProcessInfo> GetProcesses()
        {
            this.GetProcessesCallCount++;

            return this._processes;
        }

        public bool TryStopProcess(
            int processId_)
        {
            this.StopAttempts.Add(
                processId_);

            return !this._stopResults.TryGetValue(
                    processId_,
                    out bool result)
                || result;
        }

        public void SetStopResult(
            int processId_,
            bool result_)
        {
            this._stopResults[processId_] =
                result_;
        }
    }
}
