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
        "WorkflowExecutionEvent",
        "WorkflowExecutionEventKind",
        "WorkflowId",
        "WorkflowPersistenceSnapshot",
        "WorkflowPersistenceStore",
        "WorkflowState",
        "WorkflowStateMachine",
        "WorkflowStatus",
        "WorkflowStepState",
        "WorkflowStepStatus"
    ];

    [Fact]
    public void ProductionProject_HasFrozenReferences()
    {
        XDocument document =
            XDocument.Load(
                GetProductionProjectPath());

        Assert.Empty(
            document.Descendants(
                "PackageReference"));

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
                @"..\..\src\AiRepoKit.Orchestration\AiRepoKit.Orchestration.csproj",
                @"..\..\src\AiRepoKit.Execution\AiRepoKit.Execution.csproj"
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
    public void PublicSurface_ContainsExactlyTenFrozenTypes()
    {
        Assembly assembly =
            typeof(WorkflowState).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            10,
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
    public void ExistingP01StateSurface_RemainsFrozen()
    {
        Assert.Equal(
            [
                "Status",
                "TaskId"
            ],
            DeclaredPublicProperties(
                typeof(WorkflowStepState)));

        Assert.Equal(
            [
                "SourceImplementationPlanRevision",
                "Status",
                "Steps"
            ],
            DeclaredPublicProperties(
                typeof(WorkflowState)));

        Assert.Empty(
            typeof(WorkflowStepState)
                .GetConstructors());

        Assert.Empty(
            typeof(WorkflowState)
                .GetConstructors());

        Assert.Equal(
            [
                WorkflowStatus.Created,
                WorkflowStatus.Running,
                WorkflowStatus.Completed,
                WorkflowStatus.Failed,
                WorkflowStatus.Cancelled
            ],
            Enum.GetValues<WorkflowStatus>());

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
    }

    [Fact]
    public void ExistingP01StateMachineSurface_RemainsFrozen()
    {
        Type type =
            typeof(WorkflowStateMachine);

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

        Assert.Equal(
            "ai.repokit.workflow-state-machine/v1",
            fields[0].GetRawConstantValue());

        Assert.Equal(
            [
                "Create",
                "TransitionStep",
                "TransitionWorkflow"
            ],
            type
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Where(
                    method_ =>
                        !method_.IsSpecialName)
                .Select(
                    method_ =>
                        method_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray());
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
    public void WorkflowId_PublicSurfaceIsFrozen()
    {
        Assert.Equal(
            [
                "Value"
            ],
            DeclaredPublicProperties(
                typeof(WorkflowId)));

        ConstructorInfo constructor =
            Assert.Single(
                typeof(WorkflowId)
                    .GetConstructors());

        Assert.Equal(
            [
                typeof(string)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());
    }

    [Fact]
    public void WorkflowExecutionEvent_PublicSurfaceIsFrozen()
    {
        Assert.Equal(
            [
                "Kind",
                "PreviousStepStatus",
                "PreviousWorkflowStatus",
                "Sequence",
                "TargetStepStatus",
                "TargetWorkflowStatus",
                "TaskId",
                "WorkflowId"
            ],
            DeclaredPublicProperties(
                typeof(WorkflowExecutionEvent)));

        Assert.Empty(
            typeof(WorkflowExecutionEvent)
                .GetConstructors());

        Assert.Equal(
            [
                WorkflowExecutionEventKind.WorkflowInitialized,
                WorkflowExecutionEventKind.WorkflowStatusTransitioned,
                WorkflowExecutionEventKind.StepStatusTransitioned
            ],
            Enum.GetValues<WorkflowExecutionEventKind>());

        Assert.Equal(
            1,
            (int) WorkflowExecutionEventKind.WorkflowInitialized);
        Assert.Equal(
            2,
            (int) WorkflowExecutionEventKind.WorkflowStatusTransitioned);
        Assert.Equal(
            3,
            (int) WorkflowExecutionEventKind.StepStatusTransitioned);

        Assert.False(
            Enum.IsDefined(
                typeof(WorkflowExecutionEventKind),
                0));
    }

    [Fact]
    public void WorkflowPersistenceSnapshot_PublicSurfaceIsFrozen()
    {
        Assert.Equal(
            [
                "LastEvent",
                "Revision",
                "State",
                "WorkflowId"
            ],
            DeclaredPublicProperties(
                typeof(WorkflowPersistenceSnapshot)));

        Assert.Empty(
            typeof(WorkflowPersistenceSnapshot)
                .GetConstructors());
    }

    [Fact]
    public void WorkflowPersistenceStore_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowPersistenceStore);

        FieldInfo[] fields =
            type.GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly);

        Assert.Equal(
            2,
            fields.Length);

        Assert.All(
            fields,
            field_ =>
                Assert.True(
                    field_.IsLiteral));

        Assert.Equal(
            "ai.repokit.workflow-persistence-record",
            WorkflowPersistenceStore.SchemaId);

        Assert.Equal(
            1,
            WorkflowPersistenceStore.CurrentSchemaVersion);

        Assert.Equal(
            [
                "StorageRoot",
                "WorkflowId"
            ],
            DeclaredPublicProperties(
                type));

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(string),
                typeof(WorkflowId)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.Equal(
            [
                "Append",
                "Initialize",
                "Load",
                "ReadEvents"
            ],
            type
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly)
                .Where(
                    method_ =>
                        !method_.IsSpecialName)
                .Select(
                    method_ =>
                        method_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void PublicApi_ContainsNoFuturePhaseConcepts()
    {
        string[] forbiddenTerms =
        [
            "Checkpoint",
            "Resume",
            "Recover",
            "Replay",
            "Scheduler",
            "Readiness",
            "Runnable",
            "Queue",
            "Priority",
            "Dispatch",
            "Concurrency",
            "Retry",
            "Attempt",
            "Backoff",
            "Fallback",
            "Repair",
            "SideEffect",
            "Idempot",
            "Reconcil",
            "AgentExecution",
            "ValidationEngine",
            "HumanGate",
            "Delete",
            "Overwrite",
            "Compact"
        ];

        Assembly assembly =
            typeof(WorkflowState).Assembly;

        foreach (Type type in assembly.GetExportedTypes())
        {
            AssertContainsNoForbiddenTerms(
                type.Name,
                forbiddenTerms);

            foreach (PropertyInfo property in
                     type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static))
            {
                AssertContainsNoForbiddenTerms(
                    property.Name,
                    forbiddenTerms);
            }

            foreach (MethodInfo method in
                     type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.Static |
                         BindingFlags.Instance |
                         BindingFlags.DeclaredOnly))
            {
                AssertContainsNoForbiddenTerms(
                    method.Name,
                    forbiddenTerms);
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
    public void Solution_ContainsOnlyTheExistingOrchestrationProjects()
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

    private static void AssertContainsNoForbiddenTerms(
        string value_,
        IReadOnlyList<string> forbiddenTerms_)
    {
        for (int index = 0; index < forbiddenTerms_.Count; index++)
        {
            Assert.DoesNotContain(
                forbiddenTerms_[index],
                value_,
                StringComparison.OrdinalIgnoreCase);
        }
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
