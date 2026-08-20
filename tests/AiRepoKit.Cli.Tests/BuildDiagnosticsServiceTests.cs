using System.Globalization;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.BuildDiagnostics;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class BuildDiagnosticsServiceTests
{
    [Fact]
    public void Run_NoRootSolutionWritesSuccessfulCompatibleReports()
    {
        using TempRepo repo =
            new();

        FakeProcessRunner runner =
            new();

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.True(
            result.Completed);

        Assert.Equal(
            0,
            result.ExitCode);

        Assert.NotNull(
            result.Report);

        Assert.Equal(
            string.Empty,
            result.Report!.Target);

        Assert.Equal(
            0,
            result.Report.RestoreExitCode);

        Assert.Equal(
            0,
            result.Report.BuildExitCode);

        Assert.Equal(
            "No root solution found.",
            result.Report.Status);

        Assert.Empty(
            runner.Calls);

        Assert.True(
            File.Exists(
                repo.ReportPath));

        Assert.True(
            File.Exists(
                repo.SummaryPath));

        Assert.Equal(
            File.ReadAllText(
                repo.ReportPath),
            File.ReadAllText(
                repo.SummaryPath));

        using JsonDocument document =
            JsonDocument.Parse(
                File.ReadAllText(
                    repo.ReportPath));

        JsonElement root =
            document.RootElement;

        Assert.Equal(
            "No root solution found.",
            root
                .GetProperty(
                    "status")
                .GetString());

        Assert.False(
            root.TryGetProperty(
                "restoreOutputTail",
                out _));

        Assert.False(
            root.TryGetProperty(
                "buildOutputTail",
                out _));
    }

    [Fact]
    public void Run_EmulatesLegacySlnWildcardAndIgnoresNestedSolutions()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Zulu.sln");

        repo.WriteSolution(
            "AAA.slnx");

        repo.WriteSolution(
            "alpha.sln");

        repo.WriteSolution(
            "nested/Nested.sln");

        FakeProcessRunner runner =
            new();

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.True(
            result.Completed);

        Assert.Equal(
            "AAA.slnx",
            result.Report!.Target);

        Assert.Equal(
            2,
            runner.Calls.Count);

        string expectedSolution =
            System.IO.Path.Combine(
                repo.Path,
                "AAA.slnx");

        Assert.Equal(
            [
                "restore",
                expectedSolution
            ],
            runner
                .Calls[0]
                .Arguments
                .ToArray());

        Assert.Equal(
            [
                "build",
                expectedSolution,
                "-c",
                "Debug",
                "--no-restore"
            ],
            runner
                .Calls[1]
                .Arguments
                .ToArray());
    }

    [Fact]
    public void Run_ExecutesBuildEvenWhenRestoreFails()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        FakeProcessRunner runner =
            new();

        runner.Enqueue(
            17,
            "restore failed");

        runner.Enqueue(
            23,
            "build failed");

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.True(
            result.Completed);

        Assert.Equal(
            2,
            runner.Calls.Count);

        Assert.Equal(
            17,
            result.Report!.RestoreExitCode);

        Assert.Equal(
            23,
            result.Report.BuildExitCode);
    }

    [Fact]
    public void Run_RestoreFailureTakesExitCodePrecedence()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        FakeProcessRunner runner =
            new();

        runner.Enqueue(
            9);

        runner.Enqueue(
            14);

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.Equal(
            9,
            result.ExitCode);
    }

    [Fact]
    public void Run_BuildFailureBecomesExitCodeWhenRestoreSucceeds()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        FakeProcessRunner runner =
            new();

        runner.Enqueue(
            0);

        runner.Enqueue(
            7);

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.Equal(
            7,
            result.ExitCode);
    }

    [Fact]
    public void Run_CapsRestoreAndBuildOutputTails()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        string restoreOutput =
            string.Join(
                Environment.NewLine,
                Enumerable
                    .Range(
                        1,
                        100)
                    .Select(
                        value =>
                            $"restore-{value}"));

        string buildOutput =
            string.Join(
                Environment.NewLine,
                Enumerable
                    .Range(
                        1,
                        150)
                    .Select(
                        value =>
                            $"build-{value}"));

        FakeProcessRunner runner =
            new();

        runner.Enqueue(
            0,
            restoreOutput);

        runner.Enqueue(
            0,
            buildOutput);

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.Equal(
            80,
            result
                .Report!
                .RestoreOutputTail!
                .Count);

        Assert.Equal(
            "restore-21",
            result
                .Report
                .RestoreOutputTail[0]);

        Assert.Equal(
            "restore-100",
            result
                .Report
                .RestoreOutputTail[^1]);

        Assert.Equal(
            120,
            result
                .Report
                .BuildOutputTail!
                .Count);

        Assert.Equal(
            "build-31",
            result
                .Report
                .BuildOutputTail[0]);

        Assert.Equal(
            "build-150",
            result
                .Report
                .BuildOutputTail[^1]);
    }

    [Fact]
    public void Run_IncludesStandardErrorInDiagnosticTail()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        FakeProcessRunner runner =
            new();

        runner.Enqueue(
            0,
            "restore-out",
            "restore-error");

        runner.Enqueue(
            0,
            "build-out",
            "build-error");

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.Equal(
            [
                "restore-out",
                "restore-error"
            ],
            result
                .Report!
                .RestoreOutputTail!
                .ToArray());

        Assert.Equal(
            [
                "build-out",
                "build-error"
            ],
            result
                .Report
                .BuildOutputTail!
                .ToArray());
    }

    [Fact]
    public void Run_WithSolutionWritesHistoricalFullAndSummaryShapes()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        FakeProcessRunner runner =
            new();

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                runner)
                .Run(
                    repo.Path);

        Assert.True(
            result.Completed);

        using JsonDocument fullDocument =
            JsonDocument.Parse(
                File.ReadAllText(
                    repo.ReportPath));

        using JsonDocument summaryDocument =
            JsonDocument.Parse(
                File.ReadAllText(
                    repo.SummaryPath));

        string[] fullProperties =
            fullDocument
                .RootElement
                .EnumerateObject()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        string[] summaryProperties =
            summaryDocument
                .RootElement
                .EnumerateObject()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        Assert.Equal(
            [
                "generatedAtLocal",
                "target",
                "restoreExitCode",
                "buildExitCode",
                "restoreOutputTail",
                "buildOutputTail"
            ],
            fullProperties);

        Assert.Equal(
            [
                "generatedAtLocal",
                "target",
                "restoreExitCode",
                "buildExitCode"
            ],
            summaryProperties);

        Assert.False(
            fullDocument
                .RootElement
                .TryGetProperty(
                    "status",
                    out _));
    }

    [Fact]
    public void Run_UsesHistoricalLocalTimestampFormat()
    {
        using TempRepo repo =
            new();

        repo.WriteSolution(
            "Repo.sln");

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                new FakeProcessRunner())
                .Run(
                    repo.Path);

        bool parsed =
            DateTimeOffset.TryParseExact(
                result
                    .Report!
                    .GeneratedAtLocal,
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);

        Assert.True(
            parsed);
    }

    [Fact]
    public void Run_MissingRepositoryReturnsOperationalFailure()
    {
        string missing =
            System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "airepo-build-diagnostics-missing-" +
                Guid.NewGuid()
                    .ToString(
                        "N"));

        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                new FakeProcessRunner())
                .Run(
                    missing);

        Assert.False(
            result.Completed);

        Assert.Equal(
            1,
            result.ExitCode);

        Assert.Null(
            result.Report);

        Assert.Equal(
            "Repository root path was not found.",
            result.ErrorMessage);
    }

    [Fact]
    public void Run_BlankRepositoryReturnsOperationalFailure()
    {
        BuildDiagnosticsRunResult result =
            new BuildDiagnosticsService(
                new FakeProcessRunner())
                .Run(
                    " ");

        Assert.False(
            result.Completed);

        Assert.Equal(
            1,
            result.ExitCode);

        Assert.Equal(
            "Repository root path cannot be empty.",
            result.ErrorMessage);
    }

    private sealed class FakeProcessRunner :
        IProcessRunner
    {
        private readonly Queue<ProcessResult> _results =
            new();

        public List<ProcessCall> Calls { get; } =
            [];

        public void Enqueue(
            int exitCode,
            string standardOutput = "",
            string standardError = "")
        {
            _results.Enqueue(
                new ProcessResult(
                    "dotnet",
                    string.Empty,
                    string.Empty,
                    exitCode,
                    standardOutput,
                    standardError));
        }

        public ProcessResult Run(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory)
        {
            string[] materializedArguments =
                arguments.ToArray();

            Calls.Add(
                new ProcessCall(
                    fileName,
                    materializedArguments,
                    workingDirectory));

            if (_results.Count > 0)
            {
                ProcessResult configured =
                    _results.Dequeue();

                return configured with
                {
                    FileName =
                        fileName,
                    Arguments =
                        string.Join(
                            " ",
                            materializedArguments),
                    WorkingDirectory =
                        workingDirectory
                };
            }

            return new ProcessResult(
                fileName,
                string.Join(
                    " ",
                    materializedArguments),
                workingDirectory,
                0,
                string.Empty,
                string.Empty);
        }
    }

    private sealed record ProcessCall(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory);

    private sealed class TempRepo :
        IDisposable
    {
        public TempRepo()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "airepo-build-diagnostics-" +
                    Guid.NewGuid()
                        .ToString(
                            "N"));

            Directory.CreateDirectory(
                Path);
        }

        public string Path { get; }

        public string ReportPath =>
            System.IO.Path.Combine(
                Path,
                ".ai",
                "generated",
                "reports",
                "build-diagnostics-report.json");

        public string SummaryPath =>
            System.IO.Path.Combine(
                Path,
                ".ai",
                "generated",
                "reports",
                "latest-build-summary.json");

        public void WriteSolution(
            string relativePath)
        {
            string fullPath =
                System.IO.Path.Combine(
                    Path,
                    relativePath.Replace(
                        '/',
                        System.IO.Path.DirectorySeparatorChar));

            string? directory =
                System.IO.Path.GetDirectoryName(
                    fullPath);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                fullPath,
                string.Empty);
        }

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
