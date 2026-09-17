using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("AiRepoKit.Agents.Runtime.Tests")]
[assembly: InternalsVisibleTo("AiRepoKit.Agents.Antigravity.Tests")]
[assembly: InternalsVisibleTo("AiRepoKit.Agents.Codex.Tests")]

namespace AiRepoKit.Agents.Runtime;

public sealed class SystemProcessExecutionRuntime : IProcessExecutionRuntime
{
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
