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
        "ExecutableWork",
        "ValidationRequirement",
        "ValidationStrategy"
    ];

    [Fact]
    public void PublicSurface_ContainsExactlyFiveFrozenTypes()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            5,
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
    public void ExecutableTaskDependency_PublicPropertiesRemainFrozen()
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
    public void ValidationRequirement_PublicPropertiesAreFrozen()
    {
        string[] propertyNames =
            typeof(ValidationRequirement)
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
                "SourceAcceptanceCriterionId",
                "Statement",
                "Strategy",
                "TaskId"
            ],
            propertyNames);
    }

    [Fact]
    public void ValidationRequirement_HasExactlyOneFrozenConstructor()
    {
        ConstructorInfo[] constructors =
            typeof(ValidationRequirement)
                .GetConstructors();

        Assert.Single(
            constructors);

        ParameterInfo[] parameters =
            constructors[0].GetParameters();

        Assert.Equal(
            5,
            parameters.Length);

        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);

        Assert.Equal(
            typeof(string),
            parameters[1].ParameterType);

        Assert.Equal(
            typeof(string),
            parameters[2].ParameterType);

        Assert.Equal(
            typeof(ValidationStrategy),
            parameters[3].ParameterType);

        Assert.Equal(
            typeof(string),
            parameters[4].ParameterType);
    }

    [Fact]
    public void ValidationStrategy_PublicValuesAreFrozen()
    {
        ValidationStrategy[] values =
            Enum.GetValues<ValidationStrategy>();

        Assert.Equal(
            [
                ValidationStrategy.Build,
                ValidationStrategy.Test,
                ValidationStrategy.Policy
            ],
            values);

        Assert.Equal(
            1,
            (int) ValidationStrategy.Build);

        Assert.Equal(
            2,
            (int) ValidationStrategy.Test);

        Assert.Equal(
            3,
            (int) ValidationStrategy.Policy);

        Assert.False(
            Enum.IsDefined(
                typeof(ValidationStrategy),
                0));
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
    public void ExecutableWork_ExposesFrozenValidationRequirementsProperty()
    {
        PropertyInfo? property =
            typeof(ExecutableWork).GetProperty(
                nameof(ExecutableWork.ValidationRequirements));

        Assert.NotNull(
            property);

        Assert.Equal(
            typeof(IReadOnlyList<ValidationRequirement>),
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

        string[] forbiddenAssemblies =
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

            foreach (string forbidden in forbiddenAssemblies)
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
    public void PublicApi_ContainsNoDeferredValidationOrFuturePhaseConcepts()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        string[] forbiddenTerms =
        [
            "ValidationEngine",
            "ValidationResult",
            "ValidationEvidence",
            "Evaluator",
            "Runner",
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
    public void PublicApi_ContainsNoSpecTypes()
    {
        Assembly executionAssembly =
            typeof(ExecutableWork).Assembly;

        foreach (Type type in executionAssembly.GetExportedTypes())
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                Assert.DoesNotContain(
                    "AiRepoKit.Spec",
                    property.PropertyType.FullName ??
                        string.Empty,
                    StringComparison.Ordinal);
            }

            foreach (ConstructorInfo constructor in type.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    Assert.DoesNotContain(
                        "AiRepoKit.Spec",
                        parameter.ParameterType.FullName ??
                            string.Empty,
                        StringComparison.Ordinal);
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