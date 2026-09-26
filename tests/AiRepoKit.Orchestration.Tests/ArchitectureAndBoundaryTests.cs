namespace AiRepoKit.Orchestration.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using Xunit;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "WorkflowState",
        "WorkflowStateMachine",
        "WorkflowStatus",
        "WorkflowStepState",
        "WorkflowStepStatus"
    ];

    [Fact]
    public void ProductionProject_HasZeroPackageReferences()
    {
        XDocument document =
            XDocument.Load(
                GetProductionProjectPath());

        Assert.Empty(
            document.Descendants(
                "PackageReference"));
    }

    [Fact]
    public void ProductionProject_ReferencesOnlyExecution()
    {
        XDocument document =
            XDocument.Load(
                GetProductionProjectPath());

        XElement[] references =
            document
                .Descendants(
                    "ProjectReference")
                .ToArray();

        Assert.Single(
            references);

        Assert.Equal(
            @"..\AiRepoKit.Execution\AiRepoKit.Execution.csproj",
            references[0].Attribute("Include")?.Value);
    }

    [Fact]
    public void TestProject_HasFrozenDependencies()
    {
        XDocument document =
            XDocument.Load(
                GetTestProjectPath());

        XElement[] packages =
            document
                .Descendants(
                    "PackageReference")
                .ToArray();

        Assert.Equal(
            3,
            packages.Length);

        Assert.Equal(
            [
                "Microsoft.NET.Test.Sdk|17.12.0",
                "xunit|2.9.2",
                "xunit.runner.visualstudio|3.0.0"
            ],
            packages
                .Select(
                    element_ =>
                        $"{element_.Attribute("Include")?.Value}|{element_.Attribute("Version")?.Value}")
                .ToArray());

        string[] projectReferences =
            document
                .Descendants(
                    "ProjectReference")
                .Select(
                    element_ =>
                        element_.Attribute("Include")?.Value ??
                        string.Empty)
                .ToArray();

        Assert.Equal(
            [
                @"..\..\src\AiRepoKit.Orchestration\AiRepoKit.Orchestration.csproj",
                @"..\..\src\AiRepoKit.Execution\AiRepoKit.Execution.csproj"
            ],
            projectReferences);
    }

    [Fact]
    public void PublicSurface_ContainsExactlyFiveFrozenTypes()
    {
        Assembly assembly =
            typeof(WorkflowState).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            5,
            exportedTypes.Length);

        string[] names =
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
            names);
    }

    [Fact]
    public void ExistingExecutionAndAgentSurfacesRemainFrozen()
    {
        Assembly executionAssembly =
            typeof(ExecutableWork).Assembly;

        Assert.Equal(
            16,
            executionAssembly
                .GetExportedTypes()
                .Length);

        AssemblyName agentAssemblyName =
            executionAssembly
                .GetReferencedAssemblies()
                .Single(
                    reference_ =>
                        string.Equals(
                            reference_.Name,
                            "AiRepoKit.Agents.Abstractions",
                            StringComparison.Ordinal));

        Assembly agentAssembly =
            Assembly.Load(
                agentAssemblyName);

        Assert.Equal(
            11,
            agentAssembly
                .GetExportedTypes()
                .Length);
    }

    [Fact]
    public void StateRecords_HaveFrozenPropertiesAndNoPublicConstructors()
    {
        Assert.Equal(
            [
                "Status",
                "TaskId"
            ],
            typeof(WorkflowStepState)
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
                .ToArray());

        Assert.Equal(
            [
                "SourceImplementationPlanRevision",
                "Status",
                "Steps"
            ],
            typeof(WorkflowState)
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
                .ToArray());

        Assert.Empty(
            typeof(WorkflowStepState)
                .GetConstructors());

        Assert.Empty(
            typeof(WorkflowState)
                .GetConstructors());
    }

    [Fact]
    public void StateProperties_AreReadOnly()
    {
        foreach (PropertyInfo property in
                 typeof(WorkflowStepState).GetProperties())
        {
            Assert.False(
                property.CanWrite);
        }

        foreach (PropertyInfo property in
                 typeof(WorkflowState).GetProperties())
        {
            Assert.False(
                property.CanWrite);
        }
    }

    [Fact]
    public void WorkflowStatus_ValuesAreFrozen()
    {
        Assert.Equal(
            [
                WorkflowStatus.Created,
                WorkflowStatus.Running,
                WorkflowStatus.Completed,
                WorkflowStatus.Failed,
                WorkflowStatus.Cancelled
            ],
            Enum.GetValues<WorkflowStatus>());

        Assert.Equal(1, (int) WorkflowStatus.Created);
        Assert.Equal(2, (int) WorkflowStatus.Running);
        Assert.Equal(3, (int) WorkflowStatus.Completed);
        Assert.Equal(4, (int) WorkflowStatus.Failed);
        Assert.Equal(5, (int) WorkflowStatus.Cancelled);

        Assert.False(
            Enum.IsDefined(
                typeof(WorkflowStatus),
                0));
    }

    [Fact]
    public void WorkflowStepStatus_ValuesAreFrozen()
    {
        Assert.Equal(
            [
                WorkflowStepStatus.Pending,
                WorkflowStepStatus.Running,
                WorkflowStepStatus.AwaitingValidation,
                WorkflowStepStatus.Blocked,
                WorkflowStepStatus.Failed,
                WorkflowStepStatus.Completed,
                WorkflowStepStatus.Cancelled
            ],
            Enum.GetValues<WorkflowStepStatus>());

        Assert.Equal(1, (int) WorkflowStepStatus.Pending);
        Assert.Equal(2, (int) WorkflowStepStatus.Running);
        Assert.Equal(3, (int) WorkflowStepStatus.AwaitingValidation);
        Assert.Equal(4, (int) WorkflowStepStatus.Blocked);
        Assert.Equal(5, (int) WorkflowStepStatus.Failed);
        Assert.Equal(6, (int) WorkflowStepStatus.Completed);
        Assert.Equal(7, (int) WorkflowStepStatus.Cancelled);

        Assert.False(
            Enum.IsDefined(
                typeof(WorkflowStepStatus),
                0));
    }

    [Fact]
    public void WorkflowStateMachine_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowStateMachine);

        Assert.True(
            type.IsAbstract);

        Assert.True(
            type.IsSealed);

        Assert.Empty(
            type.GetConstructors());

        FieldInfo[] fields =
            type.GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

        Assert.Single(
            fields);

        Assert.Equal(
            "AlgorithmId",
            fields[0].Name);

        Assert.True(
            fields[0].IsLiteral);

        Assert.Equal(
            "ai.repokit.workflow-state-machine/v1",
            fields[0].GetRawConstantValue());

        string[] methods =
            type
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(
                    method_ =>
                        method_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            [
                "Create",
                "TransitionStep",
                "TransitionWorkflow"
            ],
            methods);
    }

    [Fact]
    public void PublicApi_ContainsNoFuturePhaseConcepts()
    {
        string[] forbiddenTerms =
        [
            "Persistence",
            "Event",
            "Checkpoint",
            "Resume",
            "Scheduler",
            "Queue",
            "Priority",
            "Dispatch",
            "Retry",
            "Attempt",
            "Backoff",
            "Fallback",
            "Repair",
            "SideEffect",
            "Idempot",
            "Reconcil",
            "AgentExecution"
        ];

        Assembly assembly =
            typeof(WorkflowState).Assembly;

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
                         BindingFlags.Static |
                         BindingFlags.Instance |
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
    public void Assembly_ReferencesExecutionButNoRuntimeSpecCliOrVendorFramework()
    {
        Assembly assembly =
            typeof(WorkflowState).Assembly;

        AssemblyName[] references =
            assembly.GetReferencedAssemblies();

        Assert.Single(
            references.Where(
                reference_ =>
                    string.Equals(
                        reference_.Name,
                        "AiRepoKit.Execution",
                        StringComparison.Ordinal)));

        string[] forbiddenAssemblies =
        [
            "AiRepoKit.Agents.Abstractions",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Antigravity",
            "AiRepoKit.Spec",
            "AiRepoKit.Cli",
            "Microsoft.Extensions.AI",
            "Microsoft.Agents"
        ];

        foreach (AssemblyName reference in references)
        {
            foreach (string forbidden in forbiddenAssemblies)
            {
                Assert.False(
                    string.Equals(
                        reference.Name,
                        forbidden,
                        StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Solution_ContainsOrchestrationProjects()
    {
        string solution =
            File.ReadAllText(
                GetSolutionPath());

        Assert.Contains(
            @"src\AiRepoKit.Orchestration\AiRepoKit.Orchestration.csproj",
            solution,
            StringComparison.Ordinal);

        Assert.Contains(
            @"tests\AiRepoKit.Orchestration.Tests\AiRepoKit.Orchestration.Tests.csproj",
            solution,
            StringComparison.Ordinal);
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

    private static string GetProductionProjectPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "src",
            "AiRepoKit.Orchestration",
            "AiRepoKit.Orchestration.csproj");
    }

    private static string GetTestProjectPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "AiRepoKit.Orchestration.Tests",
            "AiRepoKit.Orchestration.Tests.csproj");
    }

    private static string GetSolutionPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "AI.RepoKit.MCP.sln");
    }
}