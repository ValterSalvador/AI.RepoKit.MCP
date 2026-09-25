namespace AiRepoKit.Execution.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using Xunit;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "AgentRequirement",
        "CompiledContext",
        "CompiledContextItem",
        "CompiledPrompt",
        "ContextCandidate",
        "ContextCompiler",
        "DeterministicWorkEstimator",
        "ExecutableTask",
        "ExecutableTaskDependency",
        "ExecutableTaskEstimate",
        "ExecutableWork",
        "ModelRequirement",
        "PromptCompiler",
        "ValidationRequirement",
        "ValidationStrategy"
    ];

    [Fact]
    public void PublicSurface_ContainsExactlyFifteenFrozenTypes()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            15,
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
    public void ValidationRequirement_PublicPropertiesRemainFrozen()
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
    public void ValidationStrategy_PublicValuesRemainFrozen()
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
    public void ModelRequirement_PublicPropertiesAreFrozen()
    {
        string[] propertyNames =
            typeof(ModelRequirement)
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
                "RequiredCapabilities",
                "TaskId"
            ],
            propertyNames);
    }

    [Fact]
    public void AgentRequirement_PublicPropertiesAreFrozen()
    {
        string[] propertyNames =
            typeof(AgentRequirement)
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
                "RequiredCapabilities",
                "TaskId"
            ],
            propertyNames);
    }

    [Fact]
    public void ModelAndAgentRequirement_PublicConstructorsAreFrozen()
    {
        foreach (Type type in new[]
        {
            typeof(ModelRequirement),
            typeof(AgentRequirement)
        })
        {
            ConstructorInfo[] constructors =
                type.GetConstructors();

            Assert.Single(
                constructors);

            ParameterInfo[] parameters =
                constructors[0].GetParameters();

            Assert.Equal(
                2,
                parameters.Length);

            Assert.Equal(
                typeof(string),
                parameters[0].ParameterType);

            Assert.Equal(
                typeof(AgentCapabilitySet),
                parameters[1].ParameterType);
        }
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
    public void ExecutableWork_ExposesFrozenModelRequirementsProperty()
    {
        PropertyInfo? property =
            typeof(ExecutableWork).GetProperty(
                nameof(ExecutableWork.ModelRequirements));

        Assert.NotNull(
            property);

        Assert.Equal(
            typeof(IReadOnlyList<ModelRequirement>),
            property.PropertyType);
    }

    [Fact]
    public void ExecutableWork_ExposesFrozenAgentRequirementsProperty()
    {
        PropertyInfo? property =
            typeof(ExecutableWork).GetProperty(
                nameof(ExecutableWork.AgentRequirements));

        Assert.NotNull(
            property);

        Assert.Equal(
            typeof(IReadOnlyList<AgentRequirement>),
            property.PropertyType);
    }

    [Fact]
    public void ProductionProject_HasZeroPackageReferences()
    {
        XDocument document =
            XDocument.Load(
                GetProductionProjectPath());

        Assert.Empty(
            document.Descendants("PackageReference"));
    }

    [Fact]
    public void ProductionProject_HasExactlyOneAgentsAbstractionsProjectReference()
    {
        XDocument document =
            XDocument.Load(
                GetProductionProjectPath());

        XElement[] references =
            document
                .Descendants("ProjectReference")
                .ToArray();

        Assert.Single(
            references);

        Assert.Equal(
            @"..\AiRepoKit.Agents.Abstractions\AiRepoKit.Agents.Abstractions.csproj",
            references[0].Attribute("Include")?.Value);
    }

    [Fact]
    public void Assembly_ReferencesOnlyBclAndAgentsAbstractions()
    {
        Assembly assembly =
            typeof(ExecutableWork).Assembly;

        AssemblyName[] references =
            assembly.GetReferencedAssemblies();

        AssemblyName[] agentsReferences =
            references
                .Where(
                    reference_ =>
                        string.Equals(
                            reference_.Name,
                            "AiRepoKit.Agents.Abstractions",
                            StringComparison.Ordinal))
                .ToArray();

        Assert.Single(
            agentsReferences);

        string[] forbiddenAssemblies =
        [
            "AiRepoKit.Spec",
            "AiRepoKit.Agents.Antigravity",
            "AiRepoKit.Agents.Codex",
            "AiRepoKit.Agents.Runtime",
            "AiRepoKit.Cli",
            "Microsoft.Extensions.AI"
        ];

        foreach (AssemblyName reference in references)
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

            bool allowed =
                name == "AiRepoKit.Agents.Abstractions" ||
                name == "System.Runtime" ||
                name.StartsWith(
                    "System.",
                    StringComparison.Ordinal) ||
                name == "mscorlib" ||
                name == "netstandard";

            Assert.True(
                allowed,
                $"Unexpected production assembly reference: {name}");
        }
    }

    [Fact]
    public void PublicApi_ContainsNoDeferredOrConcreteSelectionConcepts()
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
            "Selector",
            "Router",
            "Resolver",
            "ModelRoute",
            "ModelHealth",
            "ProviderId",
            "ModelId",
            "Fallback",
            "Retry",
            "ComplexityCategory",
            "RetrievalMetrics",
            "RetrievalBenchmarkEvaluator",
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
    public void PublicApi_ContainsNoSpecOrRuntimeTypes()
    {
        Assembly executionAssembly =
            typeof(ExecutableWork).Assembly;

        foreach (Type type in executionAssembly.GetExportedTypes())
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                string propertyTypeName =
                    property.PropertyType.FullName ??
                    string.Empty;

                Assert.DoesNotContain(
                    "AiRepoKit.Spec",
                    propertyTypeName,
                    StringComparison.Ordinal);

                Assert.DoesNotContain(
                    "AiRepoKit.Agents.Runtime",
                    propertyTypeName,
                    StringComparison.Ordinal);
            }

            foreach (ConstructorInfo constructor in type.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    string parameterTypeName =
                        parameter.ParameterType.FullName ??
                        string.Empty;

                    Assert.DoesNotContain(
                        "AiRepoKit.Spec",
                        parameterTypeName,
                        StringComparison.Ordinal);

                    Assert.DoesNotContain(
                        "AiRepoKit.Agents.Runtime",
                        parameterTypeName,
                        StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void ExecutableTaskEstimate_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(ExecutableTaskEstimate);

        Assert.True(
            type.IsSealed);

        string[] propertyNames =
            type
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
                "ComplexityScore",
                "EstimatedInstructionTokens",
                "TaskId"
            ],
            propertyNames);

        ConstructorInfo[] constructors =
            type.GetConstructors();

        Assert.Single(
            constructors);

        ParameterInfo[] parameters =
            constructors[0].GetParameters();

        Assert.Equal(
            3,
            parameters.Length);

        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);

        Assert.Equal(
            "taskId_",
            parameters[0].Name);

        Assert.Equal(
            typeof(int),
            parameters[1].ParameterType);

        Assert.Equal(
            "complexityScore_",
            parameters[1].Name);

        Assert.Equal(
            typeof(int),
            parameters[2].ParameterType);

        Assert.Equal(
            "estimatedInstructionTokens_",
            parameters[2].Name);
    }

    [Fact]
    public void DeterministicWorkEstimator_IsStaticWithFrozenConstants()
    {
        Type type =
            typeof(DeterministicWorkEstimator);

        Assert.True(
            type.IsAbstract);

        Assert.True(
            type.IsSealed);

        Assert.Empty(
            type.GetConstructors());

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

        Assert.Equal(
            "AlgorithmId",
            fields[0].Name);

        Assert.True(
            fields[0].IsLiteral);

        Assert.Equal(
            "ai.repokit.deterministic-work-estimator/v1",
            fields[0].GetRawConstantValue());

        Assert.Equal(
            "CharactersPerEstimatedToken",
            fields[1].Name);

        Assert.True(
            fields[1].IsLiteral);

        Assert.Equal(
            4,
            fields[1].GetRawConstantValue());

        Assert.Empty(
            type.GetNestedTypes(
                BindingFlags.Public));
    }

    [Fact]
    public void DeterministicWorkEstimator_PublicMethodsAreFrozen()
    {
        MethodInfo[] methods =
            typeof(DeterministicWorkEstimator)
                .GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .OrderBy(
                    method_ =>
                        method_.Name,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            2,
            methods.Length);

        Assert.Equal(
            "Estimate",
            methods[0].Name);

        Assert.Equal(
            typeof(IReadOnlyList<ExecutableTaskEstimate>),
            methods[0].ReturnType);

        ParameterInfo[] estimateParameters =
            methods[0].GetParameters();

        Assert.Single(
            estimateParameters);

        Assert.Equal(
            typeof(ExecutableWork),
            estimateParameters[0].ParameterType);

        Assert.Equal(
            "work_",
            estimateParameters[0].Name);

        Assert.Equal(
            "EstimateTokens",
            methods[1].Name);

        Assert.Equal(
            typeof(int),
            methods[1].ReturnType);

        ParameterInfo[] tokenParameters =
            methods[1].GetParameters();

        Assert.Single(
            tokenParameters);

        Assert.Equal(
            typeof(string),
            tokenParameters[0].ParameterType);

        Assert.Equal(
            "text_",
            tokenParameters[0].Name);
    }
    [Fact]
    public void ContextCandidate_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(ContextCandidate);

        Assert.True(
            type.IsSealed);

        string[] propertyNames =
            type
                .GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly)
                .Select(property_ => property_.Name)
                .OrderBy(
                    name_ => name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            [
                "Content",
                "Id",
                "RelevanceScore"
            ],
            propertyNames);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        ParameterInfo[] parameters =
            constructor.GetParameters();

        Assert.Equal(
            3,
            parameters.Length);

        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);

        Assert.Equal(
            "id_",
            parameters[0].Name);

        Assert.Equal(
            typeof(string),
            parameters[1].ParameterType);

        Assert.Equal(
            "content_",
            parameters[1].Name);

        Assert.Equal(
            typeof(int),
            parameters[2].ParameterType);

        Assert.Equal(
            "relevanceScore_",
            parameters[2].Name);
    }

    [Fact]
    public void CompiledContextItem_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(CompiledContextItem);

        Assert.True(
            type.IsSealed);

        string[] propertyNames =
            type
                .GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly)
                .Select(property_ => property_.Name)
                .OrderBy(
                    name_ => name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            [
                "Content",
                "EstimatedTokens",
                "Id",
                "RelevanceScore"
            ],
            propertyNames);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        ParameterInfo[] parameters =
            constructor.GetParameters();

        Assert.Equal(
            4,
            parameters.Length);

        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);

        Assert.Equal(
            "id_",
            parameters[0].Name);

        Assert.Equal(
            typeof(string),
            parameters[1].ParameterType);

        Assert.Equal(
            "content_",
            parameters[1].Name);

        Assert.Equal(
            typeof(int),
            parameters[2].ParameterType);

        Assert.Equal(
            "relevanceScore_",
            parameters[2].Name);

        Assert.Equal(
            typeof(int),
            parameters[3].ParameterType);

        Assert.Equal(
            "estimatedTokens_",
            parameters[3].Name);
    }

    [Fact]
    public void CompiledContext_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(CompiledContext);

        Assert.True(
            type.IsSealed);

        string[] propertyNames =
            type
                .GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly)
                .Select(property_ => property_.Name)
                .OrderBy(
                    name_ => name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            [
                "EstimatedTokens",
                "ItemLimit",
                "Items",
                "OmittedCandidateIds",
                "TokenBudget",
                "Truncated"
            ],
            propertyNames);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        ParameterInfo[] parameters =
            constructor.GetParameters();

        Assert.Equal(
            6,
            parameters.Length);

        Assert.Equal(
            typeof(int),
            parameters[0].ParameterType);

        Assert.Equal(
            typeof(int),
            parameters[1].ParameterType);

        Assert.Equal(
            typeof(int),
            parameters[2].ParameterType);

        Assert.Equal(
            typeof(bool),
            parameters[3].ParameterType);

        Assert.Equal(
            typeof(IReadOnlyList<CompiledContextItem>),
            parameters[4].ParameterType);

        Assert.Equal(
            typeof(IReadOnlyList<string>),
            parameters[5].ParameterType);
    }

    [Fact]
    public void ContextCompiler_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(ContextCompiler);

        Assert.True(
            type.IsAbstract);

        Assert.True(
            type.IsSealed);

        Assert.Empty(
            type.GetConstructors());

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
            "ai.repokit.context-compiler/v1",
            field.GetRawConstantValue());

        MethodInfo method =
            Assert.Single(
                type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly));

        Assert.Equal(
            "Compile",
            method.Name);

        Assert.Equal(
            typeof(CompiledContext),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            3,
            parameters.Length);

        Assert.Equal(
            typeof(IReadOnlyList<ContextCandidate>),
            parameters[0].ParameterType);

        Assert.Equal(
            "candidates_",
            parameters[0].Name);

        Assert.Equal(
            typeof(int),
            parameters[1].ParameterType);

        Assert.Equal(
            "tokenBudget_",
            parameters[1].Name);

        Assert.Equal(
            typeof(int),
            parameters[2].ParameterType);

        Assert.Equal(
            "itemLimit_",
            parameters[2].Name);

        Assert.Empty(
            type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly));

        Assert.Empty(
            type.GetNestedTypes(
                BindingFlags.Public));
    }
    [Fact]
    public void CompiledPrompt_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(CompiledPrompt);

        Assert.True(
            type.IsSealed);

        string[] propertyNames =
            type
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
                "Content",
                "EstimatedTokens",
                "TaskId"
            ],
            propertyNames);

        ConstructorInfo constructor =
            Assert.Single(
                type.GetConstructors());

        ParameterInfo[] parameters =
            constructor.GetParameters();

        Assert.Equal(
            2,
            parameters.Length);

        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);

        Assert.Equal(
            "taskId_",
            parameters[0].Name);

        Assert.Equal(
            typeof(string),
            parameters[1].ParameterType);

        Assert.Equal(
            "content_",
            parameters[1].Name);
    }

    [Fact]
    public void PromptCompiler_PublicSurfaceIsFrozen()
    {
        Type type =
            typeof(PromptCompiler);

        Assert.True(
            type.IsAbstract);

        Assert.True(
            type.IsSealed);

        Assert.Empty(
            type.GetConstructors());

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
            "ai.repokit.prompt-compiler/v1",
            field.GetRawConstantValue());

        MethodInfo method =
            Assert.Single(
                type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.DeclaredOnly));

        Assert.Equal(
            "Compile",
            method.Name);

        Assert.Equal(
            typeof(CompiledPrompt),
            method.ReturnType);

        ParameterInfo[] parameters =
            method.GetParameters();

        Assert.Equal(
            3,
            parameters.Length);

        Assert.Equal(
            typeof(ExecutableWork),
            parameters[0].ParameterType);

        Assert.Equal(
            "work_",
            parameters[0].Name);

        Assert.Equal(
            typeof(string),
            parameters[1].ParameterType);

        Assert.Equal(
            "taskId_",
            parameters[1].Name);

        Assert.Equal(
            typeof(CompiledContext),
            parameters[2].ParameterType);

        Assert.Equal(
            "context_",
            parameters[2].Name);

        Assert.Empty(
            type.GetProperties(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly));

        Assert.Empty(
            type.GetNestedTypes(
                BindingFlags.Public));
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
