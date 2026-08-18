using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class ScriptShellResolver
{
    private readonly IEnvironmentAccessor _environmentAccessor;

    public ScriptShellResolver(IEnvironmentAccessor environmentAccessor)
    {
        _environmentAccessor = environmentAccessor ?? throw new ArgumentNullException(nameof(environmentAccessor));
    }

    public ScriptShell Resolve(string? explicitValue = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return ParseShell(explicitValue.Trim(), isExplicit: true);
        }

        string? envValue = _environmentAccessor.GetEnvironmentVariable("AIREPO_SHELL");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return ParseShell(envValue.Trim(), isExplicit: false);
        }

        return ScriptShell.PowerShell;
    }

    private static ScriptShell ParseShell(string rawValue, bool isExplicit)
    {
        if (string.Equals(rawValue, "powershell", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptShell.PowerShell;
        }

        if (string.Equals(rawValue, "bash", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptShell.Bash;
        }

        if (string.Equals(rawValue, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return ScriptShell.Auto;
        }

        string sourceName = isExplicit ? "Explicit script shell" : "AIREPO_SHELL environment variable";
        throw new ArgumentException(
            $"{sourceName} value '{rawValue}' is not supported. Supported values: powershell, bash, auto.",
            isExplicit ? "explicitValue" : "AIREPO_SHELL");
    }
}
