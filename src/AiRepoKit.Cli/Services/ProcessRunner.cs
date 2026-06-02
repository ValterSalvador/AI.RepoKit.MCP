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

    private static readonly IReadOnlyList<(Regex Regex, string Replacement)> LocalPathPatterns =
    [
        (new Regex(@"(?i)\b[A-Z]:\\\\Users\\\\[^\\\s""'<>|]+\\\\AppData\\\\Local\\\\Temp\\\\ai-repo-context-mcp\.log\b", RegexOptions.Compiled), "<temp>/ai-repo-context-mcp.log"),
        (new Regex(@"(?i)\b[A-Z]:\\\\Users\\\\[^\\\s""'<>|]+\\\\AppData\\\\Local\\\\Temp\\\\[^\s""'<>|]+", RegexOptions.Compiled), "<temp>"),
        (new Regex(@"(?i)\b[A-Z]:\\\\(?:Temp|Windows\\\\Temp)\\\\[^\s""'<>|]+", RegexOptions.Compiled), "<temp>"),
        (new Regex(@"(?i)\b[A-Z]:\\\\Repositories\\\\[^\s""'<>|]+", RegexOptions.Compiled), "<repo-root>"),
        (new Regex(@"(?i)\b[A-Z]:\\\\Users\\\\[^\\\s""'<>|]+\\\\[^\s""'<>|]+", RegexOptions.Compiled), "<local-path>"),
        (new Regex(@"(?i)\b[A-Z]:\\u005[Cc]\\u005[Cc]Users\\u005[Cc]\\u005[Cc][^\\\s""'<>|]+\\u005[Cc]\\u005[Cc]AppData\\u005[Cc]\\u005[Cc]Local\\u005[Cc]\\u005[Cc]Temp\\u005[Cc]\\u005[Cc]ai-repo-context-mcp\.log\b", RegexOptions.Compiled), "<temp>/ai-repo-context-mcp.log"),
        (new Regex(@"(?i)\b[A-Z]:\\u005[Cc]\\u005[Cc]Repositories\\u005[Cc]\\u005[Cc][^\s""'<>|]+", RegexOptions.Compiled), "<repo-root>"),
        (new Regex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|]+\\AppData\\Local\\Temp\\ai-repo-context-mcp\.log\b", RegexOptions.Compiled), "<temp>/ai-repo-context-mcp.log"),
        (new Regex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|]+\\AppData\\Local\\Temp\\[^\s""'<>|]+", RegexOptions.Compiled), "<temp>"),
        (new Regex(@"(?i)\b[A-Z]:\\(?:Temp|Windows\\Temp)\\[^\s""'<>|]+", RegexOptions.Compiled), "<temp>"),
        (new Regex(@"(?i)\b[A-Z]:\\Repositories\\[^\s""'<>|]+", RegexOptions.Compiled), "<repo-root>"),
        (new Regex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|]+\\[^\s""'<>|]+", RegexOptions.Compiled), "<local-path>"),
        (new Regex(@"(?i)\\\\[^\\\s""'<>|]+\\[^\\\s""'<>|]+\\[^\s""'<>|]+", RegexOptions.Compiled), "<local-path>"),
        (new Regex(@"(?i)/(?:Users|home)/(?!user(?:/|$))[^/\s""'<>]+/[^\s""'<>]+", RegexOptions.Compiled), "<local-path>"),
        (new Regex(@"(?i)/(?:tmp|var/tmp)/[^\s""'<>]+", RegexOptions.Compiled), "<temp>"),
        (new Regex(@"(?i)\b[A-Z]:\\[^\s""'<>|]*ai-repo-context-mcp\.log\b", RegexOptions.Compiled), "<log-file>")
    ];

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
            string redacted = SensitiveLinePattern.IsMatch(line) ? "[redacted sensitive line]" : line;
            foreach ((Regex Regex, string Replacement) pattern in LocalPathPatterns)
            {
                redacted = pattern.Regex.Replace(redacted, pattern.Replacement);
            }

            builder.AppendLine(redacted);
        }

        return builder.ToString().TrimEnd();
    }

    private static string JoinArguments(IEnumerable<string> arguments_)
    {
        return string.Join(" ", arguments_.Select(argument_ => argument_.Contains(' ', StringComparison.Ordinal) ? $"\"{argument_}\"" : argument_));
    }
}
