using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.McpBudget;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class SelfCheckCommandTests
{
    [Fact]
    public void SelfCheck_UsesMcpBudgetServiceAndPassesRepositoryRoot()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeService = new FakeMcpBudgetService
            {
                ResultToReturn = CreateBudgetResult(
                    tempDir,
                    McpBudgetExitClass.Success,
                    passed: true)
            };

            var command = new SelfCheckCommand(fakeService);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.PowerShell);

            command.Execute(options);

            Assert.Equal(1, fakeService.InvocationCount);
            Assert.Equal(Path.GetFullPath(tempDir), fakeService.LastRepositoryRoot);
            Assert.Null(fakeService.LastOptions);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void SelfCheck_SuccessfulMcpBudget_ProducesPassedCheck()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeService = new FakeMcpBudgetService
            {
                ResultToReturn = CreateBudgetResult(
                    tempDir,
                    McpBudgetExitClass.Success,
                    passed: true)
            };

            var command = new SelfCheckCommand(fakeService);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Contains("\"mcp-budget\"", result.Markdown);
            Assert.Contains("\"Passed\"", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void SelfCheck_FailedMcpBudget_ProducesFailedCheck()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeService = new FakeMcpBudgetService
            {
                ResultToReturn = CreateBudgetResult(
                    tempDir,
                    McpBudgetExitClass.ValidationFailure,
                    passed: false,
                    "get_repo_brief failed smoke validation.")
            };

            var command = new SelfCheckCommand(fakeService);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Contains("\"mcp-budget\"", result.Markdown);
            Assert.Contains("\"Failed\"", result.Markdown);
            Assert.Contains("get_repo_brief failed smoke validation.", result.Markdown);
            Assert.DoesNotContain("\"fatal\"", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void SelfCheck_McpBudgetException_ProducesFailedCheckAndNotFatal()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeService = new FakeMcpBudgetService
            {
                ExceptionToThrow = new InvalidOperationException("MCP budget service unavailable.")
            };

            var command = new SelfCheckCommand(fakeService);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Contains("\"mcp-budget\"", result.Markdown);
            Assert.Contains("\"Failed\"", result.Markdown);
            Assert.Contains("MCP budget service unavailable.", result.Markdown);
            Assert.DoesNotContain("\"fatal\"", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void SelfCheck_MissingCompatibilityBudgetScript_DoesNotBlockNativeBudget()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string compatibilityScript = Path.Combine(
                tempDir,
                "Tools",
                "AiContext",
                "MeasureMcpResponseBudget.ps1");

            Assert.False(File.Exists(compatibilityScript));

            var fakeService = new FakeMcpBudgetService
            {
                ResultToReturn = CreateBudgetResult(
                    tempDir,
                    McpBudgetExitClass.Success,
                    passed: true)
            };

            var command = new SelfCheckCommand(fakeService);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Equal(1, fakeService.InvocationCount);
            Assert.Contains("\"mcp-budget\"", result.Markdown);
            Assert.Contains("\"Passed\"", result.Markdown);
            Assert.DoesNotContain(
                "required-file:Tools/AiContext/MeasureMcpResponseBudget.ps1",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void SelfCheck_MissingNativeMigratedCompatibilityScripts_DoesNotCreateRequiredFileFailures()
    {
        string tempDir = CreateTempRepo();
        try
        {
            string updateScript =
                Path.Combine(
                    tempDir,
                    "Tools",
                    "AiContext",
                    "UpdateAiContext.ps1");

            string sdkScript =
                Path.Combine(
                    tempDir,
                    "Tools",
                    "AiContext",
                    "CheckSdkAlignment.ps1");

            Assert.False(
                File.Exists(updateScript));

            Assert.False(
                File.Exists(sdkScript));

            var fakeService =
                new FakeMcpBudgetService
                {
                    ResultToReturn =
                        CreateBudgetResult(
                            tempDir,
                            McpBudgetExitClass.Success,
                            passed: true)
                };

            var command =
                new SelfCheckCommand(
                    fakeService);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    ScriptShell.Auto);

            CommandResult result =
                command.Execute(options);

            Assert.DoesNotContain(
                "required-file:Tools/AiContext/UpdateAiContext.ps1",
                result.Markdown);

            Assert.DoesNotContain(
                "required-file:Tools/AiContext/CheckSdkAlignment.ps1",
                result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void SelfCheck_MissingNativeSecretScanCompatibilityScript_DoesNotCreateRequiredFileFailure()
    {
        string tempDir =
            CreateTempRepo();

        try
        {
            string compatibilityScript =
                Path.Combine(
                    tempDir,
                    "Tools",
                    "AiContext",
                    "CheckSecrets.ps1");

            Assert.False(
                File.Exists(
                    compatibilityScript));

            var fakeService =
                new FakeMcpBudgetService
                {
                    ResultToReturn =
                        CreateBudgetResult(
                            tempDir,
                            McpBudgetExitClass.Success,
                            passed: true)
                };

            var command =
                new SelfCheckCommand(
                    fakeService);

            BootstrapOptions options =
                CreateOptions(
                    tempDir,
                    ScriptShell.Auto);

            CommandResult result =
                command.Execute(
                    options);

            Assert.DoesNotContain(
                "required-file:Tools/AiContext/CheckSecrets.ps1",
                result.Markdown);

            Assert.Equal(
                1,
                fakeService.InvocationCount);
        }
        finally
        {
            DeleteTempRepo(
                tempDir);
        }
    }

    [Fact]
    public void SelfCheck_BashShell_DoesNotAffectNativeBudgetExecution()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeService = new FakeMcpBudgetService
            {
                ResultToReturn = CreateBudgetResult(
                    tempDir,
                    McpBudgetExitClass.Success,
                    passed: true)
            };

            var command = new SelfCheckCommand(fakeService);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            Assert.Equal(1, fakeService.InvocationCount);
            Assert.Equal(Path.GetFullPath(tempDir), fakeService.LastRepositoryRoot);
            Assert.Contains("\"mcp-budget\"", result.Markdown);
            Assert.Contains("\"Passed\"", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    private static McpBudgetRunResult CreateBudgetResult(
        string repoPath,
        McpBudgetExitClass exitClass,
        bool passed,
        params string[] failures)
    {
        return new McpBudgetRunResult(
            exitClass,
            new McpBudgetReport
            {
                GeneratedAtLocal = "2026-08-19 00:00:00",
                RepoRoot = Path.GetFullPath(repoPath),
                McpAssembly = Path.Combine(
                    Path.GetFullPath(repoPath),
                    "Tools",
                    "AiContextMcp",
                    "bin",
                    "Release",
                    "net10.0",
                    "AiRepo.ContextMcp.dll"),
                McpAssemblyExists = true,
                Manifest = Path.Combine(
                    Path.GetFullPath(repoPath),
                    ".ai",
                    "manifests",
                    "mcp-context-manifest.json"),
                ToolsListed = [],
                Results = [],
                Passed = passed,
                Failures = failures,
                Warnings = [],
                StderrLineCount = 0,
                StdoutLineCount = 0
            });
    }

    private static BootstrapOptions CreateOptions(
        string repoPath,
        ScriptShell shell)
    {
        return new BootstrapOptions(
            command_: "self-check",
            repoPath_: repoPath,
            clients_: [],
            includeMcp_: false,
            apply_: false,
            dryRun_: true,
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
            skipBuildMcp_: true,
            skipAiContext_: true,
            skipCodeInventory_: true,
            skipSecurityScan_: true,
            skipBudget_: false,
            skipSmoke_: true,
            skipScripts_: true,
            maxFiles_: 100,
            maxItems_: 100,
            includePrivateMembers_: false,
            noCache_: false,
            rebuildCache_: false,
            output_: ".ai/generated",
            format_: "json",
            verbose_: false,
            summary_: false,
            auditJson_: true,
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
            refresh_: false,
            noRefresh_: false,
            sampleQuery_: "test",
            profileExplicit_: false,
            forbiddenTerms_: [],
            sanitizeTerm_: "",
            sanitizeReplacement_: "",
            strict_: false,
            quick_: false,
            full_: true,
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

    private static string CreateTempRepo()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "airepo_selfcheck_test_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRepo(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private sealed class FakeMcpBudgetService : IMcpBudgetService
    {
        public McpBudgetRunResult? ResultToReturn { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public int InvocationCount { get; private set; }

        public string? LastRepositoryRoot { get; private set; }

        public McpBudgetOptions? LastOptions { get; private set; }

        public McpBudgetRunResult Run(
            string repoRoot,
            McpBudgetOptions? options = null)
        {
            InvocationCount++;
            LastRepositoryRoot = repoRoot;
            LastOptions = options;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return ResultToReturn
                ?? SelfCheckCommandTests.CreateBudgetResult(
                    repoRoot,
                    McpBudgetExitClass.Success,
                    passed: true);
        }
    }
}
