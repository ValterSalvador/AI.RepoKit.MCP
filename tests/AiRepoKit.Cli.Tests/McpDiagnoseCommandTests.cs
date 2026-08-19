using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.McpDiagnostics;
using AiRepoKit.Cli.Services.McpBudget;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class McpDiagnoseCommandTests
{
    [Fact]
    public void McpDiagnose_NativeBudgetService_IsInvokedWithRepoRoot()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

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
    public void McpDiagnose_SuccessfulNativeBudget_ProducesPassedBudgetDiagnostic()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Passed", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_FailedNativeBudget_ProducesFailedNonRequiredBudgetDiagnostic()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateFailureResult(tempDir)
            };
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Failed", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_NativeBudgetServiceException_ProducesFailedBudgetDiagnosticNotFatal()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ExceptionToThrow = new InvalidOperationException("MCP budget service unavailable.")
            };
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            Assert.DoesNotContain(jsonResult.Checks, c => c.Name == "fatal");

            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Failed", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
            Assert.Contains("MCP budget service unavailable.", budgetCheck.Message);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_MissingCompatibilityBudgetScript_DoesNotBlockNativeBudget()
    {
        // P02.1: physical absence of MeasureMcpResponseBudget.ps1 is irrelevant to native budget.
        string tempDir = CreateTempRepoWithoutBudgetScript();
        try
        {
            Assert.False(File.Exists(Path.Combine(tempDir, "Tools", "AiContext", "MeasureMcpResponseBudget.ps1")));

            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Equal(1, fakeBudget.InvocationCount);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");
            Assert.Equal("Passed", budgetCheck.Status);
            Assert.DoesNotContain("MeasureMcpResponseBudget.ps1 is missing", budgetCheck.Message);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_BashShell_InvokesNativeBudgetWithoutScriptShellDependency()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService
            {
                ResultToReturn = CreateSuccessResult(tempDir)
            };
            var command = new McpDiagnoseCommand(fakeBudget);
            // Bash shell must not cause budget to fail (service accepts no ScriptShell param)
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            Assert.Equal(1, fakeBudget.InvocationCount);
            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");
            Assert.Equal("Passed", budgetCheck.Status);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_QuickMode_DoesNotInvokeBudgetService()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService();
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto, quick: true);

            CommandResult result = command.Execute(options);

            Assert.Equal(0, fakeBudget.InvocationCount);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Skipped", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_SkipBudget_DoesNotInvokeBudgetService()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeBudget = new FakeMcpBudgetService();
            var command = new McpDiagnoseCommand(fakeBudget);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto, skipBudget: true);

            CommandResult result = command.Execute(options);

            Assert.Equal(0, fakeBudget.InvocationCount);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Skipped", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
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

    private static BootstrapOptions CreateOptions(string repoPath, ScriptShell shell, bool quick = false, bool skipBudget = false)
    {
        return new BootstrapOptions(
            command_: "mcp-diagnose",
            repoPath_: repoPath,
            clients_: [],
            includeMcp_: true,
            apply_: false,
            dryRun_: false,
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
            skipAiContext_: false,
            skipCodeInventory_: true,
            skipSecurityScan_: false,
            skipBudget_: skipBudget,
            skipSmoke_: true,
            skipScripts_: false,
            maxFiles_: 100,
            maxItems_: 100,
            includePrivateMembers_: false,
            noCache_: false,
            rebuildCache_: false,
            output_: ".ai/generated",
            format_: "json",
            verbose_: true,
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
            quick_: quick,
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
        string path = Path.Combine(Path.GetTempPath(), "airepo_mcpdiagnose_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "Tools", "AiContextMcp"));
        string binDir = Path.Combine(path, "Tools", "AiContextMcp", "bin", "Release", "net10.0");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(path, "Tools", "AiContextMcp", "AiRepo.ContextMcp.csproj"), "<Project></Project>");
        File.WriteAllText(Path.Combine(binDir, "AiRepo.ContextMcp.dll"), "dummy dll");
        return path;
    }

    private static string CreateTempRepoWithoutBudgetScript()
    {
        // MeasureMcpResponseBudget.ps1 intentionally absent — native budget must still run.
        string path = Path.Combine(Path.GetTempPath(), "airepo_mcpdiagnose_nobudget_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "Tools", "AiContextMcp"));
        string binDir = Path.Combine(path, "Tools", "AiContextMcp", "bin", "Release", "net10.0");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(path, "Tools", "AiContextMcp", "AiRepo.ContextMcp.csproj"), "<Project></Project>");
        File.WriteAllText(Path.Combine(binDir, "AiRepo.ContextMcp.dll"), "dummy dll");
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
        public McpBudgetRunResult? ResultToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public McpBudgetRunResult Run(string repoRoot, McpBudgetOptions? options = null)
        {
            InvocationCount++;
            LastRepoRoot = repoRoot;
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            return ResultToReturn ?? throw new InvalidOperationException("No result configured.");
        }
    }
}
