using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.SdkAlignment;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class SdkAlignmentServiceTests
{
    [Fact]
    public void Run_CapturesTrimmedDotNetVersion()
    {
        using TempRepo repo = new();
        repo.WriteProject(
            "App.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        FakeProcessRunner runner = new()
        {
            VersionOutput = " 10.0.111 \n"
        };

        SdkAlignmentRunResult result =
            new SdkAlignmentService(runner).Run(repo.Root);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "10.0.111",
            Assert.IsType<SdkAlignmentReport>(
                result.Report).DotNetSdkVersion);
        Assert.Equal(
            ["dotnet --version", "dotnet --list-sdks"],
            runner.Calls);
    }

    [Fact]
    public void Run_SplitsSdkListAndPreservesOrder()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner = new()
        {
            SdksOutput =
                "10.0.111 [/sdk]\r\n" +
                "9.0.200 [/sdk]\n"
        };

        SdkAlignmentRunResult result =
            new SdkAlignmentService(runner).Run(repo.Root);

        Assert.Equal(
            ["10.0.111 [/sdk]", "9.0.200 [/sdk]"],
            Assert.IsType<SdkAlignmentReport>(
                result.Report).DotNetSdks);
    }

    [Fact]
    public void Run_ReadsTargetFramework()
    {
        using TempRepo repo = new();
        repo.WriteProject(
            "App.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            "net10.0",
            result.Report!.Projects.Single().TargetFrameworks);
    }

    [Fact]
    public void Run_ReadsTargetFrameworks()
    {
        using TempRepo repo = new();
        repo.WriteProject(
            "App.csproj",
            "<TargetFrameworks>net9.0;net10.0</TargetFrameworks>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            "net10.0;net9.0",
            result.Report!.Projects.Single().TargetFrameworks);
    }

    [Fact]
    public void Run_RemovesDuplicateFrameworks()
    {
        using TempRepo repo = new();
        repo.WriteProject(
            "App.csproj",
            "<TargetFrameworks>net10.0;net10.0;net9.0</TargetFrameworks>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            "net10.0;net9.0",
            result.Report!.Projects.Single().TargetFrameworks);
    }

    [Fact]
    public void Run_UsesOrdinalFrameworkOrdering()
    {
        using TempRepo repo = new();
        repo.WriteProject(
            "App.csproj",
            "<TargetFrameworks>net8.0;NET7.0</TargetFrameworks>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            "NET7.0;net8.0",
            result.Report!.Projects.Single().TargetFrameworks);
    }

    [Fact]
    public void Run_IgnoresConfiguredPathSegments()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "obj/Ignored.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        repo.WriteProject(
            "src/Included.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            "src/Included.csproj",
            result.Report!.Projects.Single().Project);
    }

    [Fact]
    public void Run_IgnoresTopLevelBuildDirectories()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            ".build-temp/Ignored.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        repo.WriteProject(
            "Included.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            ["Included.csproj"],
            result.Report!.Projects
                .Select(project => project.Project));
    }

    [Fact]
    public void Run_IgnoresTopLevelBuildPrefixedProjectFile()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            ".build-generated.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        repo.WriteProject(
            "Included.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            ["Included.csproj"],
            result.Report!.Projects
                .Select(project => project.Project));
    }

    [Fact]
    public void Run_DoesNotIgnoreNestedBuildDirectory()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "src/.build-temp/Included.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            "src/.build-temp/Included.csproj",
            result.Report!.Projects.Single().Project);
    }

    [Fact]
    public void Run_UsesRelativeNormalizedProjectPaths()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "src/lib/App.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        string project =
            result.Report!.Projects.Single().Project;

        Assert.Equal("src/lib/App.csproj", project);
        Assert.DoesNotContain(repo.Root, project);
        Assert.DoesNotContain('\\', project);
    }

    [Fact]
    public void Run_SortsProjectsDeterministically()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "z/Z.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        repo.WriteProject(
            "a/A.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        SdkAlignmentRunResult result =
            RunSuccess(repo);

        Assert.Equal(
            ["a/A.csproj", "z/Z.csproj"],
            result.Report!.Projects
                .Select(project => project.Project));
    }

    [Fact]
    public void Run_WritesPascalCaseJsonContract()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "App.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        RunSuccess(repo);

        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllText(repo.ReportPath));

        JsonElement root = document.RootElement;

        Assert.True(root.TryGetProperty(
            "ExpectedTargetFramework",
            out _));

        Assert.True(root.TryGetProperty(
            "DotNetSdkVersion",
            out _));

        Assert.True(root.TryGetProperty(
            "DotNetSdks",
            out _));

        JsonElement project = root
            .GetProperty("Projects")[0];

        Assert.True(project.TryGetProperty(
            "Project",
            out _));

        Assert.True(project.TryGetProperty(
            "TargetFrameworks",
            out _));
    }

    [Fact]
    public void Run_CreatesReportAtExpectedPath()
    {
        using TempRepo repo = new();

        RunSuccess(repo);

        Assert.True(File.Exists(repo.ReportPath));

        string json = File.ReadAllText(repo.ReportPath);

        Assert.Contains(
            "\n  \"ExpectedTargetFramework\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_VersionFailureReturnsFailure()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner = new()
        {
            VersionExitCode = 7
        };

        SdkAlignmentRunResult result =
            new SdkAlignmentService(runner).Run(repo.Root);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);
        Assert.Contains(
            "dotnet --version failed",
            result.ErrorMessage,
            StringComparison.Ordinal);

        Assert.Equal(
            ["dotnet --version"],
            runner.Calls);
    }

    [Fact]
    public void Run_ListSdksFailureReturnsFailure()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner = new()
        {
            SdksExitCode = 8
        };

        SdkAlignmentRunResult result =
            new SdkAlignmentService(runner).Run(repo.Root);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Report);

        Assert.Contains(
            "dotnet --list-sdks failed",
            result.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ProcessFailureDoesNotWriteReport()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner = new()
        {
            SdksExitCode = 1
        };

        SdkAlignmentRunResult result =
            new SdkAlignmentService(runner).Run(repo.Root);

        Assert.False(result.IsSuccess);
        Assert.False(File.Exists(repo.ReportPath));
    }

    private static SdkAlignmentRunResult RunSuccess(
        TempRepo repo)
    {
        SdkAlignmentRunResult result =
            new SdkAlignmentService(
                new FakeProcessRunner())
            .Run(repo.Root);

        Assert.True(
            result.IsSuccess,
            result.ErrorMessage);

        Assert.NotNull(result.Report);

        return result;
    }

    private sealed class FakeProcessRunner :
        IProcessRunner
    {
        public string VersionOutput { get; init; } =
            "10.0.111";

        public string SdksOutput { get; init; } =
            "10.0.111 [/sdk]";

        public int VersionExitCode { get; init; }

        public int SdksExitCode { get; init; }

        public List<string> Calls { get; } = [];

        public ProcessResult Run(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory)
        {
            string[] args = arguments.ToArray();

            Calls.Add(
                $"{fileName} {string.Join(" ", args)}");

            if (args.SequenceEqual(["--version"]))
            {
                return new ProcessResult(
                    fileName,
                    "--version",
                    workingDirectory,
                    VersionExitCode,
                    VersionOutput,
                    VersionExitCode == 0
                        ? string.Empty
                        : "version failure");
            }

            if (args.SequenceEqual(["--list-sdks"]))
            {
                return new ProcessResult(
                    fileName,
                    "--list-sdks",
                    workingDirectory,
                    SdksExitCode,
                    SdksOutput,
                    SdksExitCode == 0
                        ? string.Empty
                        : "sdk list failure");
            }

            return new ProcessResult(
                fileName,
                string.Join(" ", args),
                workingDirectory,
                1,
                string.Empty,
                "unexpected command");
        }
    }

    private sealed class TempRepo : IDisposable
    {
        public TempRepo()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"airepo_sdk_alignment_{Guid.NewGuid():N}");

            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string ReportPath => Path.Combine(
            Root,
            ".ai",
            "generated",
            "reports",
            "sdk-alignment-report.json");

        public void WriteProject(
            string relativePath,
            string properties)
        {
            string path = Path.Combine(
                Root,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

            string? directory =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                path,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    {properties}
                  </PropertyGroup>
                </Project>
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(
                    Root,
                    recursive: true);
            }
        }
    }
}
