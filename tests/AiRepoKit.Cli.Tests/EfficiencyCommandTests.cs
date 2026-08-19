using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services.McpBudget;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class EfficiencyCommandTests
{
    [Fact]
    public void Efficiency_NativeBudgetService_IsInvokedWithRepoRoot()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new EfficiencyCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, skipBudget: false);

            command.Execute(options);

            Assert.Equal(1, fakeBudget.InvocationCount);
            Assert.Equal(Path.GetFullPath(tempDir), fakeBudget.LastRepoRoot);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Efficiency_SuccessfulNativeBudget_SetsBudgetRefreshedTrue()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new EfficiencyCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, skipBudget: false);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success);
            Assert.Contains("\"McpBudgetAttempted\": true", result.Markdown);
            Assert.Contains("\"McpBudgetRefreshed\": true", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Efficiency_FailedNativeBudget_PreservesFallbackAndSetsBudgetRefreshedFalse()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateFailureResult(tempDir)
            };
            var command = new EfficiencyCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, skipBudget: false);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success); // Efficiency is non-fatal on budget failure
            Assert.Contains("\"McpBudgetAttempted\": true", result.Markdown);
            Assert.Contains("\"McpBudgetRefreshed\": false", result.Markdown);
            Assert.Contains("MCP budget refresh failed", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Efficiency_NativeBudgetServiceException_PreservesFallbackAndDoesNotFailCommand()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ExceptionToThrow = new InvalidOperationException("MCP budget service failed unexpectedly.")
            };
            var command = new EfficiencyCommand(fakeBudget);
            // Even with Bash shell, native service is invoked (ScriptShell independence)
            BootstrapOptions options = CreateOptions(tempDir, skipBudget: false, shell: ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success); // Non-fatal fallback
            Assert.Contains("\"McpBudgetAttempted\": true", result.Markdown);
            Assert.Contains("\"McpBudgetRefreshed\": false", result.Markdown);
            Assert.Contains("MCP budget refresh failed", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Efficiency_BashShell_InvokesNativeBudgetWithoutScriptShellDependency()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new EfficiencyCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, skipBudget: false, shell: ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            Assert.Equal(1, fakeBudget.InvocationCount);
            Assert.Null(fakeBudget.LastOptions); // No ScriptShell passed to native service
            Assert.Contains("\"McpBudgetRefreshed\": true", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    private static McpBudgetRunResult CreateSuccessResult(string repoPath)
    {
        return new McpBudgetRunResult(
            McpBudgetExitClass.Success,
            new McpBudgetReport
            {
                GeneratedAtLocal = "2026-08-19 00:00:00",
                RepoRoot = Path.GetFullPath(repoPath),
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

    private static McpBudgetRunResult CreateFailureResult(string repoPath)
    {
        return new McpBudgetRunResult(
            McpBudgetExitClass.ValidationFailure,
            new McpBudgetReport
            {
                GeneratedAtLocal = "2026-08-19 00:00:00",
                RepoRoot = Path.GetFullPath(repoPath),
                McpAssembly = string.Empty,
                McpAssemblyExists = false,
                Manifest = null,
                ToolsListed = [],
                Results = [],
                Passed = false,
                Failures = ["get_repo_brief failed smoke validation."],
                Warnings = [],
                StderrLineCount = 0,
                StdoutLineCount = 0
            });
    }

    private static BootstrapOptions CreateOptions(string repoPath, bool skipBudget, ScriptShell shell = ScriptShell.Auto)
    {
        return new BootstrapOptions(
            command_: "efficiency",
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
            skipBudget_: skipBudget,
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

    private static string CreateTempRepo()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_efficiency_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        File.WriteAllText(Path.Combine(path, ".git", "HEAD"), "ref: refs/heads/main\n");
        Directory.CreateDirectory(Path.Combine(path, ".ai"));
        return path;
    }

    private static void DeleteTempRepo(string path)
    {
        if (Directory.Exists(path))
        {
            try { Directory.Delete(path, true); } catch { }
        }
    }

    private sealed class FakeMcpBudgetService : IMcpBudgetService
    {
        public int InvocationCount { get; private set; }
        public string? LastRepoRoot { get; private set; }
        public McpBudgetOptions? LastOptions { get; private set; }
        public McpBudgetRunResult? ResultToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public McpBudgetRunResult Run(string repoRoot, McpBudgetOptions? options = null)
        {
            InvocationCount++;
            LastRepoRoot = repoRoot;
            LastOptions = options;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return ResultToReturn ?? throw new InvalidOperationException("No result configured.");
        }
    }
}
