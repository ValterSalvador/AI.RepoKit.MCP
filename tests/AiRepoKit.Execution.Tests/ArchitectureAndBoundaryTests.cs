namespace AiRepoKit.Execution.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Execution;
using Xunit;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "ExecutableTask",
        "ExecutableTaskDependency",
        "ExecutableWork"
    ];

    [Fact]
    public void PublicSurface_ContainsExactlyThreeFrozenTypes()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            3,
            exportedTypes.Length);

        string[] exportedNames =
            exportedTypes
                .Select(
                    type_ =>
                        type_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            _expectedPublicTypeNames,
            exportedNames);
    }

    [Fact]
    public void ExecutableTask_PublicPropertiesRemainFrozen()
    {
        string[] propertyNames =
            typeof(ExecutableTask)
                .GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly)
                .Select(
                    property_ =>
                        property_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            [
                "Id",
                "Instruction",
                "SourcePlanStepId"
            ],
            propertyNames);
    }

    [Fact]
    public void ExecutableTaskDependency_PublicPropertiesAreFrozen()
    {
        string[] propertyNames =
            typeof(ExecutableTaskDependency)
                .GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly)
                .Select(
                    property_ =>
                        property_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            [
                "DependsOnTaskId",
                "TaskId"
            ],
            propertyNames);
    }

    [Fact]
    public void ExecutableWork_ExposesFrozenDependenciesProperty()
    {
        PropertyInfo? property =
            typeof(ExecutableWork).GetProperty(
                nameof(ExecutableWork.Dependencies));

        Assert.NotNull(
            property);

        Assert.Equal(
            typeof(IReadOnlyList<ExecutableTaskDependency>),
            property.PropertyType);
    }

    [Fact]
    public void ProductionProject_HasZeroPackageReferences()
    {
        string projectPath =
            GetProductionProjectPath();

        XDocument document =
            XDocument.Load(
                projectPath);

        Assert.Empty(
            document.Descendants("PackageReference"));
    }

    [Fact]
    public void ProductionProject_HasZeroProjectReferences()
    {
        string projectPath =
            GetProductionProjectPath();

        XDocument document =
            XDocument.Load(
                projectPath);

        Assert.Empty(
            document.Descendants("ProjectReference"));
    }

    [Fact]
    public void Assembly_ReferencesOnlyBclAssemblies()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        AssemblyName[] referencedAssemblies =
            assembly.GetReferencedAssemblies();

        string[] forbiddenNamespaces =
        [
            "AiRepoKit.Spec",
            "AiRepoKit.Agents.Abstractions",
            "AiRepoKit.Agents.Antigravity",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Cli",
            "Microsoft.Extensions.AI"
        ];

        foreach (AssemblyName reference in referencedAssemblies)
        {
            string name =
                reference.Name ??
                string.Empty;

            foreach (string forbidden in forbiddenNamespaces)
            {
                Assert.False(
                    string.Equals(
                        name,
                        forbidden,
                        StringComparison.OrdinalIgnoreCase));
            }

            Assert.True(
                name == "System.Runtime" ||
                name.StartsWith(
                    "System.",
                    StringComparison.Ordinal) ||
                name == "mscorlib" ||
                name == "netstandard");
        }
    }

    [Fact]
    public void PublicApi_ContainsNoSchedulingOrFuturePhaseConcepts()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        string[] forbiddenTerms =
        [
            "ValidationStrategy",
            "ValidationRequirement",
            "ModelRequirement",
            "AgentRequirement",
            "Complexity",
            "TokenBudget",
            "EstimatedTokens",
            "ContextCompiler",
            "PromptCompiler",
            "CompiledPrompt",
            "ExecutionEnvelope",
            "Scheduler",
            "Queue",
            "Worker",
            "Concurrency",
            "Priority",
            "Dispatch",
            "Orchestrat",
            "Readiness",
            "Runnable",
            "ExecutionState",
            "Topological"
        ];

        foreach (Type type in assembly.GetExportedTypes())
        {
            foreach (string term in forbiddenTerms)
            {
                Assert.DoesNotContain(
                    term,
                    type.Name,
                    StringComparison.OrdinalIgnoreCase);
            }

            foreach (PropertyInfo property in type.GetProperties())
            {
                foreach (string term in forbiddenTerms)
                {
                    Assert.DoesNotContain(
                        term,
                        property.Name,
                        StringComparison.OrdinalIgnoreCase);
                }
            }

            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                foreach (string term in forbiddenTerms)
                {
                    Assert.DoesNotContain(
                        term,
                        method.Name,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void SolutionFile_ContainsBothExecutionProjects()
    {
        string solutionPath =
            Path.Combine(
                FindRepositoryRoot(),
                "AI.RepoKit.MCP.sln");

        string solutionContent =
            File.ReadAllText(
                solutionPath);

        Assert.Contains(
            @"src\AiRepoKit.Execution\AiRepoKit.Execution.csproj",
            solutionContent,
            StringComparison.Ordinal);

        Assert.Contains(
            @"tests\AiRepoKit.Execution.Tests\AiRepoKit.Execution.Tests.csproj",
            solutionContent,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current =
            new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "AI.RepoKit.MCP.sln")))
            {
                return current.FullName;
            }

            current =
                current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root.");
    }

    private static string GetProductionProjectPath()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AiRepoKit.Execution",
            "AiRepoKit.Execution.csproj");
    }
}