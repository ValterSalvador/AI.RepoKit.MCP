using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class EfficiencyCommandTests
{
    [Fact]
    public void Efficiency_PassesScriptShellAndIndividualArgumentsToScriptRunner()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultToReturn = new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty)
            };
            var command = new EfficiencyCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.PowerShell);

            CommandResult result = command.Execute(options);

            Assert.Equal(ScriptShell.PowerShell, fakeRunner.LastRequestedShell);
            Assert.Equal(ScriptDefinition.McpBudget, fakeRunner.LastDefinition);
            Assert.NotNull(fakeRunner.LastScriptArguments);
            Assert.Equal(new[] { "-RepoRoot", tempDir }, fakeRunner.LastScriptArguments);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    [Fact]
    public void Efficiency_SuccessfulProcessResult_SetsBudgetRefreshedTrue()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultToReturn = new ProcessResult("pwsh", string.Empty, tempDir, 0, "ok", string.Empty)
            };
            var command = new EfficiencyCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Auto);

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
    public void Efficiency_FailedProcessResult_PreservesFallbackAndSetsBudgetRefreshedFalse()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ResultToReturn = new ProcessResult("pwsh", string.Empty, tempDir, 1, string.Empty, "error")
            };
            var command = new EfficiencyCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Auto);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success);
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
    public void Efficiency_ScriptRunnerException_PreservesFallbackAndDoesNotFailCommand()
    {
        string tempDir = CreateTempRepo();
        try
        {
            var fakeRunner = new FakeScriptRunner
            {
                ExceptionToThrow = new InvalidOperationException("Script 'mcp-budget' does not have a Bash implementation.")
            };
            var command = new EfficiencyCommand(fakeRunner);
            BootstrapOptions options = CreateOptions(tempDir, ScriptShell.Bash);

            CommandResult result = command.Execute(options);

            Assert.True(result.Success);
            Assert.Contains("\"McpBudgetAttempted\": true", result.Markdown);
            Assert.Contains("\"McpBudgetRefreshed\": false", result.Markdown);
            Assert.Contains("MCP budget refresh failed", result.Markdown);
        }
        finally
        {
            DeleteTempRepo(tempDir);
        }
    }

    private static BootstrapOptions CreateOptions(string repoPath, ScriptShell shell)
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
        public ScriptDefinition? LastDefinition { get; private set; }
        public ScriptShell? LastRequestedShell { get; private set; }
        public string? LastRepositoryRoot { get; private set; }
        public List<string>? LastScriptArguments { get; private set; }
        public ProcessResult? ResultToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }

        public ProcessResult RunScript(
            ScriptDefinition definition,
            ScriptShell requestedShell,
            string repositoryRoot,
            IEnumerable<string>? scriptArguments = null,
            string? workingDirectory = null)
        {
            LastDefinition = definition;
            LastRequestedShell = requestedShell;
            LastRepositoryRoot = repositoryRoot;
            LastScriptArguments = scriptArguments?.ToList();

            if (ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            return ResultToReturn ?? new ProcessResult("test", string.Empty, repositoryRoot, 0, "ok", string.Empty);
        }
    }
}
