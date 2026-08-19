using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.AiContextUpdate;
using AiRepoKit.Cli.Services.McpBudget;
using AiRepoKit.Cli.Services.SdkAlignment;
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
            var command = CreateCommand(fakeRunner, new FakeMcpBudgetService(), new FakeSdkAlignmentService());
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
            var fakeRunner =
                new FakeScriptRunner();

            var fakeBudget =
                new FakeMcpBudgetService();

            var fakeSdkAlignment =
                new FakeSdkAlignmentService();

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService();

            var command = CreateCommand(
                fakeRunner,
                fakeBudget,
                fakeSdkAlignment,
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.True(result.Success);

            List<string> scriptNames =
                fakeRunner.Calls
                    .Select(call =>
                        call.Definition.Name)
                    .ToList();

            Assert.DoesNotContain(
                "update-ai-context",
                scriptNames);

            Assert.DoesNotContain(
                "check-sdk-alignment",
                scriptNames);

            Assert.Contains(
                "check-secrets",
                scriptNames);

            Assert.DoesNotContain(
                "mcp-budget",
                scriptNames);

            Assert.Equal(
                1,
                fakeBudget.InvocationCount);

            Assert.Equal(
                1,
                fakeSdkAlignment.InvocationCount);

            Assert.Equal(
                1,
                fakeAiContextUpdate.InvocationCount);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_NativeSdkAlignment_RunsAfterNativeAiContextUpdateAndBeforeRemainingScripts()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            List<string> events = [];

            var fakeRunner =
                new FakeScriptRunner
                {
                    ResultHandler =
                        (definition, shell) =>
                        {
                            events.Add(
                                definition.Name);

                            return new ProcessResult(
                                "pwsh",
                                string.Empty,
                                tempDir,
                                0,
                                "ok",
                                string.Empty);
                        }
                };

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService
                {
                    OnRun =
                        (_, _) =>
                            events.Add(
                                "ai-context-update")
                };

            var fakeSdkAlignment =
                new FakeSdkAlignmentService
                {
                    OnRun =
                        () =>
                            events.Add(
                                "sdk-alignment")
                };

            var command = CreateCommand(
                fakeRunner,
                new FakeMcpBudgetService(),
                fakeSdkAlignment,
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.True(result.Success);

            int updateIndex =
                events.IndexOf(
                    "ai-context-update");

            int sdkIndex =
                events.IndexOf(
                    "sdk-alignment");

            int secretsIndex =
                events.IndexOf(
                    "check-secrets");

            Assert.True(updateIndex >= 0);
            Assert.True(sdkIndex > updateIndex);
            Assert.True(secretsIndex > sdkIndex);

            Assert.NotNull(
                fakeAiContextUpdate.LastOptions);

            Assert.Equal(
                "net10.0",
                fakeAiContextUpdate
                    .LastOptions!
                    .TargetFramework);

            Assert.Equal(
                "ai_repo_context",
                fakeAiContextUpdate
                    .LastOptions
                    .McpServerName);

            Assert.Equal(
                "Tools/AiContextMcp/AiRepo.ContextMcp.csproj",
                fakeAiContextUpdate
                    .LastOptions
                    .McpProjectRelativePath);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_NativeAiContextUpdateFailure_FailsBootstrapAndStillRunsSdkAlignment()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeAiContextUpdate =
                new FakeAiContextUpdateService
                {
                    ResultToReturn =
                        AiContextUpdateRunResult.Failure(
                            "test failure")
                };

            var fakeSdkAlignment =
                new FakeSdkAlignmentService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment,
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);

            Assert.Equal(
                1,
                fakeAiContextUpdate.InvocationCount);

            Assert.Equal(
                1,
                fakeSdkAlignment.InvocationCount);

            Assert.Contains(
                "ai-context-update: Failed",
                result.Markdown);

            Assert.Contains(
                "AI context update failed: test failure",
                result.Markdown);

        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_NativeSdkAlignmentFailure_FailsBootstrap()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeSdkAlignment = new FakeSdkAlignmentService
            {
                ResultToReturn = SdkAlignmentRunResult.Failure(
                    "dotnet --version failed: test failure")
            };

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment);

            BootstrapOptions options = CreateOptions(
                tempDir,
                apply: true,
                dryRun: false,
                shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "sdk-alignment: Failed",
                result.Markdown);
            Assert.Contains(
                "SDK alignment failed: dotnet --version failed: test failure",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_DryRun_DoesNotInvokeNativeSdkAlignment()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeSdkAlignment = new FakeSdkAlignmentService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment);

            BootstrapOptions options = CreateOptions(
                tempDir,
                apply: false,
                dryRun: true,
                shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Equal(0, fakeSdkAlignment.InvocationCount);
            Assert.Contains(
                "sdk-alignment: Simulated",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_SkipAiContext_DoesNotInvokeNativeSdkAlignment()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeSdkAlignment = new FakeSdkAlignmentService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment);

            BootstrapOptions options = CreateOptions(
                tempDir,
                apply: true,
                dryRun: false,
                shell: ScriptShell.Auto,
                skipAiContext: true);

            CommandResult result = command.Execute(options);

            Assert.Equal(0, fakeSdkAlignment.InvocationCount);
            Assert.Contains(
                "sdk-alignment: Skipped by --skip-ai-context",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_SkipScripts_DoesNotInvokeNativeSdkAlignment()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeSdkAlignment = new FakeSdkAlignmentService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment);

            BootstrapOptions options = CreateOptions(
                tempDir,
                apply: true,
                dryRun: false,
                shell: ScriptShell.Auto,
                skipScripts: true);

            CommandResult result = command.Execute(options);

            Assert.Equal(0, fakeSdkAlignment.InvocationCount);
            Assert.Contains(
                "sdk-alignment: Skipped by --skip-scripts",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_IncludeMcpFalse_DoesNotInvokeNativeSdkAlignment()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeSdkAlignment =
                new FakeSdkAlignmentService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment);

            BootstrapOptions options = CreateOptions(
                tempDir,
                apply: true,
                dryRun: false,
                shell: ScriptShell.Auto,
                includeMcp: false);

            CommandResult result =
                command.Execute(options);

            Assert.Equal(
                0,
                fakeSdkAlignment.InvocationCount);

            Assert.Contains(
                "Skipped because --mcp was not selected.",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_McpBuildFailure_DoesNotInvokeNativeSdkAlignment()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            string mcpProject = Path.Combine(
                tempDir,
                "Tools",
                "AiContextMcp",
                "AiRepo.ContextMcp.csproj");

            string mcpDll = Path.Combine(
                tempDir,
                "Tools",
                "AiContextMcp",
                "bin",
                "Release",
                "net10.0",
                "AiRepo.ContextMcp.dll");

            File.Delete(mcpDll);

            File.WriteAllText(
                mcpProject,
                "<Project><Invalid>");

            var fakeSdkAlignment =
                new FakeSdkAlignmentService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                fakeSdkAlignment);

            BootstrapOptions options = CreateOptions(
                tempDir,
                apply: true,
                dryRun: false,
                shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.False(result.Success);

            Assert.Equal(
                0,
                fakeSdkAlignment.InvocationCount);

            Assert.Contains(
                "Skipped because MCP build did not pass.",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_NativeAiContextUpdate_RespectsNonExecutionGates()
    {
        var cases = new[]
        {
            new
            {
                Apply = false,
                DryRun = true,
                SkipAiContext = false,
                SkipScripts = false,
                IncludeMcp = true,
                ExpectedStatus =
                    "ai-context-update: Simulated"
            },
            new
            {
                Apply = true,
                DryRun = false,
                SkipAiContext = true,
                SkipScripts = false,
                IncludeMcp = true,
                ExpectedStatus =
                    "ai-context-update: Skipped by --skip-ai-context"
            },
            new
            {
                Apply = true,
                DryRun = false,
                SkipAiContext = false,
                SkipScripts = true,
                IncludeMcp = true,
                ExpectedStatus =
                    "ai-context-update: Skipped by --skip-scripts"
            },
            new
            {
                Apply = true,
                DryRun = false,
                SkipAiContext = false,
                SkipScripts = false,
                IncludeMcp = false,
                ExpectedStatus =
                    "Skipped because --mcp was not selected."
            }
        };

        foreach (var gate in cases)
        {
            string tempDir =
                CreateTempRepoWithScripts();

            try
            {
                var fakeAiContextUpdate =
                    new FakeAiContextUpdateService();

                var command = CreateCommand(
                    new FakeScriptRunner(),
                    new FakeMcpBudgetService(),
                    new FakeSdkAlignmentService(),
                    fakeAiContextUpdate);

                BootstrapOptions options =
                    CreateOptions(
                        tempDir,
                        gate.Apply,
                        gate.DryRun,
                        ScriptShell.Auto,
                        skipAiContext:
                            gate.SkipAiContext,
                        skipScripts:
                            gate.SkipScripts,
                        includeMcp:
                            gate.IncludeMcp);

                CommandResult result =
                    command.Execute(options);

                Assert.True(result.Success);

                Assert.Equal(
                    0,
                    fakeAiContextUpdate.InvocationCount);

                Assert.Contains(
                    gate.ExpectedStatus,
                    result.Markdown);
            }
            finally
            {
                DeleteTempRepo(tempDir);
            }
        }

        string failedBuildRepo =
            CreateTempRepoWithScripts();

        try
        {
            string project =
                Path.Combine(
                    failedBuildRepo,
                    "Tools",
                    "AiContextMcp",
                    "AiRepo.ContextMcp.csproj");

            string dll =
                Path.Combine(
                    failedBuildRepo,
                    "Tools",
                    "AiContextMcp",
                    "bin",
                    "Release",
                    "net10.0",
                    "AiRepo.ContextMcp.dll");

            File.Delete(dll);

            File.WriteAllText(
                project,
                "<Project><Invalid>");

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService();

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                new FakeSdkAlignmentService(),
                fakeAiContextUpdate);

            CommandResult result =
                command.Execute(
                    CreateOptions(
                        failedBuildRepo,
                        apply: true,
                        dryRun: false,
                        shell: ScriptShell.Auto));

            Assert.False(result.Success);

            Assert.Equal(
                0,
                fakeAiContextUpdate.InvocationCount);

            Assert.Contains(
                "Skipped because MCP build did not pass.",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(failedBuildRepo);
        }
    }

    [Fact]
    public void Bootstrap_DryRun_DoesNotInvokeScriptRunner()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = CreateCommand(fakeRunner, new FakeMcpBudgetService(), new FakeSdkAlignmentService());
            BootstrapOptions options = CreateOptions(tempDir, apply: false, dryRun: true, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Empty(fakeRunner.Calls);
            Assert.Contains("ai-context-update: Simulated", result.Markdown);
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
            var fakeRunner =
                new FakeScriptRunner
                {
                    ResultHandler =
                        (definition, shell) =>
                            new ProcessResult(
                                "pwsh",
                                string.Empty,
                                tempDir,
                                0,
                                "ok",
                                string.Empty)
                };

            var command = CreateCommand(
                fakeRunner);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.True(result.Success);

            Assert.Contains(
                "Tools/AiContext/CheckSecrets.ps1: Passed",
                result.Markdown);

            Assert.Contains(
                "ai-context-update: Passed",
                result.Markdown);
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
            var command = CreateCommand(fakeRunner, new FakeMcpBudgetService(), new FakeSdkAlignmentService());
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
            var fakeRunner =
                new FakeScriptRunner
                {
                    ExceptionToThrow =
                        new InvalidOperationException(
                            "Executable resolution failed.")
                };

            var command = CreateCommand(
                fakeRunner);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.False(result.Success);
            Assert.Equal(1, result.ExitCode);

            Assert.Contains(
                "Tools/AiContext/CheckSecrets.ps1: Failed / unable to execute",
                result.Markdown);

            Assert.Contains(
                "Tools/AiContext/CheckSecrets.ps1 execution failed: Executable resolution failed.",
                result.Markdown);

            Assert.Contains(
                "ai-context-update: Passed",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_ExplicitBash_DoesNotAffectNativeAiContextUpdate()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner =
                new FakeScriptRunner();

            var fakeSdkAlignment =
                new FakeSdkAlignmentService();

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService();

            var command = CreateCommand(
                fakeRunner,
                new FakeMcpBudgetService(),
                fakeSdkAlignment,
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Bash,
                    skipSecurityScan: true);

            CommandResult result =
                command.Execute(options);

            Assert.True(result.Success);
            Assert.Empty(fakeRunner.Calls);

            Assert.Equal(
                1,
                fakeAiContextUpdate.InvocationCount);

            Assert.Equal(
                1,
                fakeSdkAlignment.InvocationCount);

            Assert.Contains(
                "ai-context-update: Passed",
                result.Markdown);

            Assert.Contains(
                "sdk-alignment: Passed",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_NativeAiContextUpdate_IsNotSentToScriptRunner()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner =
                new FakeScriptRunner();

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService();

            var command = CreateCommand(
                fakeRunner,
                new FakeMcpBudgetService(),
                new FakeSdkAlignmentService(),
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.True(result.Success);

            Assert.Equal(
                1,
                fakeAiContextUpdate.InvocationCount);

            Assert.DoesNotContain(
                fakeRunner.Calls,
                call =>
                    call.Definition.Name ==
                    "update-ai-context");
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_NativeAiContextUpdateSuccess_EnablesManifestRefreshPath()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            bool updateAiContextPassed =
                false;

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService
                {
                    OnRun =
                        (_, _) =>
                            updateAiContextPassed =
                                true
                };

            var command = CreateCommand(
                new FakeScriptRunner(),
                new FakeMcpBudgetService(),
                new FakeSdkAlignmentService(),
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.True(result.Success);
            Assert.True(updateAiContextPassed);

            Assert.Contains(
                "ai-context-update: Passed",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Bootstrap_FailureOfAnotherScript_DoesNotFalselyFailNativeAiContextUpdate()
    {
        string tempDir = CreateTempRepoWithScripts();
        try
        {
            var fakeRunner =
                new FakeScriptRunner
                {
                    ResultHandler =
                        (definition, shell) =>
                            definition.Name ==
                            "check-secrets"
                                ? new ProcessResult(
                                    "pwsh",
                                    string.Empty,
                                    tempDir,
                                    1,
                                    string.Empty,
                                    "check-secrets failed")
                                : new ProcessResult(
                                    "pwsh",
                                    string.Empty,
                                    tempDir,
                                    0,
                                    "ok",
                                    string.Empty)
                };

            var fakeAiContextUpdate =
                new FakeAiContextUpdateService();

            var command = CreateCommand(
                fakeRunner,
                new FakeMcpBudgetService(),
                new FakeSdkAlignmentService(),
                fakeAiContextUpdate);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    apply: true,
                    dryRun: false,
                    shell: ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.False(result.Success);

            Assert.Equal(
                1,
                fakeAiContextUpdate.InvocationCount);

            Assert.Contains(
                "Tools/AiContext/CheckSecrets.ps1: Failed exit 1",
                result.Markdown);

            Assert.Contains(
                "ai-context-update: Passed",
                result.Markdown);
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
            var command = CreateCommand(fakeRunner, new FakeMcpBudgetService(), new FakeSdkAlignmentService());
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
            var command = CreateCommand(fakeRunner, new FakeMcpBudgetService(), new FakeSdkAlignmentService());
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

    private static BootstrapCommand CreateCommand(
        IScriptRunner scriptRunner,
        IMcpBudgetService? mcpBudgetService = null,
        ISdkAlignmentService? sdkAlignmentService = null,
        IAiContextUpdateService? aiContextUpdateService = null)
    {
        return new BootstrapCommand(
            scriptRunner,
            mcpBudgetService ??
                new FakeMcpBudgetService(),
            sdkAlignmentService ??
                new FakeSdkAlignmentService(),
            aiContextUpdateService ??
                new FakeAiContextUpdateService());
    }

    private static BootstrapOptions CreateOptions(
        string repoPath,
        bool apply,
        bool dryRun,
        ScriptShell shell,
        bool skipCodeInventory = true,
        string format = "markdown",
        bool skipAiContext = false,
        bool skipScripts = false,
        bool includeMcp = true,
        bool skipSecurityScan = false)
    {
        return new BootstrapOptions(
            command_: "bootstrap",
            repoPath_: repoPath,
            clients_: [],
            includeMcp_: includeMcp,
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
            skipAiContext_: skipAiContext,
            skipCodeInventory_: skipCodeInventory,
            skipSecurityScan_: skipSecurityScan,
            skipBudget_: false,
            skipSmoke_: true,
            skipScripts_: skipScripts,
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

    private sealed class FakeAiContextUpdateService :
        IAiContextUpdateService
    {
        public int InvocationCount
        {
            get;
            private set;
        }

        public Action<
            string,
            AiContextUpdateOptions?>? OnRun
        {
            get;
            init;
        }

        public AiContextUpdateRunResult? ResultToReturn
        {
            get;
            init;
        }

        public AiContextUpdateOptions? LastOptions
        {
            get;
            private set;
        }

        public AiContextUpdateRunResult Run(
            string repoRoot,
            AiContextUpdateOptions? options = null)
        {
            InvocationCount++;
            LastOptions = options;

            OnRun?.Invoke(
                repoRoot,
                options);

            return ResultToReturn ??
                AiContextUpdateRunResult.Success();
        }
    }

    private sealed class FakeSdkAlignmentService : ISdkAlignmentService
    {
        public int InvocationCount { get; private set; }

        public Action? OnRun { get; init; }

        public SdkAlignmentRunResult? ResultToReturn { get; init; }

        public SdkAlignmentRunResult Run(string repoRoot)
        {
            InvocationCount++;
            OnRun?.Invoke();

            return ResultToReturn ??
                SdkAlignmentRunResult.Success(
                    new SdkAlignmentReport
                    {
                        ExpectedTargetFramework = "net10.0",
                        DotNetSdkVersion = "10.0.111",
                        DotNetSdks = ["10.0.111 [/sdk]"],
                        Projects = []
                    });
        }
    }

    private sealed class FakeMcpBudgetService : IMcpBudgetService
    {
        public int InvocationCount { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public McpBudgetRunResult? ResultToReturn { get; set; }

        public McpBudgetRunResult Run(string repoRoot, McpBudgetOptions? options = null)
        {
            InvocationCount++;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return ResultToReturn ?? new McpBudgetRunResult(
                McpBudgetExitClass.Success,
                new McpBudgetReport
                {
                    GeneratedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    RepoRoot = repoRoot,
                    McpAssembly = string.Empty,
                    McpAssemblyExists = false,
                    Manifest = null,
                    ToolsListed = [],
                    Results = [],
                    Passed = true,
                    Failures = [],
                    Warnings = [],
                    StderrLineCount = 0,
                    StdoutLineCount = 0
                });
        }
    }
}
