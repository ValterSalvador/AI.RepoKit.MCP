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
        "ExecutableWork"
    ];

    [Fact]
    public void PublicSurface_ContainsExactlyTwoFrozenTypes()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            2,
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
    public void ProductionProject_HasZeroPackageReferences()
    {
        string projectPath =
            GetProductionProjectPath();

        Assert.True(
            File.Exists(
                projectPath),
            $"Project file does not exist at: {projectPath}");

        XDocument document =
            XDocument.Load(
                projectPath);

        IEnumerable<XElement> packageReferences =
            document.Descendants(
                "PackageReference");

        Assert.Empty(
            packageReferences);
    }

    [Fact]
    public void ProductionProject_HasZeroProjectReferences()
    {
        string projectPath =
            GetProductionProjectPath();

        Assert.True(
            File.Exists(
                projectPath),
            $"Project file does not exist at: {projectPath}");

        XDocument document =
            XDocument.Load(
                projectPath);

        IEnumerable<XElement> projectReferences =
            document.Descendants(
                "ProjectReference");

        Assert.Empty(
            projectReferences);
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
                        StringComparison.OrdinalIgnoreCase),
                    $"Assembly referenced forbidden assembly: {name}");
            }

            Assert.True(
                name == "System.Runtime" ||
                name.StartsWith(
                    "System.",
                    StringComparison.Ordinal) ||
                name == "mscorlib" ||
                name == "netstandard",
                $"Unexpected referenced assembly: {name}");
        }
    }

    [Fact]
    public void PublicApi_ContainsNoForbiddenNamespaces()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        string[] forbiddenNamespaces =
        [
            "AiRepoKit.Spec",
            "AiRepoKit.Agents",
            "AiRepoKit.Cli",
            "Microsoft.Extensions.AI"
        ];

        foreach (Type type in exportedTypes)
        {
            foreach (string forbidden in forbiddenNamespaces)
            {
                Assert.DoesNotContain(
                    forbidden,
                    type.Namespace ??
                    string.Empty);
            }

            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                AssertForbiddenType(
                    method.ReturnType,
                    forbiddenNamespaces);

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertForbiddenType(
                        parameter.ParameterType,
                        forbiddenNamespaces);
                }
            }

            foreach (PropertyInfo property in type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                AssertForbiddenType(
                    property.PropertyType,
                    forbiddenNamespaces);
            }
        }
    }

    [Fact]
    public void PublicApi_ContainsNoDagOrDependencyContract()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        string[] dagTerms =
        [
            "DependsOn",
            "Dependency",
            "Dependencies",
            "Prerequisite",
            "Predecessor",
            "Successor",
            "Topological",
            "TaskDag",
            "Dag"
        ];

        foreach (Type type in exportedTypes)
        {
            foreach (string term in dagTerms)
            {
                Assert.DoesNotContain(
                    term,
                    type.Name,
                    StringComparison.OrdinalIgnoreCase);
            }

            foreach (PropertyInfo property in type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static))
            {
                foreach (string term in dagTerms)
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
                foreach (string term in dagTerms)
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
    public void PublicApi_ContainsNoFuturePhaseConcepts()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        string[] futurePhaseTerms =
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
            "Orchestrat"
        ];

        foreach (Type type in exportedTypes)
        {
            foreach (string term in futurePhaseTerms)
            {
                Assert.DoesNotContain(
                    term,
                    type.Name,
                    StringComparison.OrdinalIgnoreCase);
            }

            foreach (PropertyInfo property in type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static))
            {
                foreach (string term in futurePhaseTerms)
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
                foreach (string term in futurePhaseTerms)
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

        Assert.True(
            File.Exists(
                solutionPath),
            $"Solution file does not exist at: {solutionPath}");

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
            if (File.Exists(Path.Combine(current.FullName, "AI.RepoKit.MCP.sln")))
            {
                return current.FullName;
            }

            current =
                current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root containing AI.RepoKit.MCP.sln.");
    }

    private static string GetProductionProjectPath()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "src",
            "AiRepoKit.Execution",
            "AiRepoKit.Execution.csproj");
    }

    private static void AssertForbiddenType(
        Type type_,
        string[] forbiddenNamespaces_)
    {
        string? ns =
            type_.Namespace;

        if (ns is null)
        {
            return;
        }

        foreach (string forbidden in forbiddenNamespaces_)
        {
            Assert.False(
                ns.StartsWith(
                    forbidden,
                    StringComparison.Ordinal),
                $"Public API exposed forbidden type '{type_.FullName}' from namespace '{ns}'.");
        }
    }
}
