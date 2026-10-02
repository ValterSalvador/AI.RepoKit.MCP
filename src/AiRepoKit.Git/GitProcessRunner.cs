namespace AiRepoKit.Git;

using System.Diagnostics;
using System.Text;

internal interface IGitProcessRunner
{
    Task<GitProcessResult> RunAsync(
        string workingDirectory_,
        IReadOnlyList<string> arguments_,
        TimeSpan timeout_,
        CancellationToken cancellationToken_);
}

internal sealed record GitProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class GitProcessRunner : IGitProcessRunner
{
    public async Task<GitProcessResult> RunAsync(
        string workingDirectory_,
        IReadOnlyList<string> arguments_,
        TimeSpan timeout_,
        CancellationToken cancellationToken_)
    {
        ArgumentNullException.ThrowIfNull(
            workingDirectory_);

        ArgumentNullException.ThrowIfNull(
            arguments_);

        cancellationToken_.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo =
            new()
            {
                FileName = "git",
                WorkingDirectory = workingDirectory_,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

        foreach (string argument in arguments_)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using Process process =
            new()
            {
                StartInfo = startInfo
            };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Git process could not be started.");
        }

        Task<string> standardOutputTask =
            process.StandardOutput.ReadToEndAsync();

        Task<string> standardErrorTask =
            process.StandardError.ReadToEndAsync();

        using CancellationTokenSource timeoutCancellation =
            new();

        timeoutCancellation.CancelAfter(
            timeout_);

        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken_,
                timeoutCancellation.Token);

        try
        {
            await process.WaitForExitAsync(
                linkedCancellation.Token);
        }
        catch (OperationCanceledException)
            when (cancellationToken_.IsCancellationRequested)
        {
            KillProcessTree(
                process);

            await DrainAsync(
                standardOutputTask,
                standardErrorTask);

            throw new OperationCanceledException(
                cancellationToken_);
        }
        catch (OperationCanceledException)
            when (timeoutCancellation.IsCancellationRequested)
        {
            KillProcessTree(
                process);

            await DrainAsync(
                standardOutputTask,
                standardErrorTask);

            throw new TimeoutException(
                "Git command timed out.");
        }

        string standardOutput =
            await standardOutputTask;

        string standardError =
            await standardErrorTask;

        return new GitProcessResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private static void KillProcessTree(
        Process process_)
    {
        try
        {
            if (!process_.HasExited)
            {
                process_.Kill(
                    entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task DrainAsync(
        Task<string> standardOutputTask_,
        Task<string> standardErrorTask_)
    {
        try
        {
            await Task.WhenAll(
                standardOutputTask_,
                standardErrorTask_);
        }
        catch
        {
        }
    }
}
