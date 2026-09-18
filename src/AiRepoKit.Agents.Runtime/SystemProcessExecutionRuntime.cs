using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("AiRepoKit.Agents.Runtime.Tests")]
[assembly: InternalsVisibleTo("AiRepoKit.Agents.Antigravity.Tests")]
[assembly: InternalsVisibleTo("AiRepoKit.Agents.Codex.Tests")]

namespace AiRepoKit.Agents.Runtime;

public sealed class SystemProcessExecutionRuntime : IProcessExecutionRuntime
{
    private readonly TimeProvider _timeProvider;

    public SystemProcessExecutionRuntime()
        : this(TimeProvider.System)
    {
    }

    internal SystemProcessExecutionRuntime(TimeProvider timeProvider_)
    {
        this._timeProvider = timeProvider_ ?? TimeProvider.System;
    }

    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        cancellationToken_.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo =
            CreateProcessStartInfo(request_);

        using Process process = new()
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Start();

        if (request_.Timeout is null)
        {
            Task<string> stdoutTask =
                process.StandardOutput.ReadToEndAsync(cancellationToken_);
            Task<string> stderrTask =
                process.StandardError.ReadToEndAsync(cancellationToken_);

            try
            {
                await process.WaitForExitAsync(cancellationToken_).ConfigureAwait(false);

                string stdout =
                    await stdoutTask.ConfigureAwait(false);
                string stderr =
                    await stderrTask.ConfigureAwait(false);

                return new ProcessExecutionResult(
                    process.ExitCode,
                    stdout,
                    stderr);
            }
            catch (OperationCanceledException)
            {
                await TerminateProcessTreeAsync(process).ConfigureAwait(false);
                throw;
            }
        }

        using CancellationTokenSource timeoutCts = new();
        using CancellationTokenSource effectiveCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken_, timeoutCts.Token);
        CancellationToken effectiveToken = effectiveCts.Token;

        using ITimer timer = this._timeProvider.CreateTimer(
            _ => timeoutCts.Cancel(),
            null,
            request_.Timeout.Value,
            Timeout.InfiniteTimeSpan);

        Task<string> stdoutTaskWithTimeout =
            process.StandardOutput.ReadToEndAsync(effectiveToken);
        Task<string> stderrTaskWithTimeout =
            process.StandardError.ReadToEndAsync(effectiveToken);

        try
        {
            await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);

            string stdout =
                await stdoutTaskWithTimeout.ConfigureAwait(false);
            string stderr =
                await stderrTaskWithTimeout.ConfigureAwait(false);

            return new ProcessExecutionResult(
                process.ExitCode,
                stdout,
                stderr);
        }
        catch (OperationCanceledException ex)
        {
            await TerminateProcessTreeAsync(process).ConfigureAwait(false);

            throw RuntimeTimeoutCoordinator.ClassifyException(
                ex,
                cancellationToken_,
                timeoutCts.IsCancellationRequested,
                request_.Timeout);
        }
    }

    internal static ProcessStartInfo CreateProcessStartInfo(
        ProcessExecutionRequest request_)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        ProcessStartInfo startInfo = new()
        {
            FileName = request_.Executable,
            WorkingDirectory = request_.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in request_.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal static async Task TerminateProcessTreeAsync(
        Process process_)
    {
        ArgumentNullException.ThrowIfNull(
            process_,
            nameof(process_));

        try
        {
            if (process_.HasExited)
            {
                return;
            }

            process_.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process has already exited between state check and Kill.
            return;
        }
        catch (Exception)
        {
            // Best effort on process tree termination.
        }

        await process_.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
