using System.Diagnostics;
using System.Text;

namespace AiRepoKit.Agents.Antigravity;

internal sealed class AntigravityProcessRunner : IAntigravityProcessRunner
{
    internal static ProcessStartInfo CreateProcessStartInfo(
        AntigravityProcessInvocation invocation_)
    {
        ArgumentNullException.ThrowIfNull(
            invocation_,
            nameof(invocation_));

        ProcessStartInfo startInfo = new()
        {
            FileName = invocation_.Executable,
            WorkingDirectory = invocation_.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in invocation_.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public async Task<AntigravityProcessResult> RunAsync(
        AntigravityProcessInvocation invocation_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            invocation_,
            nameof(invocation_));

        cancellationToken_.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo =
            CreateProcessStartInfo(invocation_);

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

            return new AntigravityProcessResult(
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
