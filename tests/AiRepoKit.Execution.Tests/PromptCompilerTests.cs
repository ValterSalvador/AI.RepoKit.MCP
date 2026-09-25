namespace AiRepoKit.Execution.Tests;

using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using Xunit;

public sealed class PromptCompilerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void CompiledPrompt_RejectsInvalidTaskId(
        string? taskId_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new CompiledPrompt(
                    taskId_!,
                    "{}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CompiledPrompt_RejectsNullOrEmptyContent(
        string? content_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new CompiledPrompt(
                    "task-a",
                    content_!));
    }

    [Fact]
    public void CompiledPrompt_PreservesExactValuesAndDerivesTokens()
    {
        CompiledPrompt prompt =
            new(
                " task-a ",
                "   ");

        Assert.Equal(
            " task-a ",
            prompt.TaskId);

        Assert.Equal(
            "   ",
            prompt.Content);

        Assert.Equal(
            DeterministicWorkEstimator.EstimateTokens(
                prompt.Content),
            prompt.EstimatedTokens);
    }

    [Fact]
    public void Compile_ProducesExactCanonicalCompactJson()
    {
        ExecutableWork work =
            CreateRichWork();

        CompiledContext context =
            CreateRichContext();

        CompiledPrompt prompt =
            PromptCompiler.Compile(
                work,
                "task-b",
                context);

        const string expected =
            """{"algorithmId":"ai.repokit.prompt-compiler/v1","task":{"id":"task-b","sourcePlanStepId":"plan-step-2","instruction":"Perform target task"},"context":{"truncated":true,"items":[{"id":"ctx-z","content":"context z"},{"id":"ctx-a","content":"context a"}]},"validationRequirements":[{"id":"val-a","sourceAcceptanceCriterionId":"ac-1","strategy":"Test","statement":"Run tests"},{"id":"val-z","sourceAcceptanceCriterionId":"ac-2","strategy":"Policy","statement":"Check policy"}],"modelRequiredCapabilities":["structured-output","write"],"agentRequiredCapabilities":["read","write"]}""";

        Assert.Equal(
            "task-b",
            prompt.TaskId);

        Assert.Equal(
            expected,
            prompt.Content);

        Assert.False(
            prompt.Content.EndsWith(
                "\n",
                StringComparison.Ordinal));

        Assert.Equal(
            DeterministicWorkEstimator.EstimateTokens(
                prompt.Content),
            prompt.EstimatedTokens);
    }

    [Fact]
    public void Compile_RejectsNullWork()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                PromptCompiler.Compile(
                    null!,
                    "task-a",
                    CreateEmptyContext()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Compile_RejectsInvalidTaskId(
        string? taskId_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                PromptCompiler.Compile(
                    CreateSimpleWork(),
                    taskId_!,
                    CreateEmptyContext()));
    }

    [Fact]
    public void Compile_RejectsNullContext()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                PromptCompiler.Compile(
                    CreateSimpleWork(),
                    "task-a",
                    null!));
    }

    [Fact]
    public void Compile_RejectsUnknownTaskUsingOrdinalIdentity()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    PromptCompiler.Compile(
                        CreateSimpleWork(),
                        "TASK-A",
                        CreateEmptyContext()));

        Assert.Equal(
            "taskId_",
            exception.ParamName);
    }

    [Fact]
    public void Compile_ValidationInputOrderDoesNotChangePrompt()
    {
        CompiledContext context =
            CreateRichContext();

        CompiledPrompt first =
            PromptCompiler.Compile(
                CreateRichWork(
                    reverseValidationOrder_: false),
                "task-b",
                context);

        CompiledPrompt second =
            PromptCompiler.Compile(
                CreateRichWork(
                    reverseValidationOrder_: true),
                "task-b",
                context);

        Assert.Equal(
            first.Content,
            second.Content);

        Assert.Equal(
            first.EstimatedTokens,
            second.EstimatedTokens);
    }

    [Fact]
    public void Compile_PreservesCompiledContextItemOrder()
    {
        CompiledContextItem firstItem =
            ContextItem(
                "ctx-z",
                "first");

        CompiledContextItem secondItem =
            ContextItem(
                "ctx-a",
                "second");

        CompiledContext context =
            new(
                100,
                10,
                checked(
                    firstItem.EstimatedTokens +
                    secondItem.EstimatedTokens),
                false,
                [
                    firstItem,
                    secondItem
                ],
                Array.Empty<string>());

        CompiledPrompt prompt =
            PromptCompiler.Compile(
                CreateSimpleWork(),
                "task-a",
                context);

        using JsonDocument document =
            JsonDocument.Parse(
                prompt.Content);

        JsonElement.ArrayEnumerator items =
            document
                .RootElement
                .GetProperty("context")
                .GetProperty("items")
                .EnumerateArray();

        Assert.True(
            items.MoveNext());

        Assert.Equal(
            "ctx-z",
            items.Current.GetProperty("id").GetString());

        Assert.True(
            items.MoveNext());

        Assert.Equal(
            "ctx-a",
            items.Current.GetProperty("id").GetString());

        Assert.False(
            items.MoveNext());
    }

    [Fact]
    public void Compile_MissingRequirementsProduceEmptyCapabilityArrays()
    {
        CompiledPrompt prompt =
            PromptCompiler.Compile(
                CreateSimpleWork(),
                "task-a",
                CreateEmptyContext());

        using JsonDocument document =
            JsonDocument.Parse(
                prompt.Content);

        JsonElement root =
            document.RootElement;

        Assert.Equal(
            0,
            root
                .GetProperty("modelRequiredCapabilities")
                .GetArrayLength());

        Assert.Equal(
            0,
            root
                .GetProperty("agentRequiredCapabilities")
                .GetArrayLength());
    }

    [Fact]
    public void Compile_IncludesOnlyTargetTaskValidationRequirements()
    {
        CompiledPrompt prompt =
            PromptCompiler.Compile(
                CreateRichWork(),
                "task-b",
                CreateRichContext());

        using JsonDocument document =
            JsonDocument.Parse(
                prompt.Content);

        JsonElement validations =
            document
                .RootElement
                .GetProperty("validationRequirements");

        Assert.Equal(
            2,
            validations.GetArrayLength());

        Assert.Equal(
            "val-a",
            validations[0].GetProperty("id").GetString());

        Assert.Equal(
            "Test",
            validations[0].GetProperty("strategy").GetString());

        Assert.Equal(
            "val-z",
            validations[1].GetProperty("id").GetString());

        Assert.Equal(
            "Policy",
            validations[1].GetProperty("strategy").GetString());
    }

    [Fact]
    public void Compile_ExcludesDependencyAndContextSelectionMetadata()
    {
        CompiledPrompt prompt =
            PromptCompiler.Compile(
                CreateRichWork(),
                "task-b",
                CreateRichContext());

        using JsonDocument document =
            JsonDocument.Parse(
                prompt.Content);

        JsonElement root =
            document.RootElement;

        Assert.False(
            root.TryGetProperty(
                "dependencies",
                out _));

        JsonElement context =
            root.GetProperty(
                "context");

        Assert.False(
            context.TryGetProperty(
                "omittedCandidateIds",
                out _));

        Assert.False(
            context.TryGetProperty(
                "tokenBudget",
                out _));

        Assert.False(
            context.TryGetProperty(
                "itemLimit",
                out _));

        Assert.False(
            context.TryGetProperty(
                "estimatedTokens",
                out _));

        JsonElement firstItem =
            context
                .GetProperty("items")[0];

        Assert.False(
            firstItem.TryGetProperty(
                "relevanceScore",
                out _));

        Assert.False(
            firstItem.TryGetProperty(
                "estimatedTokens",
                out _));
    }

    [Fact]
    public void Compile_ContainsNoConcreteExecutionSelectionFields()
    {
        CompiledPrompt prompt =
            PromptCompiler.Compile(
                CreateRichWork(),
                "task-b",
                CreateRichContext());

        foreach (string forbidden in new[]
        {
            "providerId",
            "modelId",
            "agentId",
            "permission",
            "environment",
            "sessionReference",
            "structuredOutput",
            "executionEnvelope",
            "scheduler",
            "routing"
        })
        {
            Assert.DoesNotContain(
                forbidden,
                prompt.Content,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Compile_UsesSystemTextJsonEscapingAndRoundTripsExactText()
    {
        string instruction =
            "Use <tag> & \"quotes\"\nnext";

        string contextContent =
            "alpha < beta & gamma";

        ExecutableWork work =
            new(
                1,
                [
                    new ExecutableTask(
                        "task-a",
                        "plan-step-a",
                        instruction)
                ]);

        CompiledContextItem item =
            ContextItem(
                "ctx",
                contextContent);

        CompiledContext context =
            new(
                100,
                10,
                item.EstimatedTokens,
                false,
                [item],
                Array.Empty<string>());

        CompiledPrompt prompt =
            PromptCompiler.Compile(
                work,
                "task-a",
                context);

        using JsonDocument document =
            JsonDocument.Parse(
                prompt.Content);

        Assert.Equal(
            instruction,
            document
                .RootElement
                .GetProperty("task")
                .GetProperty("instruction")
                .GetString());

        Assert.Equal(
            contextContent,
            document
                .RootElement
                .GetProperty("context")
                .GetProperty("items")[0]
                .GetProperty("content")
                .GetString());
    }

    [Fact]
    public void Compile_RepeatedCallsAreByteForByteDeterministic()
    {
        ExecutableWork work =
            CreateRichWork();

        CompiledContext context =
            CreateRichContext();

        CompiledPrompt first =
            PromptCompiler.Compile(
                work,
                "task-b",
                context);

        CompiledPrompt second =
            PromptCompiler.Compile(
                work,
                "task-b",
                context);

        Assert.Equal(
            first.TaskId,
            second.TaskId);

        Assert.Equal(
            first.Content,
            second.Content);

        Assert.Equal(
            first.EstimatedTokens,
            second.EstimatedTokens);
    }

    private static ExecutableWork CreateSimpleWork()
    {
        return new ExecutableWork(
            1,
            [
                new ExecutableTask(
                    "task-a",
                    "plan-step-a",
                    "Perform task A")
            ]);
    }

    private static ExecutableWork CreateRichWork(
        bool reverseValidationOrder_ = false)
    {
        ValidationRequirement valA =
            new(
                "val-a",
                "task-b",
                "ac-1",
                ValidationStrategy.Test,
                "Run tests");

        ValidationRequirement valZ =
            new(
                "val-z",
                "task-b",
                "ac-2",
                ValidationStrategy.Policy,
                "Check policy");

        ValidationRequirement unrelated =
            new(
                "val-0",
                "task-a",
                "ac-0",
                ValidationStrategy.Build,
                "Build unrelated task");

        ValidationRequirement[] validations =
            reverseValidationOrder_
                ? [
                    valA,
                    unrelated,
                    valZ
                ]
                : [
                    valZ,
                    unrelated,
                    valA
                ];

        return new ExecutableWork(
            1,
            [
                new ExecutableTask(
                    "task-a",
                    "plan-step-1",
                    "Perform prerequisite"),
                new ExecutableTask(
                    "task-b",
                    "plan-step-2",
                    "Perform target task")
            ],
            [
                new ExecutableTaskDependency(
                    "task-b",
                    "task-a")
            ],
            validations,
            [
                new ModelRequirement(
                    "task-a",
                    Capabilities(
                        "read")),
                new ModelRequirement(
                    "task-b",
                    Capabilities(
                        "write",
                        "structured-output"))
            ],
            [
                new AgentRequirement(
                    "task-b",
                    Capabilities(
                        "write",
                        "read"))
            ]);
    }

    private static CompiledContext CreateEmptyContext()
    {
        return new CompiledContext(
            100,
            10,
            0,
            false,
            Array.Empty<CompiledContextItem>(),
            Array.Empty<string>());
    }

    private static CompiledContext CreateRichContext()
    {
        CompiledContextItem first =
            ContextItem(
                "ctx-z",
                "context z");

        CompiledContextItem second =
            ContextItem(
                "ctx-a",
                "context a");

        return new CompiledContext(
            100,
            10,
            checked(
                first.EstimatedTokens +
                second.EstimatedTokens),
            true,
            [
                first,
                second
            ],
            [
                "ctx-omitted"
            ]);
    }

    private static CompiledContextItem ContextItem(
        string id_,
        string content_)
    {
        return new CompiledContextItem(
            id_,
            content_,
            1,
            DeterministicWorkEstimator.EstimateTokens(
                content_));
    }

    private static AgentCapabilitySet Capabilities(
        params string[] values_)
    {
        return new AgentCapabilitySet(
            values_.Select(
                value_ =>
                    new AgentCapability(
                        value_)));
    }
}
