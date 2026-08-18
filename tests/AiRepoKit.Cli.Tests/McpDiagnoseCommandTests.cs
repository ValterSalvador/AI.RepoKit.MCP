using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.McpDiagnostics;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class McpDiagnoseCommandTests
{
    [Fact]
    public void McpDiagnose_PassesScriptShellToScriptRunner()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.PowerShell);

            CommandResult result = command.Execute(options);

            Assert.Single(fakeRunner.Calls);
            Assert.Equal(ScriptShell.PowerShell, fakeRunner.Calls[0].RequestedShell);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_PassesMcpBudgetScriptDefinition()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Single(fakeRunner.Calls);
            Assert.Equal(ScriptDefinition.McpBudget, fakeRunner.Calls[0].Definition);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_PassesRepoRootAsSeparateScriptArguments()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Single(fakeRunner.Calls);
            Assert.NotNull(fakeRunner.Calls[0].ScriptArguments);
            Assert.Equal(new[] { "-RepoRoot", Path.GetFullPath(tempDir) }, fakeRunner.Calls[0].ScriptArguments);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_SuccessfulProcessResult_ProducesPassedBudgetDiagnostic()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultToReturn = new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty)
            };
            var command = new McpDiagnoseCommand(fakeRunner);
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
    public void McpDiagnose_FailedProcessResult_ProducesFailedNonRequiredBudgetDiagnostic()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultToReturn = new ProcessResult("pwsh", string.Empty, tempDir, 1, string.Empty, "budget check failed")
            };
            var command = new McpDiagnoseCommand(fakeRunner);
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
    public void McpDiagnose_ScriptRunnerPreLaunchException_ProducesFailedBudgetDiagnosticNotFatal()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ExceptionToThrow = new InvalidOperationException("Script 'mcp-budget' does not have a Bash implementation.")
            };
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            Assert.DoesNotContain(jsonResult.Checks, c => c.Name == "fatal");

            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Failed", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
            Assert.Contains("Script 'mcp-budget' does not have a Bash implementation.", budgetCheck.Message);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_PhysicallyMissingBudgetScript_ProducesWarningAndNoRunnerInvocation()
    {
        string tempDir = CreateTempRepoWithoutBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.Empty(fakeRunner.Calls);

            McpDiagnosticResult jsonResult = JsonSerializer.Deserialize<McpDiagnosticResult>(result.Markdown)!;
            McpDiagnosticItem budgetCheck = jsonResult.Checks.First(c => c.Name == "budget");

            Assert.Equal("Warning", budgetCheck.Status);
            Assert.False(budgetCheck.Required);
            Assert.Contains("Tools/AiContext/MeasureMcpResponseBudget.ps1 is missing.", budgetCheck.Message);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void McpDiagnose_QuickMode_DoesNotInvokeScriptRunner()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto, quick: true);

            CommandResult result = command.Execute(options);

            Assert.Empty(fakeRunner.Calls);

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
    public void McpDiagnose_SkipBudget_DoesNotInvokeScriptRunner()
    {
        string tempDir = CreateTempRepoWithBudgetScript();
        try
        {
            var fakeRunner = new FakeScriptRunner();
            var command = new McpDiagnoseCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, shell: ScriptShell.Auto, skipBudget: true);

            CommandResult result = command.Execute(options);

            Assert.Empty(fakeRunner.Calls);

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

    private static string CreateTempRepoWithBudgetScript()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_mcpdiagnose_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "Tools", "AiContext"));
        Directory.CreateDirectory(Path.Combine(path, "Tools", "AiContextMcp"));
        string binDir = Path.Combine(path, "Tools", "AiContextMcp", "bin", "Release", "net10.0");
        Directory.CreateDirectory(binDir);
        File.WriteAllText(Path.Combine(path, "Tools", "AiContextMcp", "AiRepo.ContextMcp.csproj"), "<Project></Project>");
        File.WriteAllText(Path.Combine(binDir, "AiRepo.ContextMcp.dll"), "dummy dll");
        File.WriteAllText(Path.Combine(path, "Tools", "AiContext", "MeasureMcpResponseBudget.ps1"), "# dummy");
        return path;
    }

    private static string CreateTempRepoWithoutBudgetScript()
    {
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
            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeScriptRunner : IScriptRunner
    {
        public List<(ScriptDefinition Definition, ScriptShell RequestedShell, string RepoRoot, List<string>? ScriptArguments)> Calls { get; } = [];
        public ProcessResult? ResultToReturn { get; set; }
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

            return ResultToReturn ?? new ProcessResult("pwsh", string.Empty, repositoryRoot, 0, "ok", string.Empty);
        }
    }
}
