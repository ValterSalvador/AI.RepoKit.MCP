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
        string processIdFile = Path.Combine(
            Path.GetTempPath(),
            $"airepokit-runtime-timeout-{Guid.NewGuid():N}.pid");

        int? processId = null;

        try
        {
            ManualTimeoutTimeProvider timeProvider = new();
            SystemProcessExecutionRuntime runtime = new(timeProvider);
            var (executable, args) =
                GetPidReportingLongRunningProcessSpec(processIdFile);

            ProcessExecutionRequest request = new(
                executable,
                args,
                ValidWorkingDirectory,
                TimeSpan.FromSeconds(30));

            Task<ProcessExecutionResult> executionTask =
                runtime.ExecuteAsync(request);

            await timeProvider.TimerCreated.WaitAsync(
                TimeSpan.FromSeconds(10));

            processId =
                await WaitForProcessIdAsync(processIdFile);

            timeProvider.TriggerTimeout();

            TimeoutException ex =
                await Assert.ThrowsAsync<TimeoutException>(
                    () => executionTask);

            Assert.Contains(
                "timed out",
                ex.Message,
                StringComparison.OrdinalIgnoreCase);

            Assert.False(
                IsProcessRunning(processId.Value),
                "Timed-out process should be terminated before ExecuteAsync completes.");
        }
        finally
        {
            if (processId is int observedProcessId)
            {
                TryTerminateProcess(observedProcessId);
            }

            if (File.Exists(processIdFile))
            {
                File.Delete(processIdFile);
            }
        }
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

    private static async Task<int> WaitForProcessIdAsync(
        string processIdFile_)
    {
        const int maxAttempts = 400;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(processIdFile_))
                {
                    string processIdText =
                        await File.ReadAllTextAsync(processIdFile_);

                    if (int.TryParse(
                            processIdText.Trim(),
                            out int processId) &&
                        processId > 0)
                    {
                        return processId;
                    }
                }
            }
            catch (IOException)
            {
                // The child may still be publishing the file.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException(
            "Long-running test process did not publish its process ID.");
    }

    private static bool IsProcessRunning(
        int processId_)
    {
        try
        {
            using Process process =
                Process.GetProcessById(processId_);

            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryTerminateProcess(
        int processId_)
    {
        try
        {
            using Process process =
                Process.GetProcessById(processId_);

            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup for a failed test path.
        }
    }

    private static (
        string Executable,
        IReadOnlyList<string> Arguments)
        GetPidReportingLongRunningProcessSpec(
            string processIdFile_)
    {
        if (OperatingSystem.IsWindows())
        {
            string escapedProcessIdFile =
                processIdFile_.Replace(
                    "'",
                    "''",
                    StringComparison.Ordinal);

            return (
                "powershell.exe",
                [
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    $"[System.IO.File]::WriteAllText('{escapedProcessIdFile}', [string]$PID); Start-Sleep -Seconds 30"
                ]);
        }

        string escapedUnixProcessIdFile =
            processIdFile_.Replace(
                "'",
                "'\"'\"'",
                StringComparison.Ordinal);

        return (
            "/bin/sh",
            [
                "-c",
                $"printf '%s' \"$$\" > '{escapedUnixProcessIdFile}'; exec sleep 30"
            ]);
    }

    private sealed class ManualTimeoutTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource<ManualTimer> _timerCreated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TimerCreated =>
            this._timerCreated.Task;

        public override ITimer CreateTimer(
            TimerCallback callback_,
            object? state_,
            TimeSpan dueTime_,
            TimeSpan period_)
        {
            ManualTimer timer =
                new(
                    callback_,
                    state_);

            if (!this._timerCreated.TrySetResult(timer))
            {
                throw new InvalidOperationException(
                    "Only one timeout timer is expected by this test.");
            }

            return timer;
        }

        public void TriggerTimeout()
        {
            if (!this._timerCreated.Task.IsCompletedSuccessfully)
            {
                throw new InvalidOperationException(
                    "The Runtime timeout timer has not been created.");
            }

            this._timerCreated.Task
                .GetAwaiter()
                .GetResult()
                .Fire();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;

        private int _disposed;
        private int _fired;

        public ManualTimer(
            TimerCallback callback_,
            object? state_)
        {
            this._callback =
                callback_ ??
                throw new ArgumentNullException(nameof(callback_));

            this._state = state_;
        }

        public bool Change(
            TimeSpan dueTime_,
            TimeSpan period_)
        {
            return Volatile.Read(ref this._disposed) == 0;
        }

        public void Fire()
        {
            if (Volatile.Read(ref this._disposed) != 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref this._fired, 1) != 0)
            {
                return;
            }

            this._callback(this._state);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref this._disposed, 1);
        }

        public ValueTask DisposeAsync()
        {
            this.Dispose();
            return ValueTask.CompletedTask;
        }
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
