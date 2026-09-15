using System.Text.Json;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Commands.Spec;
using AiRepoKit.Cli.McpRuntime.Services;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class V3MigrationAcceptanceTests
{
    [Fact]
    public void ToolVersion_ReportsThreeZeroZero()
    {
        string toolVersion = TemplateService.GetToolVersion();
        Assert.Equal("3.0.0", toolVersion);
    }

    [Fact]
    public void CliCsproj_HasVersionThreeZeroZero_AndSpecIsNotPackable()
    {
        string baseDir = AppContext.BaseDirectory;
        string repoRoot = ResolveRepoRoot(baseDir);

        string cliCsprojPath = Path.Combine(repoRoot, "src", "AiRepoKit.Cli", "AiRepoKit.Cli.csproj");
        Assert.True(File.Exists(cliCsprojPath), $"CLI project file not found at: {cliCsprojPath}");
        string cliContent = File.ReadAllText(cliCsprojPath);
        Assert.Contains("<Version>3.0.0</Version>", cliContent);
        Assert.Contains("<ToolCommandName>airepo</ToolCommandName>", cliContent);
        Assert.Contains("<PackageId>AiRepoKit.Cli</PackageId>", cliContent);

        string specCsprojPath = Path.Combine(repoRoot, "src", "AiRepoKit.Spec", "AiRepoKit.Spec.csproj");
        Assert.True(File.Exists(specCsprojPath), $"Spec project file not found at: {specCsprojPath}");
        string specContent = File.ReadAllText(specCsprojPath);
        Assert.Contains("<IsPackable>false</IsPackable>", specContent);
    }

    [Fact]
    public void V2Repository_WithoutSpecs_RemainsWithoutSpecsAfterSafeWorkflows()
    {
        using TestRepo repo = new();
        string specsDir = Path.Combine(repo.Root, ".ai", "specs");
        Assert.False(Directory.Exists(specsDir));

        // 1. Audit
        CommandResult auditResult = new AuditCommand().Execute(CreateOptions(repo.Root, "audit"));
        Assert.False(Directory.Exists(specsDir), "Audit created .ai/specs unexpectedly");

        // 2. SelfCheck quick
        CommandResult selfCheckResult = new SelfCheckCommand().Execute(CreateOptions(repo.Root, "self-check", quick_: true));
        Assert.False(Directory.Exists(specsDir), "SelfCheck created .ai/specs unexpectedly");

        // 3. MCP diagnose quick
        CommandResult mcpDiagnoseResult = new McpDiagnoseCommand().Execute(CreateOptions(repo.Root, "mcp-diagnose", quick_: true));
        Assert.False(Directory.Exists(specsDir), "McpDiagnose created .ai/specs unexpectedly");
    }

    [Fact]
    public void V3Installation_DoesNotCreateSpecCanonicalState()
    {
        using TestRepo repo = new();
        string specsDir = Path.Combine(repo.Root, ".ai", "specs");

        // Running help, version, or read-only commands does not touch or create .ai/specs
        Assert.False(Directory.Exists(specsDir));

        ContextRepository mcpRepo = new(new ContextRepositoryOptions(repo.Root), new SecretRedactor());
        var health = mcpRepo.GetCapabilities();
        Assert.NotNull(health);
        Assert.False(Directory.Exists(specsDir), "MCP capabilities lookup created .ai/specs unexpectedly");
    }

    [Fact]
    public void SpecInit_DryRunByDefault_CreatesNoCanonicalState()
    {
        using TestRepo repo = new();
        const string specId = "spec-dryrun-test";
        string reqCandidate = repo.WriteCandidate("req.json", CreateRequirementSetCandidate());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", specId,
            "--from", reqCandidate,
            "--repo", repo.Root
        ]);

        Assert.True(result.Success, $"Spec init failed: {result.Markdown}");
        Assert.Equal(0, result.ExitCode);

        string canonicalReqPath = Path.Combine(repo.Root, ".ai", "specs", specId, "requirements.json");
        Assert.False(File.Exists(canonicalReqPath), "Spec init without --apply wrote requirements.json to disk");
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".ai", "specs", specId)), "Spec directory was created during dry-run");
    }

    [Fact]
    public void SpecInit_Apply_CreatesCanonicalStateOnlyWhenRequested()
    {
        using TestRepo repo = new();
        const string specId = "spec-apply-test";
        string reqCandidate = repo.WriteCandidate("req.json", CreateRequirementSetCandidate());

        CommandResult result = new SpecCommand().Execute([
            "init",
            "--spec-id", specId,
            "--from", reqCandidate,
            "--repo", repo.Root,
            "--apply"
        ]);

        Assert.True(result.Success, $"Spec init --apply failed: {result.Markdown}");
        Assert.Equal(0, result.ExitCode);

        string canonicalReqPath = Path.Combine(repo.Root, ".ai", "specs", specId, "requirements.json");
        Assert.True(File.Exists(canonicalReqPath), "Canonical requirements.json was not created after --apply");
    }

    [Fact]
    public void LegacyPlanCommand_RemainsAvailableAndDistinctFromSpecPlan()
    {
        using TestRepo repo = new();

        // Top-level airepo plan (legacy v1/v2 infrastructure planning)
        PlanCommand legacyPlan = new();
        CommandResult legacyResult = legacyPlan.Execute(CreateOptions(repo.Root, "plan", dryRun_: true));
        Assert.True(legacyResult.Success, $"Legacy plan command failed: {legacyResult.Markdown}");
        Assert.False(Directory.Exists(Path.Combine(repo.Root, ".ai", "specs")), "Legacy plan created .ai/specs");

        // Spec plan command (governing Spec IR ImplementationPlan)
        SpecCommand specCommand = new();
        CommandResult specHelpResult = specCommand.Execute(["plan"]);
        // Without required --spec-id and --from, spec plan fails with usage or error rather than executing legacy plan
        Assert.False(specHelpResult.Success);
        Assert.Equal(1, specHelpResult.ExitCode);
    }

    [Fact]
    public void PortableMcp_RepositoryIsolation_RejectsPathTraversal()
    {
        using TestRepo repo = new();
        ContextRepository mcpRepo = new(new ContextRepositoryOptions(repo.Root), new SecretRedactor());

        // Attempting to read outside repository root via traversal should return error / rejected
        object result = mcpRepo.ReadContextObject("spec", Cli.McpRuntime.Models.ContextDetail.Brief, 10, null, "../outside-spec");
        string json = JsonSerializer.Serialize(result);
        Assert.Contains("INVALID_SPEC_ID", json);
    }

    private static BootstrapOptions CreateOptions(string repoRoot_, string command_, bool quick_ = false, bool dryRun_ = true)
    {
        List<string> args = [command_, "--repo", repoRoot_];
        if (quick_)
        {
            args.Add("--quick");
        }
        if (dryRun_)
        {
            args.Add("--dry-run");
        }
        args.Add("--no-progress");
        return Program.Parse(args.ToArray());
    }

    private static RequirementSet CreateRequirementSetCandidate()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput
                {
                    Id = new StableEntityId("INPUT-001"),
                    Text = "Test requirement for v3 migration acceptance."
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "The system shall verify v3 migration behavior.",
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                }
            ]
        };
    }

    private static string ResolveRepoRoot(string baseDir_)
    {
        DirectoryInfo? dir = new(baseDir_);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI.RepoKit.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return Path.GetFullPath(Path.Combine(baseDir_, "..", "..", "..", ".."));
    }

    private sealed class TestRepo : IDisposable
    {
        public string Root { get; }

        public TestRepo()
        {
            this.Root = Path.Combine(Path.GetTempPath(), "airepokit-v3migration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this.Root);
            Directory.CreateDirectory(Path.Combine(this.Root, ".git"));
        }

        public string WriteCandidate<T>(string fileName_, T obj_)
        {
            string path = Path.Combine(this.Root, fileName_);
            File.WriteAllText(path, SpecJsonSerializer.Serialize(obj_));
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Root))
                {
                    Directory.Delete(this.Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
