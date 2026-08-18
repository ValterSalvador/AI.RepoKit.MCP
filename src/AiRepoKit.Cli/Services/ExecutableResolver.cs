using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class ExecutableResolver : IExecutableResolver
{
    private readonly IExecutableLocator _locator;
    private readonly IPlatformAccessor _platformAccessor;

    public ExecutableResolver(IExecutableLocator locator, IPlatformAccessor platformAccessor)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _platformAccessor = platformAccessor ?? throw new ArgumentNullException(nameof(platformAccessor));
    }

    public ResolvedScriptExecutable Resolve(ScriptShell shell)
    {
        return shell switch
        {
            ScriptShell.PowerShell => ResolvePowerShell(),
            ScriptShell.Bash => ResolveBash(),
            ScriptShell.Auto => ResolveAuto(),
            _ => throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported ScriptShell value.")
        };
    }

    private ResolvedScriptExecutable ResolvePowerShell()
    {
        if (_platformAccessor.IsWindows)
        {
            string? powershell = _locator.Find("powershell.exe");
            if (!string.IsNullOrEmpty(powershell))
            {
                return new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, powershell);
            }

            string? pwsh = _locator.Find("pwsh.exe");
            if (!string.IsNullOrEmpty(pwsh))
            {
                return new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.PowerShellCore, pwsh);
            }

            throw new InvalidOperationException("Explicit PowerShell requested, but neither 'powershell.exe' nor 'pwsh.exe' was found on PATH.");
        }

        string? nativePwsh = _locator.Find("pwsh");
        if (!string.IsNullOrEmpty(nativePwsh))
        {
            return new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.PowerShellCore, nativePwsh);
        }

        throw new InvalidOperationException("Explicit PowerShell requested, but native 'pwsh' was not found on PATH.");
    }

    private ResolvedScriptExecutable ResolveBash()
    {
        if (_platformAccessor.IsWindows)
        {
            string? bash = _locator.Find("bash.exe");
            if (!string.IsNullOrEmpty(bash))
            {
                return new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, bash);
            }

            throw new InvalidOperationException("Explicit Bash requested, but 'bash.exe' was not found on PATH.");
        }

        string? nativeBash = _locator.Find("bash");
        if (!string.IsNullOrEmpty(nativeBash))
        {
            return new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, nativeBash);
        }

        throw new InvalidOperationException("Explicit Bash requested, but 'bash' was not found on PATH.");
    }

    private ResolvedScriptExecutable ResolveAuto()
    {
        if (_platformAccessor.IsWindows)
        {
            string? powershell = _locator.Find("powershell.exe");
            if (!string.IsNullOrEmpty(powershell))
            {
                return new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, powershell);
            }

            string? pwsh = _locator.Find("pwsh.exe");
            if (!string.IsNullOrEmpty(pwsh))
            {
                return new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.PowerShellCore, pwsh);
            }

            string? bash = _locator.Find("bash.exe");
            if (!string.IsNullOrEmpty(bash))
            {
                return new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, bash);
            }

            throw new InvalidOperationException("Auto shell resolution failed. Tried candidates: powershell.exe, pwsh.exe, bash.exe.");
        }

        string? nativeBash = _locator.Find("bash");
        if (!string.IsNullOrEmpty(nativeBash))
        {
            return new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, nativeBash);
        }

        string? nativePwsh = _locator.Find("pwsh");
        if (!string.IsNullOrEmpty(nativePwsh))
        {
            return new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.PowerShellCore, nativePwsh);
        }

        throw new InvalidOperationException("Auto shell resolution failed. Tried candidates: bash, pwsh.");
    }
}
