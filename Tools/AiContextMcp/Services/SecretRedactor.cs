using System.Text.RegularExpressions;

namespace AiRepo.ContextMcp.Services;

public sealed partial class SecretRedactor
{
    public string Redact(string value_)
    {
        string result = value_;
        foreach (Regex regex in SensitivePatterns())
        {
            result = regex.Replace(result, match_ => $"{match_.Groups[1].Value}<redacted>");
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
}
