namespace AiRepoKit.Agents.Runtime.Tests;

using System.Diagnostics;
using System.Text;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class SystemProcessExecutionRuntimeTests
{
    private static readonly string ValidWorkingDirectory =
        Path.GetFullPath(AppContext.BaseDirectory);

    [Fact]
    public void CreateProcessStartInfo_NullRequest_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => SystemProcessExecutionRuntime.CreateProcessStartInfo(null!));
    }

    [Fact]
    public void CreateProcessStartInfo_SetsExactFlagsAndConfigurations()
    {
        ProcessExecutionRequest request = new(
            "my_executable",
            ["arg1", "arg2 with spaces", "--opt=val"],
            ValidWorkingDirectory);

        ProcessStartInfo startInfo =
            SystemProcessExecutionRuntime.CreateProcessStartInfo(request);

        Assert.Equal("my_executable", startInfo.FileName);
        Assert.Equal(ValidWorkingDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.True(startInfo.CreateNoWindow);
        Assert.Equal(Encoding.UTF8, startInfo.StandardOutputEncoding);
        Assert.Equal(Encoding.UTF8, startInfo.StandardErrorEncoding);

        Assert.Equal(3, startInfo.ArgumentList.Count);
        Assert.Equal("arg1", startInfo.ArgumentList[0]);
        Assert.Equal("arg2 with spaces", startInfo.ArgumentList[1]);
        Assert.Equal("--opt=val", startInfo.ArgumentList[2]);
    }

    [Fact]
    public async Task ExecuteAsync_NullRequest_ThrowsArgumentNullException()
    {
        SystemProcessExecutionRuntime runtime = new();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runtime.ExecuteAsync(null!));
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceledToken_ThrowsOperationCanceledException_WithoutStartingProcess()
    {
        SystemProcessExecutionRuntime runtime = new();
        ProcessExecutionRequest request = new(
            "non_existent_executable_12345",
            [],
            ValidWorkingDirectory);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(request, cts.Token));
    }

    [Fact]
    public async Task TerminateProcessTreeAsync_NullProcess_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => SystemProcessExecutionRuntime.TerminateProcessTreeAsync(null!));
    }

    [Fact]
    public async Task TerminateProcessTreeAsync_AlreadyExitedProcess_ReturnsPromptly()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.ProcessPath ?? "dotnet",
            Arguments = "--version",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        Assert.True(process.HasExited);

        await SystemProcessExecutionRuntime.TerminateProcessTreeAsync(process);
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesHarmlessLocalProcess_ReturnsSuccessResult()
    {
        SystemProcessExecutionRuntime runtime = new();
        ProcessExecutionRequest request = new(
            "dotnet",
            ["--version"],
            ValidWorkingDirectory);

        ProcessExecutionResult result = await runtime.ExecuteAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
        Assert.Empty(result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_WithTimeout_CompletesNormally_WhenProcessFinishesBeforeTimeout()
    {
        SystemProcessExecutionRuntime runtime = new();
        ProcessExecutionRequest request = new(
            "dotnet",
            ["--version"],
            ValidWorkingDirectory,
            TimeSpan.FromSeconds(30));

        ProcessExecutionResult result = await runtime.ExecuteAsync(request);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutExpires_ThrowsTimeoutException_AndTerminatesProcess()
    {
        SystemProcessExecutionRuntime runtime = new();
        var (executable, args) = GetLongRunningProcessSpec();
        ProcessExecutionRequest request = new(
            executable,
            args,
            ValidWorkingDirectory,
            TimeSpan.FromMilliseconds(150));

        Stopwatch stopwatch = Stopwatch.StartNew();
        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
            () => runtime.ExecuteAsync(request));

        stopwatch.Stop();
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Timeout should have terminated promptly.");
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_ThrowsOperationCanceledException_AndTerminatesProcess()
    {
        SystemProcessExecutionRuntime runtime = new();
        var (executable, args) = GetLongRunningProcessSpec();
        ProcessExecutionRequest request = new(
            executable,
            args,
            ValidWorkingDirectory,
            TimeSpan.FromSeconds(30));

        using CancellationTokenSource cts = new();
        Task<ProcessExecutionResult> executeTask = runtime.ExecuteAsync(request, cts.Token);

        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => executeTask);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellationAndTimeoutRace_CallerCancellationWinsPrecedence()
    {
        SystemProcessExecutionRuntime runtime = new();
        var (executable, args) = GetLongRunningProcessSpec();
        ProcessExecutionRequest request = new(
            executable,
            args,
            ValidWorkingDirectory,
            TimeSpan.FromMilliseconds(100));

        using CancellationTokenSource cts = new();
        cts.Cancel(); // Pre-canceled caller token

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(request, cts.Token));
    }

    private static (string Executable, IReadOnlyList<string> Arguments) GetLongRunningProcessSpec()
    {
        if (OperatingSystem.IsWindows())
        {
            return ("ping.exe", ["127.0.0.1", "-n", "10"]);
        }
        else
        {
            return ("sleep", ["10"]);
        }
    }
}
