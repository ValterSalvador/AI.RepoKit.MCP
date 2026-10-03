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
        "WorkflowAgentStepExecutor",
        "WorkflowAgentStepResult"
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
    public void PublicSurface_ContainsExactlyTwoFrozenTypes()
    {
        Assembly assembly =
            typeof(WorkflowAgentStepExecutor).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            2,
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
    public void WorkflowAgentStepExecutor_PublicSurfaceIsFrozen()
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

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowState),
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

        FieldInfo field =
            Assert.Single(
                type.GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly));

        Assert.Equal(
            "AlgorithmId",
            field.Name);

        Assert.True(
            field.IsLiteral);

        Assert.Equal(
            "ai.repokit.workflow-agent-step-executor/v1",
            field.GetRawConstantValue());
    }

    [Fact]
    public void WorkflowAgentStepResult_PublicSurfaceIsFrozen()
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

        Assert.Equal(
            typeof(WorkflowState),
            type.GetProperty(
                "State")!.PropertyType);

        Assert.Equal(
            typeof(AgentProviderId),
            type.GetProperty(
                "ProviderId")!.PropertyType);

        Assert.Equal(
            typeof(AgentExecutionResult),
            type.GetProperty(
                "AgentResult")!.PropertyType);
    }

    [Fact]
    public void Solution_ContainsExactlyTheNewWorkflowExecutionProjectsByPath()
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
    public void ProductionSource_RespectsFrozenP06Boundary()
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
            "ValidationEngine",
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
            "SupportsAll(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "string.CompareOrdinal(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "WorkflowStateMachine.TransitionStep(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "CancellationTokenSource.CreateLinkedTokenSource(",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "new TimeoutException(",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "WorkflowStepStatus.Completed",
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
