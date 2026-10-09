namespace AiRepoKit.WorkflowExecution.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "IValidationExecutor",
        "IWorkflowGateVerifier",
        "IWorkflowSideEffectReconciler",
        "ValidationExecutionResult",
        "WorkflowAgentStepExecutor",
        "WorkflowAgentStepResult",
        "WorkflowCheckpointCoordinator",
        "WorkflowCheckpointResult",
        "WorkflowGateChallenge",
        "WorkflowGateCoordinator",
        "WorkflowGatePolicy",
        "WorkflowGateProof",
        "WorkflowGateResult",
        "WorkflowGateStatus",
        "WorkflowGateVerificationResult",
        "WorkflowRepairAttempt",
        "WorkflowRepairCoordinator",
        "WorkflowRepairPolicy",
        "WorkflowRepairResult",
        "WorkflowSideEffectReconciliationResult",
        "WorkflowValidationEngine",
        "WorkflowValidationEvidence",
        "WorkflowValidationResult"
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

        Assert.Equal(
            [
                @"..\AiRepoKit.Agents.Abstractions\AiRepoKit.Agents.Abstractions.csproj",
                @"..\AiRepoKit.Execution\AiRepoKit.Execution.csproj",
                @"..\AiRepoKit.Orchestration\AiRepoKit.Orchestration.csproj"
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
                @"..\..\src\AiRepoKit.WorkflowExecution\AiRepoKit.WorkflowExecution.csproj"
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
    public void PublicSurface_ContainsExactlyTwentyThreeFrozenTypes()
    {
        Assembly assembly =
            typeof(WorkflowValidationEngine).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            23,
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
    public void WorkflowAgentStepExecutor_PublicSurfaceRemainsFrozen()
    {
        Type type =
            typeof(WorkflowAgentStepExecutor);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(IEnumerable<IAgentExecutor>)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        MethodInfo method =
            Assert.Single(
                DeclaredPublicMethods(
                    type));

        Assert.Equal(
            "ExecuteAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<WorkflowAgentStepResult>),
            method.ReturnType);

        Assert.Equal(
            [
                typeof(WorkflowState),
                typeof(ExecutionEnvelope),
                typeof(CancellationToken)
            ],
            method
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        FieldInfo field =
            Assert.Single(
                type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly));

        Assert.Equal(
            "AlgorithmId",
            field.Name);

        Assert.Equal(
            "ai.repokit.workflow-agent-step-executor/v1",
            field.GetRawConstantValue());
    }

    [Fact]
    public void WorkflowAgentStepResult_PublicSurfaceRemainsFrozen()
    {
        Type type =
            typeof(WorkflowAgentStepResult);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "AgentResult",
                "ProviderId",
                "State"
            ],
            DeclaredPublicProperties(
                type));
    }

    [Fact]
    public void IValidationExecutor_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(IValidationExecutor);

        Assert.True(
            type.IsInterface);

        Assert.Equal(
            [
                "Strategy"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(ValidationStrategy),
            type.GetProperty("Strategy")!.PropertyType);

        MethodInfo method =
            Assert.Single(
                DeclaredPublicMethods(
                    type));

        Assert.Equal(
            "ValidateAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<ValidationExecutionResult>),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            [
                typeof(ValidationRequirement),
                typeof(ExecutionEnvironment),
                typeof(CancellationToken)
            ],
            parameters
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            parameters[2].HasDefaultValue);
    }

    [Fact]
    public void ValidationExecutionResult_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(ValidationExecutionResult);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(bool),
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
                "Evidence",
                "Passed"
            ],
            DeclaredPublicProperties(
                type));
    }

    [Fact]
    public void WorkflowValidationEngine_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowValidationEngine);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(IEnumerable<IValidationExecutor>)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        MethodInfo method =
            Assert.Single(
                DeclaredPublicMethods(
                    type));

        Assert.Equal(
            "ValidateAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<WorkflowValidationResult>),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowState),
                typeof(ExecutableWork),
                typeof(ExecutionEnvironment),
                typeof(string),
                typeof(CancellationToken)
            ],
            parameters
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            parameters[4].HasDefaultValue);

        Assert.Empty(
            DeclaredPublicProperties(
                type));

        FieldInfo field =
            Assert.Single(
                type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly));

        Assert.Equal(
            "AlgorithmId",
            field.Name);

        Assert.Equal(
            "ai.repokit.workflow-validation-engine/v1",
            field.GetRawConstantValue());
    }

    [Fact]
    public void WorkflowValidationEvidence_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowValidationEvidence);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "Requirement",
                "ValidationResult"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(ValidationRequirement),
            type.GetProperty("Requirement")!.PropertyType);

        Assert.Equal(
            typeof(ValidationExecutionResult),
            type.GetProperty("ValidationResult")!.PropertyType);
    }

    [Fact]
    public void WorkflowValidationResult_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowValidationResult);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "Evidence",
                "State"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(WorkflowState),
            type.GetProperty("State")!.PropertyType);

        Assert.Equal(
            typeof(IReadOnlyList<WorkflowValidationEvidence>),
            type.GetProperty("Evidence")!.PropertyType);
    }

    [Fact]
    public void WorkflowRepairPolicy_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowRepairPolicy);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(int),
                typeof(int)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.Equal(
            [
                "MaxAttemptsPerProvider",
                "MaxTotalAttempts"
            ],
            DeclaredPublicProperties(
                type));
    }

    [Fact]
    public void WorkflowRepairAttempt_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowRepairAttempt);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "AgentResult",
                "AttemptNumber",
                "Prompt",
                "ProviderAttemptNumber",
                "ProviderId",
                "SemanticRepair",
                "ValidationEvidence"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(int),
            type.GetProperty("AttemptNumber")!.PropertyType);

        Assert.Equal(
            typeof(int),
            type.GetProperty("ProviderAttemptNumber")!.PropertyType);

        Assert.Equal(
            typeof(AgentProviderId),
            type.GetProperty("ProviderId")!.PropertyType);

        Assert.Equal(
            typeof(CompiledPrompt),
            type.GetProperty("Prompt")!.PropertyType);

        Assert.Equal(
            typeof(AgentExecutionResult),
            type.GetProperty("AgentResult")!.PropertyType);

        Assert.Equal(
            typeof(bool),
            type.GetProperty("SemanticRepair")!.PropertyType);

        Assert.Equal(
            typeof(IReadOnlyList<WorkflowValidationEvidence>),
            type.GetProperty("ValidationEvidence")!.PropertyType);
    }

    [Fact]
    public void WorkflowRepairResult_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowRepairResult);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "Attempts",
                "BudgetExhausted",
                "State"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(WorkflowState),
            type.GetProperty("State")!.PropertyType);

        Assert.Equal(
            typeof(IReadOnlyList<WorkflowRepairAttempt>),
            type.GetProperty("Attempts")!.PropertyType);

        Assert.Equal(
            typeof(bool),
            type.GetProperty("BudgetExhausted")!.PropertyType);
    }

    [Fact]
    public void WorkflowRepairCoordinator_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowRepairCoordinator);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(IEnumerable<IAgentExecutor>),
                typeof(IEnumerable<IValidationExecutor>),
                typeof(WorkflowRepairPolicy)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        MethodInfo method =
            Assert.Single(
                DeclaredPublicMethods(
                    type));

        Assert.Equal(
            "ExecuteAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<WorkflowRepairResult>),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowState),
                typeof(ExecutableWork),
                typeof(ExecutionEnvelope),
                typeof(CancellationToken)
            ],
            parameters
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            parameters[3].HasDefaultValue);

        Assert.Empty(
            DeclaredPublicProperties(
                type));

        FieldInfo field =
            Assert.Single(
                type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly));

        Assert.Equal(
            "AlgorithmId",
            field.Name);

        Assert.Equal(
            "ai.repokit.workflow-repair-coordinator/v1",
            field.GetRawConstantValue());
    }

    [Fact]
    public void WorkflowRepairCoordinator_ComposesFrozenP06P07Boundaries()
    {
        string source =
            File.ReadAllText(
                Path.Combine(
                    GetProductionDirectory(),
                    "WorkflowRepairCoordinator.cs"));

        Assert.Contains(
            "WorkflowAgentStepExecutor",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowValidationEngine",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowStateMachine.TransitionStep(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "ExecutionEnvelope.Create(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "AIRepoKit.SemanticRepair/v1",
            source,
            StringComparison.Ordinal);

        foreach (string forbidden in new[]
        {
            "AgentExecutionRequest(",
            "Process.Start(",
            "HttpClient",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Antigravity",
            "TransitionWorkflow(",
            "DateTime.",
            "DateTimeOffset.",
            "Guid.",
            "Random."
        })
        {
            Assert.DoesNotContain(
                forbidden,
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void IWorkflowSideEffectReconciler_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(IWorkflowSideEffectReconciler);

        Assert.True(
            type.IsInterface);

        Assert.Empty(
            DeclaredPublicProperties(
                type));

        MethodInfo[] methods =
            DeclaredPublicMethods(
                type)
            .OrderBy(
                method_ =>
                    method_.Name,
                StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            2,
            methods.Length);

        MethodInfo agentMethod =
            methods[0];

        Assert.Equal(
            "ReconcileAgentAsync",
            agentMethod.Name);

        Assert.Equal(
            typeof(Task<WorkflowSideEffectReconciliationResult>),
            agentMethod.ReturnType);

        ParameterInfo[] agentParams =
            agentMethod.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowId),
                typeof(string),
                typeof(string),
                typeof(long),
                typeof(int),
                typeof(AgentProviderId),
                typeof(AgentExecutionRequest),
                typeof(CancellationToken)
            ],
            agentParams
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            agentParams[7].HasDefaultValue);

        MethodInfo validationMethod =
            methods[1];

        Assert.Equal(
            "ReconcileValidationAsync",
            validationMethod.Name);

        Assert.Equal(
            typeof(Task<WorkflowSideEffectReconciliationResult>),
            validationMethod.ReturnType);

        ParameterInfo[] valParams =
            validationMethod.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowId),
                typeof(string),
                typeof(string),
                typeof(long),
                typeof(int),
                typeof(ValidationRequirement),
                typeof(ExecutionEnvironment),
                typeof(CancellationToken)
            ],
            valParams
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            valParams[7].HasDefaultValue);
    }

    [Fact]
    public void WorkflowSideEffectReconciliationResult_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowSideEffectReconciliationResult);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(bool),
                typeof(AgentExecutionResult),
                typeof(ValidationExecutionResult)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.Equal(
            [
                "AgentResult",
                "IsProvenNotExecuted",
                "ValidationResult"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(bool),
            type.GetProperty("IsProvenNotExecuted")!.PropertyType);

        Assert.Equal(
            typeof(AgentExecutionResult),
            type.GetProperty("AgentResult")!.PropertyType);

        Assert.Equal(
            typeof(ValidationExecutionResult),
            type.GetProperty("ValidationResult")!.PropertyType);
    }

    [Fact]
    public void WorkflowCheckpointResult_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowCheckpointResult);

        Assert.Empty(
            type.GetConstructors());

        Assert.Equal(
            [
                "ReconciliationUsed",
                "RepairResult",
                "Resumed",
                "Snapshot"
            ],
            DeclaredPublicProperties(
                type));

        Assert.Equal(
            typeof(WorkflowPersistenceSnapshot),
            type.GetProperty("Snapshot")!.PropertyType);

        Assert.Equal(
            typeof(WorkflowRepairResult),
            type.GetProperty("RepairResult")!.PropertyType);

        Assert.Equal(
            typeof(bool),
            type.GetProperty("Resumed")!.PropertyType);

        Assert.Equal(
            typeof(bool),
            type.GetProperty("ReconciliationUsed")!.PropertyType);
    }

    [Fact]
    public void WorkflowCheckpointCoordinator_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(WorkflowCheckpointCoordinator);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        Assert.Equal(
            [
                typeof(WorkflowPersistenceStore),
                typeof(IEnumerable<IAgentExecutor>),
                typeof(IEnumerable<IValidationExecutor>),
                typeof(WorkflowRepairPolicy),
                typeof(IWorkflowSideEffectReconciler)
            ],
            constructor
                .GetParameters()
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        MethodInfo method =
            Assert.Single(
                DeclaredPublicMethods(
                    type));

        Assert.Equal(
            "ExecuteAsync",
            method.Name);

        Assert.Equal(
            typeof(Task<WorkflowCheckpointResult>),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            [
                typeof(ExecutableWork),
                typeof(ExecutionEnvelope),
                typeof(CancellationToken)
            ],
            parameters
                .Select(
                    parameter_ =>
                        parameter_.ParameterType)
                .ToArray());

        Assert.True(
            parameters[2].HasDefaultValue);

        Assert.Empty(
            DeclaredPublicProperties(
                type));

        FieldInfo[] fields =
            type.GetFields(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .OrderBy(
                field_ =>
                    field_.Name,
                StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            3,
            fields.Length);

        Assert.Equal(
            "AlgorithmId",
            fields[0].Name);

        Assert.Equal(
            "ai.repokit.workflow-checkpoint-coordinator/v1",
            fields[0].GetRawConstantValue());

        Assert.Equal(
            "JournalSchemaId",
            fields[1].Name);

        Assert.Equal(
            "ai.repokit.workflow-execution-journal-record",
            fields[1].GetRawConstantValue());

        Assert.Equal(
            "JournalSchemaVersion",
            fields[2].Name);

        Assert.Equal(
            1,
            fields[2].GetRawConstantValue());
    }

    [Fact]
    public void WorkflowCheckpointCoordinator_ComposesFrozenBoundaries()
    {
        string source =
            File.ReadAllText(
                Path.Combine(
                    GetProductionDirectory(),
                    "WorkflowCheckpointCoordinator.cs"));

        Assert.Contains(
            "WorkflowRepairCoordinator",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowCheckpointJournal",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowCheckpointAgentExecutor",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowCheckpointValidationExecutor",
            source,
            StringComparison.Ordinal);

        foreach (string forbidden in new[]
        {
            "Process.Start(",
            "HttpClient",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Antigravity",
            "DateTime.",
            "DateTimeOffset.",
            "Random."
        })
        {
            Assert.DoesNotContain(
                forbidden,
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductionSource_RespectsFrozenP09FuturePhaseExclusions()
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

        // P10 Human Gates, P11 Evidence Packs / Audit Trail, P12 Controlled Concurrency, P13 Promptfoo Golden Security
        foreach (string forbidden in new[]
        {
            "Promptfoo",
            "HumanGate",
            "HumanApproval",
            "EvidencePackExport",
            "AuditExport",
            "AuditTrail",
            "ControlledConcurrency",
            "AutonomousSchedule"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Solution_ContainsExistingWorkflowExecutionProjectsExactlyOnce()
    {
        string solution =
            File.ReadAllText(
                GetSolutionPath());

        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                @"src\AiRepoKit.WorkflowExecution\AiRepoKit.WorkflowExecution.csproj"));

        Assert.Equal(
            1,
            CountOccurrences(
                solution,
                @"tests\AiRepoKit.WorkflowExecution.Tests\AiRepoKit.WorkflowExecution.Tests.csproj"));
    }

    [Fact]
    public void ProductionSource_RespectsFrozenP07Boundary()
    {
        string[] files =
            Directory
                .GetFiles(
                    GetProductionDirectory(),
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .OrderBy(
                    path_ =>
                        path_,
                    StringComparer.Ordinal)
                .ToArray();

        string nonJournalSource =
            string.Join(
                "\n",
                files
                    .Where(
                        path_ =>
                            !Path.GetFileName(path_).Equals("WorkflowCheckpointJournal.cs", StringComparison.Ordinal))
                    .Select(
                        File.ReadAllText));

        foreach (string forbidden in new[]
        {
            "DateTime.",
            "DateTimeOffset.",
            "Guid.",
            "Random.",
            "HttpClient",
            "System.Diagnostics",
            "AiRepoKit.Git",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Antigravity",
            "Microsoft.Agents",
            "Microsoft.Extensions.AI"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                nonJournalSource,
                StringComparison.Ordinal);
        }

        string journalSource =
            File.ReadAllText(
                Path.Combine(
                    GetProductionDirectory(),
                    "WorkflowCheckpointJournal.cs"));

        foreach (string forbidden in new[]
        {
            "DateTime.",
            "DateTimeOffset.",
            "Random.",
            "HttpClient",
            "System.Diagnostics",
            "AiRepoKit.Git",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Antigravity",
            "Microsoft.Agents",
            "Microsoft.Extensions.AI"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                journalSource,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            ".tmp\");",
            journalSource,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            CountOccurrences(
                journalSource,
                "Guid.NewGuid()"));

        string fullSource =
            string.Join("\n", files.Select(File.ReadAllText));

        Assert.Contains(
            "WorkflowValidationEngine",
            fullSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "ValidationStrategy",
            fullSource,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowStateMachine.TransitionStep(",
            fullSource,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Process.Start(",
            fullSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void P06AgentCompletionBoundary_RemainsFrozen()
    {
        string source =
            File.ReadAllText(
                Path.Combine(
                    GetProductionDirectory(),
                    "WorkflowAgentStepExecutor.cs"));

        Assert.Contains(
            "WorkflowStepStatus.AwaitingValidation",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WorkflowStepStatus.Completed",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void P07ValidationEngine_OwnsOnlyValidationCompletionTransition()
    {
        string source =
            File.ReadAllText(
                Path.Combine(
                    GetProductionDirectory(),
                    "WorkflowValidationEngine.cs"));

        Assert.Contains(
            "WorkflowStepStatus.Completed",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowStepStatus.Failed",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WorkflowStepStatus.Blocked",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "TransitionWorkflow(",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowGateStatus_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGateStatus);

        Assert.True(type.IsEnum);

        Assert.Equal(
            [
                "Approved",
                "Bypassed",
                "Denied",
                "Pending"
            ],
            Enum.GetNames(type).OrderBy(name_ => name_, StringComparer.Ordinal).ToArray());

        Assert.Equal(1, (int) WorkflowGateStatus.Pending);
        Assert.Equal(2, (int) WorkflowGateStatus.Denied);
        Assert.Equal(3, (int) WorkflowGateStatus.Approved);
        Assert.Equal(4, (int) WorkflowGateStatus.Bypassed);
    }

    [Fact]
    public void WorkflowGatePolicy_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGatePolicy);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());

        Assert.Equal(
            [
                typeof(bool),
                typeof(bool),
                typeof(bool)
            ],
            constructor.GetParameters().Select(parameter_ => parameter_.ParameterType).ToArray());

        Assert.Equal(
            [
                "RequireGateForReadOnly",
                "RequireGateForUnrestricted",
                "RequireGateForWorkspaceWrite"
            ],
            DeclaredPublicProperties(type));

        MethodInfo method = Assert.Single(DeclaredPublicMethods(type));

        Assert.Equal("RequiresGate", method.Name);
        Assert.Equal(typeof(bool), method.ReturnType);
        Assert.Equal([typeof(ExecutionPermission)], method.GetParameters().Select(parameter_ => parameter_.ParameterType).ToArray());
    }

    [Fact]
    public void WorkflowGateChallenge_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGateChallenge);

        Assert.Empty(type.GetConstructors());

        Assert.Equal(
            [
                "BasePersistenceRevision",
                "BaseStateFingerprint",
                "GateId",
                "GateOrdinal",
                "InputFingerprint",
                "PolicyFingerprint",
                "RegistryFingerprint",
                "RequiredPermission",
                "SourceImplementationPlanRevision",
                "TaskId",
                "WorkflowId"
            ],
            DeclaredPublicProperties(type));
    }

    [Fact]
    public void WorkflowGateProof_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGateProof);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());

        Assert.Equal(
            [
                typeof(string),
                typeof(string)
            ],
            constructor.GetParameters().Select(parameter_ => parameter_.ParameterType).ToArray());

        Assert.Equal(
            [
                "GateId",
                "OpaqueEvidence"
            ],
            DeclaredPublicProperties(type));
    }

    [Fact]
    public void WorkflowGateVerificationResult_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGateVerificationResult);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());

        Assert.Equal(
            [
                typeof(bool),
                typeof(bool),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(string)
            ],
            constructor.GetParameters().Select(parameter_ => parameter_.ParameterType).ToArray());

        Assert.Equal(
            [
                "EvidenceFingerprint",
                "FailureReason",
                "IsAuthenticated",
                "IsAuthorized",
                "MechanismId",
                "PrincipalId"
            ],
            DeclaredPublicProperties(type));
    }

    [Fact]
    public void IWorkflowGateVerifier_PublicSurfaceIsFrozen()
    {
        Type type = typeof(IWorkflowGateVerifier);

        Assert.True(type.IsInterface);

        MethodInfo method = Assert.Single(DeclaredPublicMethods(type));

        Assert.Equal("VerifyAsync", method.Name);
        Assert.Equal(typeof(Task<WorkflowGateVerificationResult>), method.ReturnType);

        ParameterInfo[] parameters = method.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowGateChallenge),
                typeof(WorkflowGateProof),
                typeof(CancellationToken)
            ],
            parameters.Select(parameter_ => parameter_.ParameterType).ToArray());

        Assert.True(parameters[2].HasDefaultValue);
    }

    [Fact]
    public void WorkflowGateResult_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGateResult);

        Assert.Empty(type.GetConstructors());

        Assert.Equal(
            [
                "Challenge",
                "CheckpointResult",
                "Snapshot",
                "Status"
            ],
            DeclaredPublicProperties(type));
    }

    [Fact]
    public void WorkflowGateCoordinator_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowGateCoordinator);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());

        Assert.Equal(
            [
                typeof(WorkflowPersistenceStore),
                typeof(IEnumerable<IAgentExecutor>),
                typeof(IEnumerable<IValidationExecutor>),
                typeof(WorkflowRepairPolicy),
                typeof(IWorkflowSideEffectReconciler),
                typeof(WorkflowGatePolicy),
                typeof(IWorkflowGateVerifier)
            ],
            constructor.GetParameters().Select(parameter_ => parameter_.ParameterType).ToArray());

        MethodInfo method = Assert.Single(DeclaredPublicMethods(type));

        Assert.Equal("ExecuteAsync", method.Name);
        Assert.Equal(typeof(Task<WorkflowGateResult>), method.ReturnType);

        ParameterInfo[] parameters = method.GetParameters();

        Assert.Equal(
            [
                typeof(ExecutableWork),
                typeof(ExecutionEnvelope),
                typeof(WorkflowGateProof),
                typeof(CancellationToken)
            ],
            parameters.Select(parameter_ => parameter_.ParameterType).ToArray());

        Assert.True(parameters[2].HasDefaultValue);
        Assert.True(parameters[3].HasDefaultValue);

        FieldInfo[] fields = type.GetFields(
            BindingFlags.Public |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly);

        Assert.Equal(3, fields.Length);

        FieldInfo algorithmIdField = Assert.Single(fields, field_ => field_.Name == "AlgorithmId");
        Assert.Equal("ai.repokit.workflow-gate-coordinator/v1", algorithmIdField.GetRawConstantValue());

        FieldInfo schemaIdField = Assert.Single(fields, field_ => field_.Name == "JournalSchemaId");
        Assert.Equal("ai.repokit.workflow-gate-journal-record", schemaIdField.GetRawConstantValue());

        FieldInfo versionField = Assert.Single(fields, field_ => field_.Name == "JournalSchemaVersion");
        Assert.Equal(1, versionField.GetRawConstantValue());
    }

    [Fact]
    public void P10_ProductionSource_HasNoDisallowedDependenciesOrSideEffects()
    {
        string[] p10Files =
        [
            "WorkflowGateStatus.cs",
            "WorkflowGatePolicy.cs",
            "WorkflowGateChallenge.cs",
            "WorkflowGateProof.cs",
            "WorkflowGateVerificationResult.cs",
            "IWorkflowGateVerifier.cs",
            "WorkflowGateResult.cs",
            "WorkflowGateCoordinator.cs",
            "WorkflowGateJournal.cs"
        ];

        foreach (string file in p10Files)
        {
            string source = File.ReadAllText(Path.Combine(GetProductionDirectory(), file));

            Assert.DoesNotContain("AiRepoKit.Spec", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AiRepoKit.Cli", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AiRepoKit.Git", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Diagnostics.Process", source, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Net.Http", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.Extensions.AI", source, StringComparison.Ordinal);
        }
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
                    !method_.IsSpecialName &&
                    method_.Name != "Equals" &&
                    method_.Name != "GetHashCode" &&
                    method_.Name != "PrintMembers" &&
                    method_.Name != "ToString" &&
                    !method_.Name.StartsWith("<Clone>$", StringComparison.Ordinal))
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
            "AiRepoKit.WorkflowExecution");
    }

    private static string GetProductionProjectPath()
    {
        return Path.Combine(
            GetProductionDirectory(),
            "AiRepoKit.WorkflowExecution.csproj");
    }

    private static string GetTestProjectPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "tests",
            "AiRepoKit.WorkflowExecution.Tests",
            "AiRepoKit.WorkflowExecution.Tests.csproj");
    }

    private static string GetSolutionPath()
    {
        return Path.Combine(
            GetRepositoryRoot(),
            "AI.RepoKit.MCP.sln");
    }
}
