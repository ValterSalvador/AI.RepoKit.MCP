using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.AiContextUpdate;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class AiContextUpdateServiceTests
{
    [Fact]
    public void Run_CreatesAllExpectedArtifacts()
    {
        using TempRepo repo = new();

        AiContextUpdateRunResult result =
            RunSuccess(repo);

        string[] expected =
        [
            ".ai/manifests/mcp-context-manifest.json",
            ".ai/generated/inventories/project-inventory.json",
            ".ai/generated/inventories/project-references.json",
            ".ai/generated/inventories/package-inventory.json",
            ".ai/generated/inventories/sdk-inventory.json",
            ".ai/generated/summaries/generated-context-summary.md"
        ];

        foreach (string relativePath in expected)
        {
            Assert.True(
                File.Exists(
                    repo.PathOf(relativePath)),
                relativePath);
        }
    }

    [Fact]
    public void Run_UsesRootSolutionSortedDeterministically()
    {
        using TempRepo repo = new();

        repo.WriteFile(
            "Zeta.sln",
            string.Empty);

        repo.WriteFile(
            "Alpha.sln",
            string.Empty);

        repo.WriteFile(
            "nested/BeforeAlpha.sln",
            string.Empty);

        RunSuccess(repo);

        using JsonDocument document =
            repo.ReadJson(
                ".ai/manifests/mcp-context-manifest.json");

        Assert.Equal(
            "Alpha.sln",
            document.RootElement
                .GetProperty("mainSolution")
                .GetString());
    }

    [Fact]
    public void Run_DiscoversProjectsAndNormalizesPaths()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "z/Zeta.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        repo.WriteProject(
            "a/Alpha.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        RunSuccess(repo);

        using JsonDocument document =
            repo.ReadJson(
                ".ai/generated/inventories/project-inventory.json");

        string[] projects =
            document.RootElement
                .GetProperty("projects")
                .EnumerateArray()
                .Select(item =>
                    item.GetProperty("path").GetString()!)
                .ToArray();

        Assert.Equal(
            new[]
            {
                "a/Alpha.csproj",
                "z/Zeta.csproj"
            },
            projects);

        Assert.All(
            projects,
            path =>
            {
                Assert.False(
                    Path.IsPathRooted(path));

                Assert.DoesNotContain(
                    '\\',
                    path);
            });
    }

    [Fact]
    public void Run_IgnoresConfiguredPathSegments()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "src/App.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        repo.WriteProject(
            "obj/Ignored.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        repo.WriteProject(
            "src/bin/Ignored.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        repo.WriteProject(
            "src/node_modules/Ignored.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        RunSuccess(repo);

        Assert.Equal(
            new[] { "src/App.csproj" },
            repo.ReadProjectPaths());
    }

    [Fact]
    public void Run_IgnoresTopLevelBuildDirectory()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            ".build-temp/Ignored.csproj",
            "<TargetFramework>net8.0</TargetFramework>");

        repo.WriteProject(
            "Included.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        RunSuccess(repo);

        Assert.Equal(
            new[] { "Included.csproj" },
            repo.ReadProjectPaths());
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

        RunSuccess(repo);

        Assert.Equal(
            new[] { "Included.csproj" },
            repo.ReadProjectPaths());
    }

    [Fact]
    public void Run_DoesNotIgnoreNestedBuildDirectory()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "src/.build-temp/Included.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        RunSuccess(repo);

        Assert.Equal(
            new[]
            {
                "src/.build-temp/Included.csproj"
            },
            repo.ReadProjectPaths());
    }

    [Fact]
    public void Run_ReadsAndSortsTargetFrameworks()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "App.csproj",
            """
            <TargetFramework>net10.0</TargetFramework>
            <TargetFrameworks>net9.0;net10.0;NET8.0</TargetFrameworks>
            """);

        RunSuccess(repo);

        using JsonDocument document =
            repo.ReadJson(
                ".ai/generated/inventories/project-inventory.json");

        string[] frameworks =
            document.RootElement
                .GetProperty("projects")[0]
                .GetProperty("targetFrameworks")
                .EnumerateArray()
                .Select(item =>
                    item.GetString()!)
                .ToArray();

        Assert.Equal(
            new[]
            {
                "NET8.0",
                "net10.0",
                "net9.0"
            },
            frameworks);
    }

    [Fact]
    public void Run_CollectsProjectReferences()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "src/App/App.csproj",
            "<TargetFramework>net10.0</TargetFramework>",
            """
            <ProjectReference Include="..\Lib\Lib.csproj" />
            """);

        repo.WriteProject(
            "src/Lib/Lib.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        RunSuccess(repo);

        using JsonDocument document =
            repo.ReadJson(
                ".ai/generated/inventories/project-references.json");

        JsonElement reference =
            document.RootElement
                .GetProperty("references")[0];

        Assert.Equal(
            "src/App/App.csproj",
            reference
                .GetProperty("project")
                .GetString());

        Assert.Equal(
            "../Lib/Lib.csproj",
            reference
                .GetProperty("reference")
                .GetString());
    }

    [Fact]
    public void Run_CollectsPackageReferences()
    {
        using TempRepo repo = new();

        repo.WriteProject(
            "App.csproj",
            "<TargetFramework>net10.0</TargetFramework>",
            """
            <PackageReference Include="One" Version="1.2.3" />
            <PackageReference Include="Two">
              <Version>2.0.0</Version>
            </PackageReference>
            <PackageReference Include="Three" />
            """);

        RunSuccess(repo);

        using JsonDocument document =
            repo.ReadJson(
                ".ai/generated/inventories/package-inventory.json");

        JsonElement[] packages =
            document.RootElement
                .GetProperty("packages")
                .EnumerateArray()
                .ToArray();

        Assert.Equal(
            3,
            packages.Length);

        Assert.Equal(
            "One",
            packages[0]
                .GetProperty("package")
                .GetString());

        Assert.Equal(
            "1.2.3",
            packages[0]
                .GetProperty("version")
                .GetString());

        Assert.Equal(
            "Two",
            packages[1]
                .GetProperty("package")
                .GetString());

        Assert.Equal(
            "2.0.0",
            packages[1]
                .GetProperty("version")
                .GetString());

        Assert.Equal(
            string.Empty,
            packages[2]
                .GetProperty("version")
                .GetString());
    }

    [Fact]
    public void Run_UsesRuntimeOptionsInGeneratedContracts()
    {
        using TempRepo repo = new();

        AiContextUpdateOptions options =
            new()
            {
                TargetFramework = "net9.0",
                McpServerName = "custom_context",
                McpProjectRelativePath =
                    "src/Mcp/Custom.Mcp.csproj"
            };

        RunSuccess(
            repo,
            options: options);

        using JsonDocument manifest =
            repo.ReadJson(
                ".ai/manifests/mcp-context-manifest.json");

        JsonElement root =
            manifest.RootElement;

        Assert.Equal(
            "net9.0",
            root
                .GetProperty("targetFramework")
                .GetString());

        Assert.Equal(
            "custom_context",
            root
                .GetProperty("mcpServerName")
                .GetString());

        Assert.Equal(
            "src/Mcp/Custom.Mcp.csproj",
            root
                .GetProperty("mcpProjectRelativePath")
                .GetString());

        using JsonDocument sdk =
            repo.ReadJson(
                ".ai/generated/inventories/sdk-inventory.json");

        Assert.Equal(
            "net9.0",
            sdk.RootElement
                .GetProperty("expectedTargetFramework")
                .GetString());
    }

    [Fact]
    public void Run_CapturesSdkVersionAndList()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner =
            new()
            {
                VersionOutput =
                    " 10.0.111 \n",
                SdksOutput =
                    "10.0.111 [/sdk]\r\n" +
                    "9.0.200 [/sdk]\n"
            };

        RunSuccess(
            repo,
            runner);

        using JsonDocument sdk =
            repo.ReadJson(
                ".ai/generated/inventories/sdk-inventory.json");

        Assert.Equal(
            "10.0.111",
            sdk.RootElement
                .GetProperty("dotNetSdkVersion")
                .GetString());

        string[] sdks =
            sdk.RootElement
                .GetProperty("dotNetSdks")
                .EnumerateArray()
                .Select(item =>
                    item.GetString()!)
                .ToArray();

        Assert.Equal(
            new[]
            {
                "10.0.111 [/sdk]",
                "9.0.200 [/sdk]"
            },
            sdks);
    }

    [Fact]
    public void Run_DotNetFailuresProduceUnavailableAndStillSucceed()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner =
            new()
            {
                VersionExitCode = 7,
                SdksExitCode = 8
            };

        AiContextUpdateRunResult result =
            RunSuccess(
                repo,
                runner);

        Assert.True(result.IsSuccess);

        using JsonDocument sdk =
            repo.ReadJson(
                ".ai/generated/inventories/sdk-inventory.json");

        Assert.Equal(
            "Unavailable",
            sdk.RootElement
                .GetProperty("dotNetSdkVersion")
                .GetString());

        Assert.Equal(
            new[] { "Unavailable" },
            sdk.RootElement
                .GetProperty("dotNetSdks")
                .EnumerateArray()
                .Select(item =>
                    item.GetString()!)
                .ToArray());
    }

    [Fact]
    public void Run_UsesDeterministicTimeProvider()
    {
        using TempRepo repo = new();

        TimeZoneInfo zone =
            TimeZoneInfo.CreateCustomTimeZone(
                "TestPlusTwo",
                TimeSpan.FromHours(2),
                "TestPlusTwo",
                "TestPlusTwo");

        FixedTimeProvider timeProvider =
            new(
                new DateTimeOffset(
                    2026,
                    8,
                    19,
                    18,
                    10,
                    11,
                    TimeSpan.Zero),
                zone);

        RunSuccess(
            repo,
            timeProvider:
                timeProvider);

        using JsonDocument manifest =
            repo.ReadJson(
                ".ai/manifests/mcp-context-manifest.json");

        Assert.Equal(
            "2026-08-19 20:10:11 +02:00",
            manifest.RootElement
                .GetProperty("generatedAtLocal")
                .GetString());

        using JsonDocument projects =
            repo.ReadJson(
                ".ai/generated/inventories/project-inventory.json");

        Assert.Equal(
            "2026-08-19 20:10:11 +02:00",
            projects.RootElement
                .GetProperty("generatedAtLocal")
                .GetString());
    }

    [Fact]
    public void Run_WritesManifestPolicyAndBudgets()
    {
        using TempRepo repo = new();

        RunSuccess(repo);

        using JsonDocument manifest =
            repo.ReadJson(
                ".ai/manifests/mcp-context-manifest.json");

        JsonElement root =
            manifest.RootElement;

        Assert.Equal(
            Path.GetFileName(repo.Root),
            root
                .GetProperty("repoName")
                .GetString());

        string[] allowed =
            root
                .GetProperty("allowedContextFiles")
                .EnumerateArray()
                .Select(item =>
                    item.GetString()!)
                .ToArray();

        Assert.Contains(
            ".ai/generated/inventories/project-inventory.json",
            allowed);

        Assert.Contains(
            ".ai/generated/reports/sdk-alignment-report.json",
            allowed);

        string[] restricted =
            root
                .GetProperty("restrictedPaths")
                .EnumerateArray()
                .Select(item =>
                    item.GetString()!)
                .ToArray();

        Assert.Contains(
            ".build-*",
            restricted);

        Assert.Contains(
            "appsettings*.json",
            restricted);

        JsonElement budgets =
            root.GetProperty("budgets");

        Assert.Equal(
            8192,
            budgets
                .GetProperty("compactBytes")
                .GetInt32());

        Assert.Equal(
            49152,
            budgets
                .GetProperty("fullBytes")
                .GetInt32());

        Assert.Equal(
            65536,
            budgets
                .GetProperty("combinedBytes")
                .GetInt32());

        Assert.Equal(
            1048576,
            budgets
                .GetProperty("fileReadBytes")
                .GetInt32());

        Assert.Equal(
            25,
            budgets
                .GetProperty("searchHardLimit")
                .GetInt32());

        Assert.Equal(
            100,
            budgets
                .GetProperty("arrayHardLimit")
                .GetInt32());

        Assert.Equal(
            240,
            budgets
                .GetProperty("previewChars")
                .GetInt32());
    }

    [Fact]
    public void Run_WritesSummaryCounts()
    {
        using TempRepo repo = new();

        repo.WriteFile(
            "Main.sln",
            string.Empty);

        repo.WriteProject(
            "Lib.csproj",
            "<TargetFramework>net10.0</TargetFramework>");

        repo.WriteProject(
            "App.csproj",
            "<TargetFramework>net10.0</TargetFramework>",
            """
            <ProjectReference Include="Lib.csproj" />
            <PackageReference Include="Example" Version="1.0.0" />
            """);

        RunSuccess(repo);

        string markdown =
            File.ReadAllText(
                repo.PathOf(
                    ".ai/generated/summaries/generated-context-summary.md"));

        Assert.Contains(
            "- Main solution: Main.sln",
            markdown,
            StringComparison.Ordinal);

        Assert.Contains(
            "- Projects: 2",
            markdown,
            StringComparison.Ordinal);

        Assert.Contains(
            "- Packages: 1",
            markdown,
            StringComparison.Ordinal);

        Assert.Contains(
            "- Project references: 1",
            markdown,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Run_ProjectParseFailureDoesNotModifyGeneratedOutputs()
    {
        using TempRepo repo = new();

        string manifestPath =
            ".ai/manifests/mcp-context-manifest.json";

        string inventoryPath =
            ".ai/generated/inventories/project-inventory.json";

        repo.WriteFile(
            manifestPath,
            "existing-manifest");

        repo.WriteFile(
            inventoryPath,
            "existing-inventory");

        repo.WriteFile(
            "Broken.csproj",
            "<Project><Broken>");

        AiContextUpdateRunResult result =
            new AiContextUpdateService(
                new FakeProcessRunner())
            .Run(repo.Root);

        Assert.False(
            result.IsSuccess);

        Assert.Equal(
            "existing-manifest",
            File.ReadAllText(
                repo.PathOf(manifestPath)));

        Assert.Equal(
            "existing-inventory",
            File.ReadAllText(
                repo.PathOf(inventoryPath)));

        Assert.False(
            File.Exists(
                repo.PathOf(
                    ".ai/generated/inventories/package-inventory.json")));

        Assert.False(
            File.Exists(
                repo.PathOf(
                    ".ai/generated/inventories/sdk-inventory.json")));

        Assert.False(
            File.Exists(
                repo.PathOf(
                    ".ai/generated/summaries/generated-context-summary.md")));
    }

    [Fact]
    public void Run_UsesExpectedDotNetCommandsAndRepoWorkingDirectory()
    {
        using TempRepo repo = new();

        FakeProcessRunner runner =
            new();

        RunSuccess(
            repo,
            runner);

        Assert.Equal(
            2,
            runner.Calls.Count);

        Assert.Equal(
            "dotnet",
            runner.Calls[0].FileName);

        Assert.Equal(
            new[] { "--version" },
            runner.Calls[0].Arguments);

        Assert.Equal(
            repo.Root,
            runner.Calls[0].WorkingDirectory);

        Assert.Equal(
            "dotnet",
            runner.Calls[1].FileName);

        Assert.Equal(
            new[] { "--list-sdks" },
            runner.Calls[1].Arguments);

        Assert.Equal(
            repo.Root,
            runner.Calls[1].WorkingDirectory);
    }

    private static AiContextUpdateRunResult RunSuccess(
        TempRepo repo,
        FakeProcessRunner? runner = null,
        TimeProvider? timeProvider = null,
        AiContextUpdateOptions? options = null)
    {
        runner ??=
            new FakeProcessRunner();

        timeProvider ??=
            new FixedTimeProvider(
                new DateTimeOffset(
                    2026,
                    8,
                    19,
                    18,
                    0,
                    0,
                    TimeSpan.Zero),
                TimeZoneInfo.Utc);

        AiContextUpdateRunResult result =
            new AiContextUpdateService(
                runner,
                timeProvider)
            .Run(
                repo.Root,
                options);

        Assert.True(
            result.IsSuccess,
            result.ErrorMessage);

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

        public List<ProcessCall> Calls { get; } =
            [];

        public ProcessResult Run(
            string fileName,
            IEnumerable<string> arguments,
            string workingDirectory)
        {
            string[] args =
                arguments.ToArray();

            Calls.Add(
                new ProcessCall(
                    fileName,
                    args,
                    workingDirectory));

            if (args.SequenceEqual(
                    new[] { "--version" }))
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

            if (args.SequenceEqual(
                    new[] { "--list-sdks" }))
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
                string.Join(
                    " ",
                    args),
                workingDirectory,
                1,
                string.Empty,
                "unexpected command");
        }
    }

    private sealed record ProcessCall(
        string FileName,
        string[] Arguments,
        string WorkingDirectory);

    private sealed class FixedTimeProvider :
        TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProvider(
            DateTimeOffset utcNow,
            TimeZoneInfo localTimeZone)
        {
            _utcNow = utcNow;
            _localTimeZone = localTimeZone;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public override TimeZoneInfo LocalTimeZone =>
            _localTimeZone;
    }

    private sealed class TempRepo :
        IDisposable
    {
        public TempRepo()
        {
            Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "airepo_ai_context_" +
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string PathOf(
            string relativePath)
        {
            return Path.Combine(
                Root,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
        }

        public void WriteFile(
            string relativePath,
            string contents)
        {
            string path =
                PathOf(relativePath);

            string? directory =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                path,
                contents);
        }

        public void WriteProject(
            string relativePath,
            string properties,
            string items = "")
        {
            WriteFile(
                relativePath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    {properties}
                  </PropertyGroup>
                  <ItemGroup>
                    {items}
                  </ItemGroup>
                </Project>
                """);
        }

        public JsonDocument ReadJson(
            string relativePath)
        {
            return JsonDocument.Parse(
                File.ReadAllText(
                    PathOf(relativePath)));
        }

        public string[] ReadProjectPaths()
        {
            using JsonDocument document =
                ReadJson(
                    ".ai/generated/inventories/project-inventory.json");

            return document.RootElement
                .GetProperty("projects")
                .EnumerateArray()
                .Select(item =>
                    item.GetProperty("path").GetString()!)
                .ToArray();
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
