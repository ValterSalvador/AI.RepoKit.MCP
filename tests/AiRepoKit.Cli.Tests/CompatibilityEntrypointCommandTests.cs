using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services.AiContextUpdate;
using AiRepoKit.Cli.Services.McpBudget;
using AiRepoKit.Cli.Services.SdkAlignment;
using AiRepoKit.Cli.Services.SecretScan;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class CompatibilityEntrypointCommandTests
{
    [Fact]
    public void AiContextUpdate_ForwardsNativeOptions()
    {
        using TempRepo repo =
            new();

        FakeAiContextUpdateService service =
            new();

        BootstrapOptions options =
            Program.Parse(
                [
                    "ai-context-update",
                    "--repo",
                    repo.Path,
                    "--target-framework",
                    "net9.0",
                    "--mcp-server-name",
                    "custom_server",
                    "--mcp-project-relative-path",
                    "Tools/Custom/Custom.csproj"
                ]);

        CommandResult result =
            new AiContextUpdateCommand(
                service)
                .Execute(
                    options);

        Assert.True(
            result.Success);

        Assert.Equal(
            repo.Path,
            service.RepositoryRoot);

        Assert.Equal(
            "net9.0",
            service.Options?.TargetFramework);

        Assert.Equal(
            "custom_server",
            service.Options?.McpServerName);

        Assert.Equal(
            "Tools/Custom/Custom.csproj",
            service.Options?.McpProjectRelativePath);
    }

    [Fact]
    public void SdkAlignment_UsesNativeService()
    {
        using TempRepo repo =
            new();

        FakeSdkAlignmentService service =
            new();

        BootstrapOptions options =
            Program.Parse(
                [
                    "sdk-alignment",
                    "--repo",
                    repo.Path
                ]);

        CommandResult result =
            new SdkAlignmentCommand(
                service)
                .Execute(
                    options);

        Assert.True(
            result.Success);

        Assert.Equal(
            repo.Path,
            service.RepositoryRoot);

        Assert.Contains(
            "sdk-alignment-report.json",
            result.Markdown);
    }

    [Fact]
    public void SecretScan_UsesNativeServiceAndReportsFindingsWithoutSecrets()
    {
        using TempRepo repo =
            new();

        FakeSecretScanService service =
            new();

        BootstrapOptions options =
            Program.Parse(
                [
                    "secret-scan",
                    "--repo",
                    repo.Path
                ]);

        CommandResult result =
            new SecretScanCommand(
                service)
                .Execute(
                    options);

        Assert.True(
            result.Success);

        Assert.Equal(
            repo.Path,
            service.RepositoryRoot);

        Assert.Contains(
            "Findings: `2`",
            result.Markdown);

        Assert.Contains(
            "Secret values returned: `False`",
            result.Markdown);
    }

    [Fact]
    public void McpBudget_PreservesTimeoutsJsonAndExitClass()
    {
        using TempRepo repo =
            new();

        FakeMcpBudgetService service =
            new();

        BootstrapOptions options =
            Program.Parse(
                [
                    "mcp-budget",
                    "--repo",
                    repo.Path,
                    "--startup-timeout-seconds",
                    "11",
                    "--tool-timeout-seconds",
                    "12",
                    "--json"
                ]);

        CommandResult result =
            new McpBudgetCommand(
                service)
                .Execute(
                    options);

        Assert.False(
            result.Success);

        Assert.Equal(
            2,
            result.ExitCode);

        Assert.Equal(
            11,
            service.Options?.StartupTimeoutSeconds);

        Assert.Equal(
            12,
            service.Options?.ToolTimeoutSeconds);

        Assert.Contains(
            "\"Passed\": false",
            result.Markdown);
    }

    [Fact]
    public void McpBudget_InvalidTimeoutIsExplicitParseFailure()
    {
        using TempRepo repo =
            new();

        BootstrapOptions options =
            Program.Parse(
                [
                    "mcp-budget",
                    "--repo",
                    repo.Path,
                    "--startup-timeout-seconds",
                    "0"
                ]);

        Assert.Contains(
            options.UnknownOptions,
            value =>
                value.Contains(
                    "positive integer",
                    StringComparison.Ordinal));
    }

    private sealed class FakeAiContextUpdateService :
        IAiContextUpdateService
    {
        public string? RepositoryRoot { get; private set; }

        public AiContextUpdateOptions? Options { get; private set; }

        public AiContextUpdateRunResult Run(
            string repoRoot,
            AiContextUpdateOptions? options = null)
        {
            this.RepositoryRoot =
                repoRoot;

            this.Options =
                options;

            return AiContextUpdateRunResult.Success();
        }
    }

    private sealed class FakeSdkAlignmentService :
        ISdkAlignmentService
    {
        public string? RepositoryRoot { get; private set; }

        public SdkAlignmentRunResult Run(
            string repoRoot)
        {
            this.RepositoryRoot =
                repoRoot;

            return SdkAlignmentRunResult.Success(
                new SdkAlignmentReport());
        }
    }

    private sealed class FakeSecretScanService :
        ISecretScanService
    {
        public string? RepositoryRoot { get; private set; }

        public SecretScanRunResult Run(
            string repoRoot)
        {
            this.RepositoryRoot =
                repoRoot;

            return SecretScanRunResult.Success(
                new SecretScanReport
                {
                    FindingCount =
                        2,
                    RedactedOnly =
                        true,
                    SecretValuesReturned =
                        false,
                    SecretsExposed =
                        false
                });
        }
    }

    private sealed class FakeMcpBudgetService :
        IMcpBudgetService
    {
        public McpBudgetOptions? Options { get; private set; }

        public McpBudgetRunResult Run(
            string repoRoot,
            McpBudgetOptions? options = null)
        {
            this.Options =
                options;

            return new McpBudgetRunResult(
                McpBudgetExitClass.ValidationFailure,
                new McpBudgetReport
                {
                    GeneratedAtLocal =
                        "2026-08-20 14:00:00",
                    RepoRoot =
                        repoRoot,
                    McpAssembly =
                        "test.dll",
                    McpAssemblyExists =
                        true,
                    Manifest =
                        "manifest.json",
                    ToolsListed =
                        [],
                    Results =
                        [],
                    Passed =
                        false,
                    Failures =
                        ["budget failure"],
                    Warnings =
                        [],
                    StderrLineCount =
                        0,
                    StdoutLineCount =
                        0
                });
        }
    }

    private sealed class TempRepo :
        IDisposable
    {
        public TempRepo()
        {
            this.Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "airepo_p03_entrypoint_" +
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(
                this.Path);

            Directory.CreateDirectory(
                System.IO.Path.Combine(
                    this.Path,
                    ".ai"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(
                    this.Path,
                    true);
            }
            catch
            {
            }
        }
    }
}
