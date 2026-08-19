using System.Diagnostics;
using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services.DotNetSdk;

/// <summary>
/// Executes only the dotnet SDK discovery commands used by generated
/// SDK artifacts.
///
/// Successful stdout is intentionally preserved because the historical
/// artifact contract includes the SDK installation paths returned by
/// dotnet --list-sdks.
///
/// stderr and exception messages remain redacted.
/// </summary>
public sealed class DotNetSdkProcessRunner :
    IProcessRunner
{
    public ProcessResult Run(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        string[] args =
            arguments.ToArray();

        bool supported =
            string.Equals(
                fileName,
                "dotnet",
                StringComparison.OrdinalIgnoreCase) &&
            args.Length == 1 &&
            (
                string.Equals(
                    args[0],
                    "--version",
                    StringComparison.Ordinal) ||
                string.Equals(
                    args[0],
                    "--list-sdks",
                    StringComparison.Ordinal)
            );

        if (!supported)
        {
            return new ProcessResult(
                fileName,
                JoinArguments(args),
                workingDirectory,
                1,
                string.Empty,
                "Unsupported dotnet SDK probe command.");
        }

        try
        {
            using Process process =
                new();

            process.StartInfo.FileName =
                fileName;

            process.StartInfo.WorkingDirectory =
                workingDirectory;

            process.StartInfo.RedirectStandardOutput =
                true;

            process.StartInfo.RedirectStandardError =
                true;

            process.StartInfo.UseShellExecute =
                false;

            process.StartInfo.CreateNoWindow =
                true;

            foreach (string argument in args)
            {
                process.StartInfo.ArgumentList.Add(
                    argument);
            }

            process.Start();

            string standardOutput =
                process.StandardOutput.ReadToEnd();

            string standardError =
                process.StandardError.ReadToEnd();

            process.WaitForExit();

            return new ProcessResult(
                fileName,
                JoinArguments(args),
                workingDirectory,
                process.ExitCode,
                standardOutput,
                ProcessRunner.Redact(
                    standardError));
        }
        catch (Exception exception)
        {
            return new ProcessResult(
                fileName,
                JoinArguments(args),
                workingDirectory,
                1,
                string.Empty,
                ProcessRunner.Redact(
                    exception.Message));
        }
    }

    private static string JoinArguments(
        IEnumerable<string> arguments)
    {
        return string.Join(
            " ",
            arguments.Select(
                argument =>
                    argument.Contains(
                        ' ',
                        StringComparison.Ordinal)
                        ? $"\"{argument}\""
                        : argument));
    }
}
