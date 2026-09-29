namespace AiRepoKit.WorkItems.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.WorkItems;
using Xunit;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "GitHubWorkItemProvider",
        "IWorkItemProvider",
        "JiraWorkItemProvider",
        "WorkItemProviderIds",
        "WorkItemReference",
        "WorkItemSnapshot",
        "WorkItemState"
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
                @"..\..\src\AiRepoKit.WorkItems\AiRepoKit.WorkItems.csproj"
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
    public void PublicSurface_ContainsExactlySevenFrozenTypes()
    {
        Assembly assembly =
            typeof(WorkItemReference).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            7,
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
    public void ProviderIds_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkItemProviderIds);

        Assert.True(
            type.IsAbstract);

        Assert.True(
            type.IsSealed);

        FieldInfo[] fields =
            type
                .GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .OrderBy(
                    field_ =>
                        field_.Name,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            2,
            fields.Length);

        Assert.All(
            fields,
            field_ =>
                Assert.True(
                    field_.IsLiteral));

        Assert.Equal(
            "github",
            WorkItemProviderIds.GitHub);

        Assert.Equal(
            "jira",
            WorkItemProviderIds.Jira);
    }

    [Fact]
    public void WorkItemState_PublicSurfaceIsFrozen()
    {
        Assert.Equal(
            [
                WorkItemState.Unknown,
                WorkItemState.Open,
                WorkItemState.Closed
            ],
            Enum.GetValues<WorkItemState>());

        Assert.Equal(
            0,
            (int) WorkItemState.Unknown);

        Assert.Equal(
            1,
            (int) WorkItemState.Open);

        Assert.Equal(
            2,
            (int) WorkItemState.Closed);
    }

    [Fact]
    public void WorkItemReference_PublicSurfaceIsFrozen()
    {
        ConstructorInfo constructor =
            Assert.Single(
                typeof(WorkItemReference)
                    .GetConstructors());

        Assert.Equal(
            [
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
                "Id",
                "Namespace",
                "ProviderId"
            ],
            DeclaredPublicProperties(
                typeof(WorkItemReference)));
    }

    [Fact]
    public void WorkItemSnapshot_PublicSurfaceIsFrozen()
    {
        ConstructorInfo constructor =
            Assert.Single(
                typeof(WorkItemSnapshot)
                    .GetConstructors());

        Assert.Equal(
            [
                typeof(WorkItemReference),
                typeof(string),
                typeof(WorkItemState),
                typeof(string),
                typeof(Uri)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.Equal(
            [
                "NativeState",
                "ProviderUri",
                "Reference",
                "State",
                "Title"
            ],
            DeclaredPublicProperties(
                typeof(WorkItemSnapshot)));
    }

    [Fact]
    public void ProviderInterface_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(IWorkItemProvider);

        Assert.True(
            type.IsInterface);

        Assert.Equal(
            [
                "ProviderId"
            ],
            DeclaredPublicProperties(
                type));

        MethodInfo method =
            Assert.Single(
                type
                    .GetMethods(
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Where(
                        method_ =>
                            !method_.IsSpecialName));

        Assert.Equal(
            "GetAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<WorkItemSnapshot>),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            [
                typeof(WorkItemReference),
                typeof(CancellationToken)
            ],
            parameters
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            parameters[1].HasDefaultValue);
    }

    [Fact]
    public void GitHubProvider_PublicSurfaceIsFrozen()
    {
        AssertProviderSurface(
            typeof(GitHubWorkItemProvider));
    }

    [Fact]
    public void JiraProvider_PublicSurfaceIsFrozen()
    {
        AssertProviderSurface(
            typeof(JiraWorkItemProvider));
    }

    [Fact]
    public void Solution_ContainsExactlyTheNewWorkItemsProjectsByPath()
    {
        string solution =
            File.ReadAllText(
                GetSolutionPath());

        Assert.Contains(
            @"src\AiRepoKit.WorkItems\AiRepoKit.WorkItems.csproj",
            solution,
            StringComparison.Ordinal);

        Assert.Contains(
            @"tests\AiRepoKit.WorkItems.Tests\AiRepoKit.WorkItems.Tests.csproj",
            solution,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSource_ContainsOnlyReadOnlyDeterministicHttpBoundary()
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

        string[] forbidden =
        [
            "HttpMethod.Post",
            "HttpMethod.Put",
            "HttpMethod.Patch",
            "HttpMethod.Delete",
            ".PostAsync(",
            ".PutAsync(",
            ".PatchAsync(",
            ".DeleteAsync(",
            "Environment.",
            "File.",
            "Directory.",
            "DateTime.",
            "DateTimeOffset.",
            "Guid.",
            "Random.",
            "Microsoft.Agents",
            "Microsoft.Extensions.AI",
            "SVALA"
        ];

        foreach (string value in forbidden)
        {
            Assert.DoesNotContain(
                value,
                source,
                StringComparison.Ordinal);
        }

        Assert.Equal(
            2,
            source.Split(
                    "HttpMethod.Get",
                    StringSplitOptions.None)
                .Length -
            1);
    }

    private static void AssertProviderSurface(
        Type type_)
    {
        Assert.True(
            type_.IsClass);

        Assert.True(
            type_.IsSealed);

        ConstructorInfo constructor =
            Assert.Single(
                type_.GetConstructors());

        Assert.Equal(
            [
                typeof(HttpClient)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.Equal(
            [
                "ProviderId"
            ],
            DeclaredPublicProperties(
                type_));

        MethodInfo method =
            Assert.Single(
                type_
                    .GetMethods(
                        BindingFlags.Public |
                        BindingFlags.Instance |
                        BindingFlags.DeclaredOnly)
                    .Where(
                        method_ =>
                            !method_.IsSpecialName));

        Assert.Equal(
            "GetAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<WorkItemSnapshot>),
            method.ReturnType);

        Assert.Equal(
            [
                typeof(WorkItemReference),
                typeof(CancellationToken)
            ],
            method
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());
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
            "AiRepoKit.WorkItems");
    }

    private static string GetProductionProjectPath()
    {
        return Path.Combine(
            GetProductionDirectory(),
            "AiRepoKit.WorkItems.csproj");
    }

    private static string GetTestProjectPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "AiRepoKit.WorkItems.Tests",
            "AiRepoKit.WorkItems.Tests.csproj");
    }

    private static string GetSolutionPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "AI.RepoKit.MCP.sln");
    }
}
