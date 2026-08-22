using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Services.BuildDiagnostics;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class BuildDiagnosticsCommandTests
{
    [Fact]
    public void Execute_SuccessReturnsSuccessfulCommandResult()
    {
        using TempRepo repo =
            new();

        BuildDiagnosticsReport report =
            CreateReport(
                "Repo.sln",
                0,
                0);

        FakeBuildDiagnosticsService service =
            new(
                BuildDiagnosticsRunResult.Complete(
                    report,
                    0));

        BootstrapOptions options =
            Program.Parse(
                [
                    "build-diagnostics",
                    "--repo",
                    repo.Path
                ]);

        CommandResult result =
            new BuildDiagnosticsCommand(
                service)
                .Execute(
                    options);

        Assert.True(
            result.Success);

        Assert.Equal(
            0,
            result.ExitCode);

        Assert.Contains(
            "# Build Diagnostics",
            result.Markdown);

        Assert.Contains(
            "Repo.sln",
            result.Markdown);

        Assert.Contains(
            BuildDiagnosticsService.ReportRelativePath,
            result.Markdown);

        Assert.Contains(
            BuildDiagnosticsService.SummaryRelativePath,
            result.Markdown);
    }

    [Fact]
    public void Execute_NonzeroNativeResultPreservesExitCode()
    {
        using TempRepo repo =
            new();

        BuildDiagnosticsReport report =
            CreateReport(
                "Repo.sln",
                17,
                23);

        FakeBuildDiagnosticsService service =
            new(
                BuildDiagnosticsRunResult.Complete(
                    report,
                    17));

        BootstrapOptions options =
            Program.Parse(
                [
                    "build-diagnostics",
                    "--repo",
                    repo.Path
                ]);

        CommandResult result =
            new BuildDiagnosticsCommand(
                service)
                .Execute(
                    options);

        Assert.False(
            result.Success);

        Assert.Equal(
            17,
            result.ExitCode);

        Assert.Contains(
            "Restore exit code: `17`",
            result.Markdown);

        Assert.Contains(
            "Build exit code: `23`",
            result.Markdown);
    }

    [Fact]
    public void Execute_OperationalFailureReturnsRedactedFailure()
    {
        using TempRepo repo =
            new();

        FakeBuildDiagnosticsService service =
            new(
                BuildDiagnosticsRunResult.Failure(
                    "password=should-not-appear"));

        BootstrapOptions options =
            Program.Parse(
                [
                    "build-diagnostics",
                    "--repo",
                    repo.Path
                ]);

        CommandResult result =
            new BuildDiagnosticsCommand(
                service)
                .Execute(
                    options);

        Assert.False(
            result.Success);

        Assert.Equal(
            1,
            result.ExitCode);

        Assert.DoesNotContain(
            "should-not-appear",
            result.Markdown);

        Assert.Contains(
            "[redacted sensitive line]",
            result.Markdown);
    }

    [Fact]
    public void Execute_NoSolutionStatusIsSurfaced()
    {
        using TempRepo repo =
            new();

        BuildDiagnosticsReport report =
            new()
            {
                GeneratedAtLocal =
                    "2026-08-20 12:00:00 +02:00",
                Target =
                    string.Empty,
                RestoreExitCode =
                    0,
                BuildExitCode =
                    0,
                Status =
                    "No root solution found."
            };

        FakeBuildDiagnosticsService service =
            new(
                BuildDiagnosticsRunResult.Complete(
                    report,
                    0));

        BootstrapOptions options =
            Program.Parse(
                [
                    "build-diagnostics",
                    "--repo",
                    repo.Path
                ]);

        CommandResult result =
            new BuildDiagnosticsCommand(
                service)
                .Execute(
                    options);

        Assert.True(
            result.Success);

        Assert.Contains(
            "No root solution found.",
            result.Markdown);
    }

    [Fact]
    public async Task Program_MainRegistersBuildDiagnosticsCommand()
    {
        using TempRepo repo =
            new();

        TextWriter previousOut =
            Console.Out;

        StringWriter writer =
            new();

        try
        {
            Console.SetOut(
                writer);

            int exitCode =
                await Program.Main(
                    [
                        "build-diagnostics",
                        "--repo",
                        repo.Path
                    ]);

            Assert.Equal(
                0,
                exitCode);

            string output =
                writer.ToString();

            Assert.Contains(
                "# Build Diagnostics",
                output);

            Assert.Contains(
                "No root solution found.",
                output);

            Assert.True(
                File.Exists(
                    Path.Combine(
                        repo.Path,
                        ".ai",
                        "generated",
                        "reports",
                        "build-diagnostics-report.json")));
        }
        finally
        {
            Console.SetOut(
                previousOut);
        }
    }

    [Fact]
    public async Task Program_HelpIncludesBuildDiagnosticsCommand()
    {
        TextWriter previousOut =
            Console.Out;

        StringWriter writer =
            new();

        try
        {
            Console.SetOut(
                writer);

            int exitCode =
                await Program.Main(
                    ["--help"]);

            Assert.Equal(
                0,
                exitCode);

            Assert.Contains(
                "airepo build-diagnostics [--repo <path>]",
                writer.ToString());
        }
        finally
        {
            Console.SetOut(
                previousOut);
        }
    }

    private static BuildDiagnosticsReport CreateReport(
        string target,
        int restoreExitCode,
        int buildExitCode)
    {
        return new BuildDiagnosticsReport
        {
            GeneratedAtLocal =
                "2026-08-20 12:00:00 +02:00",
            Target =
                target,
            RestoreExitCode =
                restoreExitCode,
            BuildExitCode =
                buildExitCode,
            RestoreOutputTail =
                [],
            BuildOutputTail =
                []
        };
    }

    private sealed class FakeBuildDiagnosticsService :
        IBuildDiagnosticsService
    {
        private readonly BuildDiagnosticsRunResult _result;

        public FakeBuildDiagnosticsService(
            BuildDiagnosticsRunResult result)
        {
            _result =
                result;
        }

        public BuildDiagnosticsRunResult Run(
            string repoRoot)
        {
            return _result;
        }
    }

    private sealed class TempRepo :
        IDisposable
    {
        public TempRepo()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "airepo-build-diagnostics-command-" +
                    Guid.NewGuid()
                        .ToString(
                            "N"));

            Directory.CreateDirectory(
                Path);

            Directory.CreateDirectory(
                System.IO.Path.Combine(
                    Path,
                    ".ai"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(
                    Path,
                    true);
            }
            catch
            {
                // Best-effort cleanup for test temporary directories.
            }
        }
    }
}
