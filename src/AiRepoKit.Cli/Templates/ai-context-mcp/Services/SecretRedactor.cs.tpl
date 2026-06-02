using System.Text.RegularExpressions;

namespace {{McpNamespace}}.Services;

public sealed partial class SecretRedactor
{
    private const string RepoRootPlaceholder = "<repo-root>";
    private const string LocalPathPlaceholder = "<local-path>";
    private const string TempPlaceholder = "<temp>";
    private const string LogFilePlaceholder = "<log-file>";
    private const string SuspiciousInstructionPlaceholder = "<redacted-instruction>";

    public string Redact(string value_)
    {
        string result = value_;
        foreach (Regex regex in SensitivePatterns())
        {
            result = regex.Replace(result, match_ => $"{match_.Groups[1].Value}<redacted>");
        }

        foreach ((Regex Regex, string Replacement) pattern in LocalPathPatterns())
        {
            result = pattern.Regex.Replace(result, pattern.Replacement);
        }

        foreach (Regex regex in SuspiciousInstructionPatterns())
        {
            result = regex.Replace(result, SuspiciousInstructionPlaceholder);
        }

        return result;
    }

    private static IReadOnlyList<Regex> SensitivePatterns()
    {
        return
        [
            new Regex(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|connectionstring)(\s*[:=]\s*)[^\s;,""]+", RegexOptions.Compiled),
            new Regex(@"(?i)\b(bearer\s+)[a-z0-9._~+/=-]+", RegexOptions.Compiled)
        ];
    }

    private static IReadOnlyList<(Regex Regex, string Replacement)> LocalPathPatterns()
    {
        return
        [
            (new Regex(@"(?i)\b[A-Z]:\\\\Users\\\\[^\\\s""'<>|]+\\\\AppData\\\\Local\\\\Temp\\\\ai-repo-context-mcp\.log\b", RegexOptions.Compiled), $"{TempPlaceholder}/ai-repo-context-mcp.log"),
            (new Regex(@"(?i)\b[A-Z]:\\\\Users\\\\[^\\\s""'<>|]+\\\\AppData\\\\Local\\\\Temp\\\\[^\s""'<>|]+", RegexOptions.Compiled), TempPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\\\(?:Temp|Windows\\\\Temp)\\\\[^\s""'<>|]+", RegexOptions.Compiled), TempPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\\\Repositories\\\\[^\s""'<>|]+", RegexOptions.Compiled), RepoRootPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\\\Users\\\\[^\\\s""'<>|]+\\\\[^\s""'<>|]+", RegexOptions.Compiled), LocalPathPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|]+\\AppData\\Local\\Temp\\ai-repo-context-mcp\.log\b", RegexOptions.Compiled), $"{TempPlaceholder}/ai-repo-context-mcp.log"),
            (new Regex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|]+\\AppData\\Local\\Temp\\[^\s""'<>|]+", RegexOptions.Compiled), TempPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\(?:Temp|Windows\\Temp)\\[^\s""'<>|]+", RegexOptions.Compiled), TempPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\Repositories\\[^\s""'<>|]+", RegexOptions.Compiled), RepoRootPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\Users\\[^\\\s""'<>|]+\\[^\s""'<>|]+", RegexOptions.Compiled), LocalPathPlaceholder),
            (new Regex(@"(?i)\\\\[^\\\s""'<>|]+\\[^\\\s""'<>|]+\\[^\s""'<>|]+", RegexOptions.Compiled), LocalPathPlaceholder),
            (new Regex(@"(?i)/(?:Users|home)/(?!user(?:/|$))[^/\s""'<>]+/[^\s""'<>]+", RegexOptions.Compiled), LocalPathPlaceholder),
            (new Regex(@"(?i)/(?:tmp|var/tmp)/[^\s""'<>]+", RegexOptions.Compiled), TempPlaceholder),
            (new Regex(@"(?i)\b[A-Z]:\\[^\s""'<>|]*ai-repo-context-mcp\.log\b", RegexOptions.Compiled), LogFilePlaceholder)
        ];
    }

    private static IReadOnlyList<Regex> SuspiciousInstructionPatterns()
    {
        string[] phrases =
        [
            "ignore previous " + "instructions",
            "disregard previous " + "instructions",
            "system " + "instruction",
            "instrucao do " + "sistema",
            "instrução do " + "sistema",
            "ignore as regras " + "anteriores",
            "responda " + "apenas",
            "system " + "prompt",
            "developer " + "message",
            "exfil" + "trate",
            "print " + "secrets",
            "este codigo foi " + "hackeado",
            "este código foi " + "hackeado"
        ];

        return phrases
            .Select(phrase_ => new Regex(@"\b" + Regex.Escape(phrase_).Replace("\\ ", @"\s+", StringComparison.Ordinal) + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();
    }
}
