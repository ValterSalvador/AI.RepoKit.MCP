using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class ScriptRunnerTests
{
    [Fact]
    public void RunScript_WindowsPowerShell_UsesExpectedArgumentOrdering()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptRelPath = @"scripts\test.ps1";
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: scriptRelPath, BashRelativePath: @"scripts\test.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, @"C:\Windows\powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            ProcessResult result = scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir, new[] { "-Arg1", "val 1" });

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(@"C:\Windows\powershell.exe", processRunner.LastFileName);
            Assert.Equal(tempDir, processRunner.LastWorkingDirectory);
            Assert.Equal(
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptFullPath, "-Arg1", "val 1" },
                processRunner.LastArguments);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_Pwsh_UsesExpectedArgumentOrdering()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptRelPath = @"scripts\test.ps1";
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: scriptRelPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.PowerShellCore, @"C:\pwsh.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir, new[] { "foo", "bar" });

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(@"C:\pwsh.exe", processRunner.LastFileName);
            Assert.Equal(
                new[] { "-NoProfile", "-NonInteractive", "-File", scriptFullPath, "foo", "bar" },
                processRunner.LastArguments);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_Bash_UsesScriptPathAndArgumentsDirectly()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptRelPath = @"scripts\test.sh";
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.sh");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "#!/bin/bash");

            var def = new ScriptDefinition("test", BashRelativePath: scriptRelPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "/bin/bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.Bash, tempDir, new[] { "--opt", "hello world" });

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal("/bin/bash", processRunner.LastFileName);
            Assert.Equal(
                new[] { scriptFullPath, "--opt", "hello world" },
                processRunner.LastArguments);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_Bash_DoesNotRequireScriptExecutableBit()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptRelPath = "script.sh";
            string scriptFullPath = Path.Combine(tempDir, scriptRelPath);
            File.WriteAllText(scriptFullPath, "echo hello");

            var def = new ScriptDefinition("test", BashRelativePath: scriptRelPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "/bin/bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.Bash, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal("/bin/bash", processRunner.LastFileName);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![0]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_ScriptArgumentsRemainIndividualArguments()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptRelPath = "test.ps1";
            string scriptFullPath = Path.Combine(tempDir, scriptRelPath);
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: scriptRelPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.PowerShellCore, "pwsh"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir, new[] { "arg 1", "arg 2", "arg with spaces" });

            Assert.Equal(
                new[] { "-NoProfile", "-NonInteractive", "-File", scriptFullPath, "arg 1", "arg 2", "arg with spaces" },
                processRunner.LastArguments);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_WorkingDirectoryPassedCorrectly()
    {
        string tempDir = CreateTempRepo();
        string customWorkDir = Path.Combine(tempDir, "work");
        Directory.CreateDirectory(customWorkDir);
        try
        {
            string scriptRelPath = "test.ps1";
            string scriptFullPath = Path.Combine(tempDir, scriptRelPath);
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: scriptRelPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir, workingDirectory: customWorkDir);

            Assert.Equal(Path.GetFullPath(customWorkDir), processRunner.LastWorkingDirectory);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_PowerShellDefinitionSelectsPs1()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string ps1Rel = "script.ps1";
            string shRel = "script.sh";
            File.WriteAllText(Path.Combine(tempDir, ps1Rel), "# ps1");
            File.WriteAllText(Path.Combine(tempDir, shRel), "# sh");

            var def = new ScriptDefinition("test", PowerShellRelativePath: ps1Rel, BashRelativePath: shRel);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir);

            Assert.Contains(Path.Combine(tempDir, ps1Rel), processRunner.LastArguments!);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_BashDefinitionSelectsSh()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string ps1Rel = "script.ps1";
            string shRel = "script.sh";
            File.WriteAllText(Path.Combine(tempDir, ps1Rel), "# ps1");
            File.WriteAllText(Path.Combine(tempDir, shRel), "# sh");

            var def = new ScriptDefinition("test", PowerShellRelativePath: ps1Rel, BashRelativePath: shRel);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.Bash, tempDir);

            Assert.Contains(Path.Combine(tempDir, shRel), processRunner.LastArguments!);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_MissingPowerShellImplementation_FailsClearly()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", BashRelativePath: "script.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("test", ex.Message);
            Assert.Contains("PowerShell", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_MissingBashImplementation_FailsClearly()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: "script.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.Bash, tempDir));

            Assert.Contains("test", ex.Message);
            Assert.Contains("Bash", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_MissingScriptFile_FailsClearly()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: "nonexistent.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<FileNotFoundException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("nonexistent.ps1", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_SlashDotDotPathTraversal_PowerShell_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: "../outside.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("escapes repository root", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_BackslashDotDotPathTraversal_PowerShell_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: @"..\outside.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("escapes repository root", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_SlashDotDotPathTraversal_Bash_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", BashRelativePath: "../outside.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.Bash, tempDir));

            Assert.Contains("escapes repository root", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_BackslashDotDotPathTraversal_Bash_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", BashRelativePath: @"..\outside.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.Bash, tempDir));

            Assert.Contains("escapes repository root", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_SlashPath_PowerShell_ResolvesCorrectly()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: "scripts/test.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![5]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_BackslashPath_PowerShell_ResolvesCorrectlyEvenOnLinux()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: @"scripts\test.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![5]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_SlashPath_Bash_ResolvesCorrectly()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.sh");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "# sh");

            var def = new ScriptDefinition("test", BashRelativePath: "scripts/test.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.Bash, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![0]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_BackslashPath_Bash_ResolvesCorrectlyEvenOnLinux()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptFullPath = Path.Combine(tempDir, "scripts", "test.sh");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptFullPath)!);
            File.WriteAllText(scriptFullPath, "# sh");

            var def = new ScriptDefinition("test", BashRelativePath: @"scripts\test.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.Bash, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![0]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_RepositoryPrefixCollision_IsRejected()
    {
        string tempDir = CreateTempRepo();
        string collidingDir = tempDir + "-other";
        Directory.CreateDirectory(collidingDir);
        try
        {
            string collidingScript = Path.Combine(collidingDir, "script.ps1");
            File.WriteAllText(collidingScript, "# malicious");

            string relativeEscapingPath = Path.Combine("..", Path.GetFileName(collidingDir), "script.ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: relativeEscapingPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("escapes repository root", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
            DeleteTempRepo(collidingDir);
        }
    }

    [Fact]
    public void RunScript_UnixAbsolutePath_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", BashRelativePath: "/tmp/outside.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.Bash, tempDir));

            Assert.Contains("absolute path", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_WindowsDriveRootedPath_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: @"C:\outside\script.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("absolute path", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_WindowsUncPath_IsRejected()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: @"\\server\share\script.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            var ex = Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Contains("absolute path", ex.Message);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_CallsProcessRunnerExactlyOnce_OnValidExecution()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string scriptRelPath = "test.ps1";
            File.WriteAllText(Path.Combine(tempDir, scriptRelPath), "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: scriptRelPath);
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            ProcessResult result = scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.True(result.Success);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_ResolutionOrPathValidationFailure_DoesNotInvokeProcessRunner()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var def = new ScriptDefinition("test", PowerShellRelativePath: "invalid/file.ps1");
            var resolver = new FakeExecutableResolver(new InvalidOperationException("Failed to resolve executable."));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            Assert.Throws<InvalidOperationException>(() => scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir));

            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void ProcessRunner_ImplementsIProcessRunnerWithoutChangingRunContract()
    {
        IProcessRunner processRunner = new ProcessRunner();
        Assert.NotNull(processRunner);
    }

    [Fact]
    public void RunScript_DotDotPrefixDirectory_DotDotCache_IsAccepted()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string cacheDir = Path.Combine(tempDir, "..cache");
            Directory.CreateDirectory(cacheDir);
            string scriptFullPath = Path.Combine(cacheDir, "script.ps1");
            File.WriteAllText(scriptFullPath, "# ps1");

            var def = new ScriptDefinition("test", PowerShellRelativePath: @"..cache\script.ps1");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.PowerShell, ScriptExecutableKind.WindowsPowerShell, "powershell.exe"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.PowerShell, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![5]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void RunScript_DotDotPrefixDirectory_DotDotGenerated_IsAccepted()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string genDir = Path.Combine(tempDir, "..generated");
            Directory.CreateDirectory(genDir);
            string scriptFullPath = Path.Combine(genDir, "tool.sh");
            File.WriteAllText(scriptFullPath, "# sh");

            var def = new ScriptDefinition("test", BashRelativePath: "..generated/tool.sh");
            var resolver = new FakeExecutableResolver(new ResolvedScriptExecutable(ScriptShell.Bash, ScriptExecutableKind.Bash, "bash"));
            var processRunner = new FakeProcessRunner();

            var scriptRunner = new ScriptRunner(resolver, processRunner);
            scriptRunner.RunScript(def, ScriptShell.Bash, tempDir);

            Assert.Equal(1, processRunner.CallCount);
            Assert.Equal(scriptFullPath, processRunner.LastArguments![0]);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_runner_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRepo(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeExecutableResolver : IExecutableResolver
    {
        private readonly ResolvedScriptExecutable? _executable;
        private readonly Exception? _exception;

        public FakeExecutableResolver(ResolvedScriptExecutable executable)
        {
            _executable = executable;
        }

        public FakeExecutableResolver(Exception exception)
        {
            _exception = exception;
        }

        public ResolvedScriptExecutable Resolve(ScriptShell shell)
        {
            if (_exception != null)
            {
                throw _exception;
            }

            return _executable!;
        }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }
        public string? LastFileName { get; private set; }
        public List<string>? LastArguments { get; private set; }
        public string? LastWorkingDirectory { get; private set; }

        public ProcessResult Run(string fileName, IEnumerable<string> arguments, string workingDirectory)
        {
            CallCount++;
            LastFileName = fileName;
            LastArguments = arguments.ToList();
            LastWorkingDirectory = workingDirectory;

            return new ProcessResult(fileName, string.Join(" ", LastArguments), workingDirectory, 0, "ok", string.Empty);
        }
    }
}
