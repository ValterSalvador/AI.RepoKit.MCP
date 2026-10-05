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
        "ValidationExecutionResult",
        "WorkflowAgentStepExecutor",
        "WorkflowAgentStepResult",
        "WorkflowRepairAttempt",
        "WorkflowRepairCoordinator",
        "WorkflowRepairPolicy",
        "WorkflowRepairResult",
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
    public void PublicSurface_ContainsExactlyElevenFrozenTypes()
    {
        Assembly assembly =
            typeof(WorkflowValidationEngine).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            11,
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
                source,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "WorkflowValidationEngine",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "ValidationStrategy",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowStateMachine.TransitionStep(",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Process.Start(",
            source,
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
