using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class BootstrapCommandTests
{
    [Fact]
    public void Bootstrap_PassesScriptShellToScriptRunner()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.PowerShell);

            CommandResult result = command.Execute(options);

            Assert.NotEmpty(fakeRunner.Calls);
            Assert.All(fakeRunner.Calls, call => Assert.Equal(ScriptShell.PowerShell, call.RequestedShell));
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_PassesExpectedLogicalScriptDefinitions()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            List<string> scriptNames = fakeRunner.Calls.Select(c => c.Definition.Name).ToList();
            Assert.Contains("update-ai-context", scriptNames);
            Assert.Contains("check-sdk-alignment", scriptNames);
            Assert.Contains("check-secrets", scriptNames);
            Assert.Contains("mcp-budget", scriptNames);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_DryRun_DoesNotInvokeScriptRunner()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: false, dryRun: true, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Empty(fakeRunner.Calls);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1: Simulated", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_SuccessfulScriptProcessResult_ProducesPassedScriptStatus()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultHandler = (def, shell) => new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty)
            };
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1: Passed", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_FailedProcessResult_ProducesFailedScriptStatusAndBootstrapFailure()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultHandler = (def, shell) => def.Name == "check-secrets"
                    ? new ProcessResult("pwsh", string.Empty, tempDir, 1, string.Empty, "secret leak detected")
                    : new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty)
            };
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Tools/AiContext/CheckSecrets.ps1: Failed exit 1", result.Markdown);
            Assert.Contains("Tools/AiContext/CheckSecrets.ps1 failed.", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_ScriptRunnerPreLaunchException_BecomesScriptLevelFailure()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ExceptionToThrow = new InvalidOperationException("Executable resolution failed.")
            };
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1: Failed / unable to execute", result.Markdown);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1 execution failed: Executable resolution failed.", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_ExplicitBashAgainstPowerShellOnlyScript_DoesNotSilentlyFallbackToPowerShell()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ExceptionToThrow = new InvalidOperationException("Script 'update-ai-context' does not have a Bash implementation.")
            };
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);
            Assert.NotEmpty(fakeRunner.Calls);
            Assert.Equal(ScriptShell.Bash, fakeRunner.Calls[0].RequestedShell);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1: Failed / unable to execute", result.Markdown);
            Assert.Contains("Script 'update-ai-context' does not have a Bash implementation.", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_MissingExpectedPhysicalScript_PreservesMissingWarningBehavior()
    {
        string tempDir = CreateTempRepoWithoutScripts();
        // Create Tools/AiContext/UpdateAiContext.ps1 as a Directory so File.Exists returns false even after InitCommand
        Directory.CreateDirectory(Path.Combine(tempDir, "Tools", "AiContext", "UpdateAiContext.ps1"));
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.DoesNotContain(fakeRunner.Calls, c => c.Definition.Name == "update-ai-context");
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1: Missing", result.Markdown);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1 was not found.", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_UpdateAiContextSuccess_EnablesManifestRefreshPath()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            bool updateAiContextPassed = false;
            var fakeRunner = new FakeScriptRunner
            {
                ResultHandler = (def, shell) =>
                {
                    if (def.Name == "update-ai-context")
                    {
                        updateAiContextPassed = true;
                    }
                    return new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty);
                }
            };
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success);
            Assert.True(updateAiContextPassed);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_FailureOfAnotherScript_DoesNotFalselyMarkUpdateAiContextAsSuccessful()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultHandler = (def, shell) => def.Name == "check-secrets"
                    ? new ProcessResult("pwsh", string.Empty, tempDir, 1, string.Empty, "check-secrets failed")
                    : new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty)
            };
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.False(result.Success);
            Assert.Contains("Tools/AiContext/CheckSecrets.ps1: Failed exit 1", result.Markdown);
            Assert.Contains("Tools/AiContext/UpdateAiContext.ps1: Passed", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_SuccessfulCodeIndex_SuppressesUpdateCodeInventoryFallback()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto, skipCodeInventory: true);

            CommandResult result = command.Execute(options);

            List<string> scriptNames = fakeRunner.Calls.Select(c => c.Definition.Name).ToList();
            Assert.DoesNotContain("update-code-inventory", scriptNames);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_FailedCodeIndex_RetainsUpdateCodeInventoryFallback()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new BootstrapCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, apply: true, dryRun: false, shell: ScriptShell.Auto, skipCodeInventory: false, format: "invalid");

            CommandResult result = command.Execute(options);

            List<string> scriptNames = fakeRunner.Calls.Select(c => c.Definition.Name).ToList();
            Assert.Contains("update-code-inventory", scriptNames);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    private static BootstrapOptions CreateOptions(string repoPath, bool apply, bool dryRun, ScriptShell shell, bool skipCodeInventory = true, string format = "markdown")
    {
        return new BootstrapOptions(
            command_: "bootstrap",
            repoPath_: repoPath,
            clients_: [],
            includeMcp_: true,
            apply_: apply,
            dryRun_: dryRun,
            backup_: false,
            force_: false,
            forceManaged_: false,
            profile_: "generic",
            targetFramework_: "net10.0",
            mcpServerName_: "ai_repo_context",
            toolCommandName_: "airepo",
            mcpProjectName_: "AiRepo.ContextMcp",
            mcpNamespace_: "AiRepo.ContextMcp",
            mcpAssemblyName_: "AiRepo.ContextMcp",
            mcpProjectRelativePath_: "Tools/AiContextMcp/AiRepo.ContextMcp.csproj",
            skipBuildMcp_: false,
            skipAiContext_: false,
            skipCodeInventory_: skipCodeInventory,
            skipSecurityScan_: false,
            skipBudget_: false,
            skipSmoke_: true,
            skipScripts_: false,
            maxFiles_: 100,
            maxItems_: 100,
            includePrivateMembers_: false,
            noCache_: false,
            rebuildCache_: false,
            output_: ".ai/generated",
            format_: format,
            verbose_: false,
            summary_: false,
            auditJson_: false,
            timings_: false,
            includeSource_: false,
            createAuditBaseline_: false,
            updateAuditBaseline_: false,
            showAuditBaseline_: false,
            failOnAccepted_: false,
            skipAudit_: true,
            includeAgents_: false,
            task_: "review-risk",
            target_: "",
            limit_: 20,
            requireContextPacks_: false,
            unknownOptions_: [],
            noProgress_: true,
            refresh_: true,
            noRefresh_: false,
            sampleQuery_: "test",
            profileExplicit_: false,
            forbiddenTerms_: [],
            sanitizeTerm_: "",
            sanitizeReplacement_: "",
            strict_: false,
            quick_: false,
            full_: false,
            defaultsSummary_: "",
            budget_: 0,
            kind_: "",
            since_: "",
            changedFiles_: false,
            rootPath_: "",
            orgSubcommand_: "",
            maxDepth_: 3,
            validationOnly_: false,
            strictStdio_: false,
            stopStaleMcpHosts_: false,
            testTarget_: "",
            skipHooks_: true,
            scriptShell_: shell);
    }

    private static string CreateTempRepoWithScripts()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_bootstrap_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        Directory.CreateDirectory(Path.Combine(path, ".ai"));
        Directory.CreateDirectory(Path.Combine(path, "Tools", "AiContext"));
        File.WriteAllText(Path.Combine(path, "Tools", "AiContext", "UpdateAiContext.ps1"), "# dummy");
        File.WriteAllText(Path.Combine(path, "Tools", "AiContext", "CheckSdkAlignment.ps1"), "# dummy");
        File.WriteAllText(Path.Combine(path, "Tools", "AiContext", "UpdateCodeInventory.ps1"), "# dummy");
        File.WriteAllText(Path.Combine(path, "Tools", "AiContext", "CheckSecrets.ps1"), "# dummy");
        File.WriteAllText(Path.Combine(path, "Tools", "AiContext", "MeasureMcpResponseBudget.ps1"), "# dummy");

        string mcpBinDir = Path.Combine(path, "Tools", "AiContextMcp", "bin", "Release", "net10.0");
        Directory.CreateDirectory(mcpBinDir);
        File.WriteAllText(Path.Combine(path, "Tools", "AiContextMcp", "AiRepo.ContextMcp.csproj"), "<Project></Project>");
        File.WriteAllText(Path.Combine(mcpBinDir, "AiRepo.ContextMcp.dll"), "dummy dll");
        File.SetLastWriteTimeUtc(Path.Combine(mcpBinDir, "AiRepo.ContextMcp.dll"), DateTime.UtcNow.AddHours(1));

        return path;
    }

    private static string CreateTempRepoWithoutScripts()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_bootstrap_noscripts_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        Directory.CreateDirectory(Path.Combine(path, ".ai"));

        string mcpBinDir = Path.Combine(path, "Tools", "AiContextMcp", "bin", "Release", "net10.0");
        Directory.CreateDirectory(mcpBinDir);
        File.WriteAllText(Path.Combine(path, "Tools", "AiContextMcp", "AiRepo.ContextMcp.csproj"), "<Project></Project>");
        File.WriteAllText(Path.Combine(mcpBinDir, "AiRepo.ContextMcp.dll"), "dummy dll");
        File.SetLastWriteTimeUtc(Path.Combine(mcpBinDir, "AiRepo.ContextMcp.dll"), DateTime.UtcNow.AddHours(1));

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
        else if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeScriptRunner : IScriptRunner
    {
        public List<(ScriptDefinition Definition, ScriptShell RequestedShell, string RepoRoot, List<string>? ScriptArguments)> Calls { get; } = [];
        public Func<ScriptDefinition, ScriptShell, ProcessResult>? ResultHandler { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public ProcessResult RunScript(
            ScriptDefinition definition,
            ScriptShell requestedShell,
            string repositoryRoot,
            IEnumerable<string>? scriptArguments = null,
            string? workingDirectory = null)
        {
            Calls.Add((definition, requestedShell, repositoryRoot, scriptArguments?.ToList()));

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            if (ResultHandler != null)
            {
                return ResultHandler(definition, requestedShell);
            }

            return new ProcessResult("pwsh", string.Empty, repositoryRoot, 0, "ok", string.Empty);
        }
    }
}
