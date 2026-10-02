namespace AiRepoKit.Git.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Git;
using Xunit;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "GitWorktreeIsolation",
        "GitWorktreeRequest",
        "GitWorktreeSnapshot",
        "GitWorktreeState"
    ];

    [Fact]
    public void ProductionProject_HasNoPackageOrProjectReferences()
    {
        XDocument document =
            XDocument.Load(
                GetProductionProjectPath());

        Assert.Empty(
            document.Descendants(
                "PackageReference"));

        Assert.Empty(
            document.Descendants(
                "ProjectReference"));
    }

    [Fact]
    public void TestProject_HasFrozenDependencies()
    {
        XDocument document =
            XDocument.Load(
                GetTestProjectPath());

        Assert.Equal(
            [
                "Microsoft.NET.Test.Sdk|17.12.0",
                "xunit|2.9.2",
                "xunit.runner.visualstudio|3.0.0"
            ],
            document
                .Descendants(
                    "PackageReference")
                .Select(
                    element_ =>
                        $"{element_.Attribute("Include")?.Value}|{element_.Attribute("Version")?.Value}")
                .ToArray());

        Assert.Equal(
            [
                @"..\..\src\AiRepoKit.Git\AiRepoKit.Git.csproj"
            ],
            document
                .Descendants(
                    "ProjectReference")
                .Select(
                    element_ =>
                        element_.Attribute("Include")?.Value ??
                        string.Empty)
                .ToArray());
    }

    [Fact]
    public void PublicSurface_ContainsExactlyFourFrozenTypes()
    {
        Assembly assembly =
            typeof(GitWorktreeIsolation).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            4,
            exportedTypes.Length);

        Assert.Equal(
            _expectedPublicTypeNames,
            exportedTypes
                .Select(
                    type_ =>
                        type_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void GitWorktreeState_PublicSurfaceIsFrozen()
    {
        Assert.Equal(
            [
                GitWorktreeState.Missing,
                GitWorktreeState.Ready,
                GitWorktreeState.Dirty,
                GitWorktreeState.Conflict
            ],
            Enum.GetValues<GitWorktreeState>());

        Assert.Equal(0, (int) GitWorktreeState.Missing);
        Assert.Equal(1, (int) GitWorktreeState.Ready);
        Assert.Equal(2, (int) GitWorktreeState.Dirty);
        Assert.Equal(3, (int) GitWorktreeState.Conflict);
    }

    [Fact]
    public void GitWorktreeRequest_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(GitWorktreeRequest);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.Equal(
            [
                "BaseCommitSha",
                "IsolationId",
                "IsolationRoot",
                "RepositoryRoot"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Empty(
            DeclaredPublicMethods(
                type));
    }

    [Fact]
    public void GitWorktreeSnapshot_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(GitWorktreeSnapshot);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "IsClean",
                "IsDetached",
                "IsLocked",
                "IsPathPresent",
                "IsPrunable",
                "IsRegistered",
                "ObservedCommitSha",
                "Request",
                "State",
                "WorktreePath"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Empty(
            DeclaredPublicMethods(
                type));
    }

    [Fact]
    public void GitWorktreeIsolation_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(GitWorktreeIsolation);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(TimeSpan)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        MethodInfo[] methods =
            DeclaredPublicMethods(
                type);

        Assert.Equal(
            [
                "EnsureAsync",
                "InspectAsync",
                "ReleaseAsync"
            ],
            methods
                .Select(
                    method_ =>
                        method_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray());

        Assert.All(
            methods,
            method_ =>
            {
                Assert.Equal(
                    typeof(Task<GitWorktreeSnapshot>),
                    method_.ReturnType);

                ParameterInfo[] parameters =
                    method_.GetParameters();

                Assert.Equal(
                    [
                        typeof(GitWorktreeRequest),
                        typeof(CancellationToken)
                    ],
                    parameters
                        .Select(
                            parameter_ =>
                                parameter_.ParameterType)
                        .ToArray());

                Assert.True(
                    parameters[1].HasDefaultValue);
            });

        Assert.Empty(
            DeclaredPublicProperties(
                type));
    }

    [Fact]
    public void InternalProcessAndParserTypes_AreNotPublic()
    {
        Assembly assembly =
            typeof(GitWorktreeIsolation).Assembly;

        foreach (string typeName in new[]
        {
            "AiRepoKit.Git.IGitProcessRunner",
            "AiRepoKit.Git.GitProcessResult",
            "AiRepoKit.Git.GitProcessRunner",
            "AiRepoKit.Git.GitWorktreePorcelainEntry",
            "AiRepoKit.Git.GitWorktreePorcelainParser"
        })
        {
            Type type =
                assembly.GetType(
                    typeName,
                    throwOnError: true)!;

            Assert.False(
                type.IsPublic);
        }
    }

    [Fact]
    public void Solution_ContainsExactlyTheNewGitProjectsByPath()
    {
        string solution =
            File.ReadAllText(
                GetSolutionPath());

        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                @"src\AiRepoKit.Git\AiRepoKit.Git.csproj"));

        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                @"tests\AiRepoKit.Git.Tests\AiRepoKit.Git.Tests.csproj"));
    }

    [Fact]
    public void ProductionSource_RespectsFrozenGitBoundary()
    {
        string source =
            string.Join(
                "\n",
                Directory
                    .GetFiles(
                        GetProductionDirectory(),
                        "*.cs",
                        SearchOption.TopDirectoryOnly)
                    .OrderBy(
                        path_ =>
                            path_,
                        StringComparer.Ordinal)
                    .Select(
                        File.ReadAllText));

        foreach (string forbidden in new[]
        {
            "\"clone\"",
            "\"fetch\"",
            "\"pull\"",
            "\"push\"",
            "\"reset\"",
            "\"checkout\"",
            "\"switch\"",
            "\"prune\"",
            "\"--force\"",
            "Directory.Delete(",
            "UseShellExecute = true",
            "Environment.GetEnvironmentVariable",
            "DateTime.",
            "DateTimeOffset.",
            "Guid.",
            "Random.",
            "Microsoft.Agents",
            "Microsoft.Extensions.AI",
            "SVALA"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                source,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "FileName = \"git\"",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "UseShellExecute = false",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "startInfo.ArgumentList.Add",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "process_.Kill(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "entireProcessTree: true",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WaitForExitAsync",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "new TimeoutException",
            source,
            StringComparison.Ordinal);
    }

    private static MethodInfo[] DeclaredPublicMethods(
        Type type_)
    {
        return type_
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(
                method_ =>
                    !method_.IsSpecialName)
            .ToArray();
    }

    private static string[] DeclaredPublicProperties(
        Type type_)
    {
        return type_
            .GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(
                property_ =>
                    property_.Name)
            .OrderBy(
                name_ =>
                    name_,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static int CountOccurrences(
        string value_,
        string needle_)
    {
        return value_.Split(
                needle_,
                StringSplitOptions.None)
            .Length -
            1;
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                ".."));
    }

    private static string GetProductionDirectory()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "src",
            "AiRepoKit.Git");
    }

    private static string GetProductionProjectPath()
    {
        return Path.Combine(
            GetProductionDirectory(),
            "AiRepoKit.Git.csproj");
    }

    private static string GetTestProjectPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "AiRepoKit.Git.Tests",
            "AiRepoKit.Git.Tests.csproj");
    }

    private static string GetSolutionPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "AI.RepoKit.MCP.sln");
    }
}
