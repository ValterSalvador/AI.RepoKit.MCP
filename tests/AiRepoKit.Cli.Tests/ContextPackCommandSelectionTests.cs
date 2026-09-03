using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ContextPacks;
using AiRepoKit.Cli.Services.ContextPacks;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class ContextPackCommandSelectionTests
{
    [Fact]
    public void Execute_DelegatesCompatibilityAndSelection_PreservingInputsWarningsAndPlannedPaths()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            FakeContextPackSelectionService service = new()
            {
                Compatibility = new ContextPackInventoryCompatibility(true, true),
                Warnings = ["selection warning"]
            };
            ContextPackCommand command = new(service);
            BootstrapOptions options = CreateOptions(
                repoRoot,
                task: "CHANGE-API",
                target: "Orders V2",
                limit: 7,
                budget: 321,
                format: "all");

            CommandResult result = command.Execute(options);

            Assert.True(result.Success);
            Assert.Equal(1, service.CompatibilityInvocationCount);
            Assert.Equal(repoRoot, service.LastCompatibilityRepoRoot);
            Assert.Equal(1, service.SelectionInvocationCount);
            ContextPackRequest request = Assert.IsType<ContextPackRequest>(service.LastRequest);
            Assert.Equal(repoRoot, request.RepoRoot);
            Assert.Equal("change-api", request.Task);
            Assert.Equal("Orders V2", request.Target);
            Assert.Equal(7, request.Limit);
            Assert.Equal(321, request.Budget);
            Assert.False(string.IsNullOrWhiteSpace(service.LastGeneratedAtLocal));

            using JsonDocument report = JsonDocument.Parse(result.Markdown);
            string[] warnings = report.RootElement.GetProperty("warnings").EnumerateArray().Select(item_ => item_.GetString()!).ToArray();
            string[] files = report.RootElement.GetProperty("files").EnumerateArray().Select(item_ => item_.GetString()!).ToArray();
            Assert.Equal(
                new[]
                {
                    "Existing compatible code inventories reused before context-pack generation.",
                    "selection warning"
                },
                warnings);
            Assert.Equal(
                new[]
                {
                    ".ai/generated/context-packs/change-api.orders-v2.json",
                    ".ai/generated/context-packs/change-api.orders-v2.md"
                },
                files);
            Assert.False(Directory.Exists(Path.Combine(repoRoot, ".ai", "generated", "context-packs")));
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void Execute_SkipCodeIndexWithCompatibleInventories_PreservesExactWarning()
    {
        string repoRoot = CreateTempRepo();
        try
        {
            FakeContextPackSelectionService service = new()
            {
                Compatibility = new ContextPackInventoryCompatibility(true, true)
            };
            ContextPackCommand command = new(service);

            CommandResult result = command.Execute(CreateOptions(repoRoot, skipCodeIndex: true));

            Assert.True(result.Success);
            Assert.Equal(1, service.CompatibilityInvocationCount);
            Assert.Equal(1, service.SelectionInvocationCount);
            using JsonDocument report = JsonDocument.Parse(result.Markdown);
            string warning = Assert.Single(report.RootElement.GetProperty("warnings").EnumerateArray()).GetString()!;
            Assert.Equal(
                "Code-index skipped by --skip-code-index; compatible inventories were used without freshness verification.",
                warning);
        }
        finally
        {
            DeleteTempRepo(repoRoot);
        }
    }

    [Fact]
    public void PublicParameterlessConstructor_RemainsAvailable()
    {
        Assert.NotNull(new ContextPackCommand());
    }

    private static BootstrapOptions CreateOptions(
        string repoRoot,
        string task = "review-risk",
        string target = "",
        int limit = 20,
        int budget = 0,
        string format = "all",
        bool skipCodeIndex = false)
    {
        return new BootstrapOptions(
            command_: "context-pack",
            repoPath_: Path.GetFullPath(repoRoot),
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
            skipCodeInventory_: skipCodeIndex,
            skipSecurityScan_: true,
            skipBudget_: true,
            skipSmoke_: true,
            skipScripts_: true,
            maxFiles_: 100,
            maxItems_: 100,
            includePrivateMembers_: false,
            noCache_: false,
            rebuildCache_: false,
            output_: ".ai/generated/inventories",
            format_: format,
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
            task_: task,
            target_: target,
            limit_: limit,
            requireContextPacks_: false,
            unknownOptions_: [],
            noProgress_: true,
            budget_: budget);
    }

    private static string CreateTempRepo()
    {
        string path = Path.Combine(Path.GetTempPath(), "airepo_context_command_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRepo(string path)
    {
        if (Directory.Exists(path))
        {
            try { Directory.Delete(path, true); } catch { }
        }
    }

    private sealed class FakeContextPackSelectionService : IContextPackSelectionService
    {
        public ContextPackInventoryCompatibility Compatibility { get; set; } = new(true, true);
        public IReadOnlyList<string> Warnings { get; set; } = [];
        public int CompatibilityInvocationCount { get; private set; }
        public int SelectionInvocationCount { get; private set; }
        public string? LastCompatibilityRepoRoot { get; private set; }
        public ContextPackRequest? LastRequest { get; private set; }
        public string? LastGeneratedAtLocal { get; private set; }

        public ContextPackInventoryCompatibility GetInventoryCompatibility(string repoRoot_)
        {
            this.CompatibilityInvocationCount++;
            this.LastCompatibilityRepoRoot = repoRoot_;
            return this.Compatibility;
        }

        public ContextPackSelectionResult Select(ContextPackRequest request_, string generatedAtLocal_)
        {
            this.SelectionInvocationCount++;
            this.LastRequest = request_;
            this.LastGeneratedAtLocal = generatedAtLocal_;
            ContextPack pack = new(
                generatedAtLocal_,
                ".",
                request_.Task,
                request_.Target,
                "implementer",
                "compact",
                "fake context pack",
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);
            return new ContextPackSelectionResult(pack, this.Warnings);
        }
    }
}
