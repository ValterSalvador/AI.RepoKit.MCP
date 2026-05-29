using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class ProcessRunner
{
    private static readonly Regex SensitiveLinePattern = new(
        "(secret|password|passwd|pwd|token|apikey|api_key|connectionstring|connection string|privatekey|private key|credential)\\s*[:=]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ProcessResult Run(string fileName_, IEnumerable<string> arguments_, string workingDirectory_)
    {
        string[] arguments = arguments_.ToArray();

        try
        {
            using Process process = new();
            process.StartInfo.FileName = fileName_;
            process.StartInfo.WorkingDirectory = workingDirectory_;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            foreach (string argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(fileName_, JoinArguments(arguments), workingDirectory_, process.ExitCode, Redact(standardOutput), Redact(standardError));
        }
        catch (Exception exception)
        {
            return new ProcessResult(fileName_, JoinArguments(arguments), workingDirectory_, 1, string.Empty, Redact(exception.Message));
        }
    }

    public static string Redact(string value_)
    {
        if (string.IsNullOrEmpty(value_))
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (string line in value_.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            builder.AppendLine(SensitiveLinePattern.IsMatch(line) ? "[redacted sensitive line]" : line);
        }

        return builder.ToString().TrimEnd();
    }

    private static string JoinArguments(IEnumerable<string> arguments_)
    {
        return string.Join(" ", arguments_.Select(argument_ => argument_.Contains(' ', StringComparison.Ordinal) ? $"\"{argument_}\"" : argument_));
    }
}
