using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class ScriptShellResolverTests
{
    private readonly TestEnvironmentAccessor _env = new();

    [Fact]
    public void Resolve_ExplicitPowershell_ReturnsPowershell()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("powershell");
        Assert.Equal(ScriptShell.PowerShell, result);
    }

    [Fact]
    public void Resolve_ExplicitBash_ReturnsBash()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("bash");
        Assert.Equal(ScriptShell.Bash, result);
    }

    [Fact]
    public void Resolve_ExplicitAuto_ReturnsAuto()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("auto");
        Assert.Equal(ScriptShell.Auto, result);
    }

    [Fact]
    public void Resolve_ExplicitValueOverridesEnvironment()
    {
        _env.SetVariable("AIREPO_SHELL", "bash");
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("powershell");
        Assert.Equal(ScriptShell.PowerShell, result);
    }

    [Fact]
    public void Resolve_EnvPowershell_ReturnsPowershell()
    {
        _env.SetVariable("AIREPO_SHELL", "powershell");
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve(null);
        Assert.Equal(ScriptShell.PowerShell, result);
    }

    [Fact]
    public void Resolve_EnvBash_ReturnsBash()
    {
        _env.SetVariable("AIREPO_SHELL", "bash");
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve(null);
        Assert.Equal(ScriptShell.Bash, result);
    }

    [Fact]
    public void Resolve_EnvAuto_ReturnsAuto()
    {
        _env.SetVariable("AIREPO_SHELL", "auto");
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve(null);
        Assert.Equal(ScriptShell.Auto, result);
    }

    [Fact]
    public void Resolve_NoExplicitAndNoEnvironment_ReturnsDefaultPowershell()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve(null);
        Assert.Equal(ScriptShell.PowerShell, result);
    }

    [Fact]
    public void Resolve_BlankExplicitAllowsEnvironmentLookup()
    {
        _env.SetVariable("AIREPO_SHELL", "bash");
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("   ");
        Assert.Equal(ScriptShell.Bash, result);
    }

    [Fact]
    public void Resolve_BlankEnvironment_ReturnsDefaultPowershell()
    {
        _env.SetVariable("AIREPO_SHELL", "   ");
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve(null);
        Assert.Equal(ScriptShell.PowerShell, result);
    }

    [Fact]
    public void Resolve_CaseInsensitivePowershell_ReturnsPowershell()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("PoWeRsHeLl");
        Assert.Equal(ScriptShell.PowerShell, result);
    }

    [Fact]
    public void Resolve_CaseInsensitiveBash_ReturnsBash()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("BASH");
        Assert.Equal(ScriptShell.Bash, result);
    }

    [Fact]
    public void Resolve_CaseInsensitiveAuto_ReturnsAuto()
    {
        var resolver = new ScriptShellResolver(_env);
        ScriptShell result = resolver.Resolve("AuTo");
        Assert.Equal(ScriptShell.Auto, result);
    }

    [Fact]
    public void Resolve_InvalidExplicitValue_ThrowsArgumentException()
    {
        var resolver = new ScriptShellResolver(_env);
        Assert.Throws<ArgumentException>(() => resolver.Resolve("invalid_shell"));
    }

    [Fact]
    public void Resolve_InvalidEnvironmentValue_ThrowsArgumentException()
    {
        _env.SetVariable("AIREPO_SHELL", "invalid_shell");
        var resolver = new ScriptShellResolver(_env);
        Assert.Throws<ArgumentException>(() => resolver.Resolve(null));
    }

    [Fact]
    public void Resolve_PwshIsRejected()
    {
        var resolver = new ScriptShellResolver(_env);
        Assert.Throws<ArgumentException>(() => resolver.Resolve("pwsh"));
    }

    [Fact]
    public void Resolve_ZshIsRejected()
    {
        var resolver = new ScriptShellResolver(_env);
        Assert.Throws<ArgumentException>(() => resolver.Resolve("zsh"));
    }

    [Fact]
    public void TestInstancesDoNotShareEnvironmentState()
    {
        var env1 = new TestEnvironmentAccessor();
        var env2 = new TestEnvironmentAccessor();

        env1.SetVariable("AIREPO_SHELL", "bash");
        env2.SetVariable("AIREPO_SHELL", "auto");

        var resolver1 = new ScriptShellResolver(env1);
        var resolver2 = new ScriptShellResolver(env2);

        Assert.Equal(ScriptShell.Bash, resolver1.Resolve(null));
        Assert.Equal(ScriptShell.Auto, resolver2.Resolve(null));
    }

    private sealed class TestEnvironmentAccessor : IEnvironmentAccessor
    {
        private readonly Dictionary<string, string?> _variables = new(StringComparer.OrdinalIgnoreCase);

        public void SetVariable(string name, string? value)
        {
            _variables[name] = value;
        }

        public string? GetEnvironmentVariable(string name)
        {
            return _variables.TryGetValue(name, out string? value) ? value : null;
        }
    }
}
