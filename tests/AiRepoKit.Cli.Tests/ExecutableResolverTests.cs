using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class ExecutableResolverTests
{
    [Fact]
    public void Resolve_Windows_ExplicitPowerShell_PrefersPowershellExe()
    {
        var platform = new FakePlatformAccessor(isWindows: true);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["powershell.exe"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            ["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe"
        });

        var resolver = new ExecutableResolver(locator, platform);
        ResolvedScriptExecutable resolved = resolver.Resolve(ScriptShell.PowerShell);

        Assert.Equal(ScriptShell.PowerShell, resolved.Shell);
        Assert.Equal(ScriptExecutableKind.WindowsPowerShell, resolved.Kind);
        Assert.Equal(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", resolved.FileName);
        Assert.Equal(new[] { "powershell.exe" }, locator.ProbedCandidates);
    }

    [Fact]
    public void Resolve_Windows_ExplicitPowerShell_FallsBackToPwshExe()
    {
        var platform = new FakePlatformAccessor(isWindows: true);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe"
        });

        var resolver = new ExecutableResolver(locator, platform);
        ResolvedScriptExecutable resolved = resolver.Resolve(ScriptShell.PowerShell);

        Assert.Equal(ScriptShell.PowerShell, resolved.Shell);
        Assert.Equal(ScriptExecutableKind.PowerShellCore, resolved.Kind);
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", resolved.FileName);
        Assert.Equal(new[] { "powershell.exe", "pwsh.exe" }, locator.ProbedCandidates);
    }

    [Fact]
    public void Resolve_Windows_ExplicitPowerShell_NeverFallsBackToBash()
    {
        var platform = new FakePlatformAccessor(isWindows: true);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["bash.exe"] = @"C:\Program Files\Git\bin\bash.exe"
        });

        var resolver = new ExecutableResolver(locator, platform);

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ScriptShell.PowerShell));
        Assert.Contains("powershell.exe", ex.Message);
        Assert.Contains("pwsh.exe", ex.Message);
        Assert.DoesNotContain("bash.exe", locator.ProbedCandidates);
    }

    [Fact]
    public void Resolve_Linux_ExplicitPowerShell_ResolvesNativePwsh()
    {
        var platform = new FakePlatformAccessor(isWindows: false);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["pwsh"] = "/usr/bin/pwsh"
        });

        var resolver = new ExecutableResolver(locator, platform);
        ResolvedScriptExecutable resolved = resolver.Resolve(ScriptShell.PowerShell);

        Assert.Equal(ScriptShell.PowerShell, resolved.Shell);
        Assert.Equal(ScriptExecutableKind.PowerShellCore, resolved.Kind);
        Assert.Equal("/usr/bin/pwsh", resolved.FileName);
    }

    [Fact]
    public void Resolve_Linux_ExplicitPowerShell_DoesNotProbePowershellExe()
    {
        var platform = new FakePlatformAccessor(isWindows: false);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["pwsh"] = "/usr/bin/pwsh"
        });

        var resolver = new ExecutableResolver(locator, platform);
        resolver.Resolve(ScriptShell.PowerShell);

        Assert.DoesNotContain("powershell.exe", locator.ProbedCandidates);
    }

    [Fact]
    public void Resolve_Linux_ExplicitPowerShell_DoesNotProbePwshExe()
    {
        var platform = new FakePlatformAccessor(isWindows: false);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["pwsh"] = "/usr/bin/pwsh"
        });

        var resolver = new ExecutableResolver(locator, platform);
        resolver.Resolve(ScriptShell.PowerShell);

        Assert.DoesNotContain("pwsh.exe", locator.ProbedCandidates);
    }

    [Fact]
    public void Resolve_Linux_ExplicitPowerShell_NeverFallsBackToBash()
    {
        var platform = new FakePlatformAccessor(isWindows: false);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["bash"] = "/bin/bash"
        });

        var resolver = new ExecutableResolver(locator, platform);

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ScriptShell.PowerShell));
        Assert.Contains("pwsh", ex.Message);
        Assert.DoesNotContain("bash", locator.ProbedCandidates);
    }

    [Fact]
    public void Resolve_Windows_ExplicitBash_ResolvesBashExe()
    {
        var platform = new FakePlatformAccessor(isWindows: true);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["bash.exe"] = @"C:\Program Files\Git\bin\bash.exe"
        });

        var resolver = new ExecutableResolver(locator, platform);
        ResolvedScriptExecutable resolved = resolver.Resolve(ScriptShell.Bash);

        Assert.Equal(ScriptShell.Bash, resolved.Shell);
        Assert.Equal(ScriptExecutableKind.Bash, resolved.Kind);
        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", resolved.FileName);
    }

    [Fact]
    public void Resolve_Linux_ExplicitBash_ResolvesBash()
    {
        var platform = new FakePlatformAccessor(isWindows: false);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["bash"] = "/bin/bash"
        });

        var resolver = new ExecutableResolver(locator, platform);
        ResolvedScriptExecutable resolved = resolver.Resolve(ScriptShell.Bash);

        Assert.Equal(ScriptShell.Bash, resolved.Shell);
        Assert.Equal(ScriptExecutableKind.Bash, resolved.Kind);
        Assert.Equal("/bin/bash", resolved.FileName);
    }

    [Fact]
    public void Resolve_ExplicitBash_NeverFallsBackToPowerShell()
    {
        var platform = new FakePlatformAccessor(isWindows: true);
        var locator = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["powershell.exe"] = @"C:\Windows\System32\powershell.exe",
            ["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe"
        });

        var resolver = new ExecutableResolver(locator, platform);

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ScriptShell.Bash));
        Assert.Contains("bash.exe", ex.Message);
    }

    [Fact]
    public void Resolve_Windows_Auto_PrecedenceOrder()
    {
        // 1. powershell.exe available
        var platform = new FakePlatformAccessor(isWindows: true);
        var locator1 = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["powershell.exe"] = @"C:\Windows\powershell.exe",
            ["pwsh.exe"] = @"C:\Program Files\pwsh.exe",
            ["bash.exe"] = @"C:\Git\bash.exe"
        });
        var resolver1 = new ExecutableResolver(locator1, platform);
        var res1 = resolver1.Resolve(ScriptShell.Auto);
        Assert.Equal(ScriptExecutableKind.WindowsPowerShell, res1.Kind);

        // 2. pwsh.exe available (powershell.exe missing)
        var locator2 = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["pwsh.exe"] = @"C:\Program Files\pwsh.exe",
            ["bash.exe"] = @"C:\Git\bash.exe"
        });
        var resolver2 = new ExecutableResolver(locator2, platform);
        var res2 = resolver2.Resolve(ScriptShell.Auto);
        Assert.Equal(ScriptExecutableKind.PowerShellCore, res2.Kind);

        // 3. bash.exe available (powershell and pwsh missing)
        var locator3 = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["bash.exe"] = @"C:\Git\bash.exe"
        });
        var resolver3 = new ExecutableResolver(locator3, platform);
        var res3 = resolver3.Resolve(ScriptShell.Auto);
        Assert.Equal(ScriptExecutableKind.Bash, res3.Kind);
    }

    [Fact]
    public void Resolve_Linux_Auto_PrecedenceOrder()
    {
        // 1. bash available
        var platform = new FakePlatformAccessor(isWindows: false);
        var locator1 = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["bash"] = "/bin/bash",
            ["pwsh"] = "/usr/bin/pwsh"
        });
        var resolver1 = new ExecutableResolver(locator1, platform);
        var res1 = resolver1.Resolve(ScriptShell.Auto);
        Assert.Equal(ScriptExecutableKind.Bash, res1.Kind);

        // 2. pwsh available (bash missing)
        var locator2 = new FakeExecutableLocator(new Dictionary<string, string>
        {
            ["pwsh"] = "/usr/bin/pwsh"
        });
        var resolver2 = new ExecutableResolver(locator2, platform);
        var res2 = resolver2.Resolve(ScriptShell.Auto);
        Assert.Equal(ScriptExecutableKind.PowerShellCore, res2.Kind);
    }

    [Fact]
    public void Resolve_Auto_FailsClearlyWhenNothingAvailable()
    {
        var platformWin = new FakePlatformAccessor(isWindows: true);
        var locatorWin = new FakeExecutableLocator(new Dictionary<string, string>());
        var resolverWin = new ExecutableResolver(locatorWin, platformWin);

        var exWin = Assert.Throws<InvalidOperationException>(() => resolverWin.Resolve(ScriptShell.Auto));
        Assert.Contains("powershell.exe", exWin.Message);
        Assert.Contains("pwsh.exe", exWin.Message);
        Assert.Contains("bash.exe", exWin.Message);

        var platformLinux = new FakePlatformAccessor(isWindows: false);
        var locatorLinux = new FakeExecutableLocator(new Dictionary<string, string>());
        var resolverLinux = new ExecutableResolver(locatorLinux, platformLinux);

        var exLinux = Assert.Throws<InvalidOperationException>(() => resolverLinux.Resolve(ScriptShell.Auto));
        Assert.Contains("bash", exLinux.Message);
        Assert.Contains("pwsh", exLinux.Message);
    }

    [Fact]
    public void PathExecutableLocator_FindsExecutableFromPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "airepokit_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string fakeExePath = Path.Combine(tempDir, "mytool.exe");
            File.WriteAllText(fakeExePath, "dummy");

            var env = new TestEnvironmentAccessor();
            env.SetVariable("PATH", tempDir);

            var locator = new PathExecutableLocator(env);
            string? found = locator.Find("mytool.exe");

            Assert.Equal(fakeExePath, found);
            Assert.Null(locator.Find("nonexistent.exe"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private sealed class FakePlatformAccessor : IPlatformAccessor
    {
        public bool IsWindows { get; }

        public FakePlatformAccessor(bool isWindows)
        {
            IsWindows = isWindows;
        }
    }

    private sealed class FakeExecutableLocator : IExecutableLocator
    {
        private readonly Dictionary<string, string> _executables;

        public List<string> ProbedCandidates { get; } = new();

        public FakeExecutableLocator(Dictionary<string, string> executables)
        {
            _executables = executables;
        }

        public string? Find(string executableName)
        {
            ProbedCandidates.Add(executableName);
            return _executables.TryGetValue(executableName, out string? path) ? path : null;
        }
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
