namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Agents;
using AiRepoKit.Execution;
using Xunit;

public sealed class ExecutionEnvelopeTests
{
    private const long MaxTimeoutMilliseconds =
        4294967294L;

    [Fact]
    public void Create_PreservesExecutionInputsAndRequirementReferences()
    {
        AgentCapabilitySet modelCapabilities =
            Capabilities(
                "model.write",
                "model.structured-output");

        AgentCapabilitySet agentCapabilities =
            Capabilities(
                "agent.write",
                "agent.read");

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                Array.Empty<ValidationRequirement>(),
                [
                    new ModelRequirement(
                        "task-a",
                        Capabilities("model.other")),
                    new ModelRequirement(
                        "task-b",
                        modelCapabilities)
                ],
                [
                    new AgentRequirement(
                        "task-b",
                        agentCapabilities)
                ]);

        CompiledPrompt prompt =
            new(
                "task-b",
                "compiled prompt");

        ExecutionEnvironment environment =
            Environment();

        AgentSessionReference session =
            new(
                "session-1");

        StructuredOutputContract structuredOutput =
            new(
                """{"type":"object"}""");

        TimeSpan timeout =
            TimeSpan.FromSeconds(
                30);

        ExecutionEnvelope envelope =
            ExecutionEnvelope.Create(
                work,
                prompt,
                ExecutionPermission.WorkspaceWrite,
                environment,
                session,
                structuredOutput,
                timeout);

        Assert.Equal(
            "task-b",
            envelope.TaskId);

        Assert.Same(
            prompt,
            envelope.Prompt);

        Assert.Same(
            modelCapabilities,
            envelope.ModelRequiredCapabilities);

        Assert.Same(
            agentCapabilities,
            envelope.AgentRequiredCapabilities);

        Assert.Equal(
            ExecutionPermission.WorkspaceWrite,
            envelope.Permission);

        Assert.Same(
            environment,
            envelope.Environment);

        Assert.Same(
            session,
            envelope.SessionReference);

        Assert.Same(
            structuredOutput,
            envelope.StructuredOutput);

        Assert.Equal(
            timeout,
            envelope.Timeout);
    }

    [Fact]
    public void Create_MissingRequirementsUseEmptyCapabilitySet()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a")
                ]);

        ExecutionEnvelope envelope =
            ExecutionEnvelope.Create(
                work,
                new CompiledPrompt(
                    "task-a",
                    "prompt"),
                ExecutionPermission.ReadOnly,
                Environment(),
                null,
                null,
                null);

        Assert.Same(
            AgentCapabilitySet.Empty,
            envelope.ModelRequiredCapabilities);

        Assert.Same(
            AgentCapabilitySet.Empty,
            envelope.AgentRequiredCapabilities);

        Assert.Empty(
            envelope.ModelRequiredCapabilities);

        Assert.Empty(
            envelope.AgentRequiredCapabilities);
    }

    [Fact]
    public void Create_RequirementLookupIsTargetScopedAndOrdinal()
    {
        AgentCapabilitySet lowerModel =
            Capabilities(
                "model.lower");

        AgentCapabilitySet upperModel =
            Capabilities(
                "model.upper");

        AgentCapabilitySet lowerAgent =
            Capabilities(
                "agent.lower");

        AgentCapabilitySet upperAgent =
            Capabilities(
                "agent.upper");

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("TASK-A")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                Array.Empty<ValidationRequirement>(),
                [
                    new ModelRequirement(
                        "TASK-A",
                        upperModel),
                    new ModelRequirement(
                        "task-a",
                        lowerModel)
                ],
                [
                    new AgentRequirement(
                        "TASK-A",
                        upperAgent),
                    new AgentRequirement(
                        "task-a",
                        lowerAgent)
                ]);

        ExecutionEnvelope envelope =
            ExecutionEnvelope.Create(
                work,
                new CompiledPrompt(
                    "task-a",
                    "prompt"),
                ExecutionPermission.ReadOnly,
                Environment(),
                null,
                null,
                null);

        Assert.Same(
            lowerModel,
            envelope.ModelRequiredCapabilities);

        Assert.Same(
            lowerAgent,
            envelope.AgentRequiredCapabilities);
    }

    [Fact]
    public void Create_RejectsNullWork()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    ExecutionEnvelope.Create(
                        null!,
                        new CompiledPrompt(
                            "task-a",
                            "prompt"),
                        ExecutionPermission.ReadOnly,
                        Environment(),
                        null,
                        null,
                        null));

        Assert.Equal(
            "work_",
            exception.ParamName);
    }

    [Fact]
    public void Create_RejectsNullPrompt()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        null!,
                        ExecutionPermission.ReadOnly,
                        Environment(),
                        null,
                        null,
                        null));

        Assert.Equal(
            "prompt_",
            exception.ParamName);
    }

    [Fact]
    public void Create_RejectsNullEnvironment()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        new CompiledPrompt(
                            "task-a",
                            "prompt"),
                        ExecutionPermission.ReadOnly,
                        null!,
                        null,
                        null,
                        null));

        Assert.Equal(
            "environment_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void Create_RejectsUndefinedPermission(
        int permissionValue_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        new CompiledPrompt(
                            "task-a",
                            "prompt"),
                        (ExecutionPermission)permissionValue_,
                        Environment(),
                        null,
                        null,
                        null));

        Assert.Equal(
            "permission_",
            exception.ParamName);
    }

    [Fact]
    public void Create_RejectsUnknownTaskUsingOrdinalIdentity()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        new CompiledPrompt(
                            "TASK-A",
                            "prompt"),
                        ExecutionPermission.ReadOnly,
                        Environment(),
                        null,
                        null,
                        null));

        Assert.Equal(
            "prompt_",
            exception.ParamName);
    }

    [Fact]
    public void Create_AllowsNullTimeout()
    {
        ExecutionEnvelope envelope =
            ExecutionEnvelope.Create(
                Work("task-a"),
                new CompiledPrompt(
                    "task-a",
                    "prompt"),
                ExecutionPermission.ReadOnly,
                Environment(),
                null,
                null,
                null);

        Assert.Null(
            envelope.Timeout);
    }

    [Fact]
    public void Create_AllowsMaximumTimeout()
    {
        TimeSpan maximum =
            TimeSpan.FromTicks(
                checked(
                    MaxTimeoutMilliseconds *
                    TimeSpan.TicksPerMillisecond));

        ExecutionEnvelope envelope =
            ExecutionEnvelope.Create(
                Work("task-a"),
                new CompiledPrompt(
                    "task-a",
                    "prompt"),
                ExecutionPermission.ReadOnly,
                Environment(),
                null,
                null,
                maximum);

        Assert.Equal(
            maximum,
            envelope.Timeout);
    }

    [Fact]
    public void Create_RejectsZeroTimeout()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        new CompiledPrompt(
                            "task-a",
                            "prompt"),
                        ExecutionPermission.ReadOnly,
                        Environment(),
                        null,
                        null,
                        TimeSpan.Zero));

        Assert.Equal(
            "timeout_",
            exception.ParamName);
    }

    [Fact]
    public void Create_RejectsNegativeTimeout()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        new CompiledPrompt(
                            "task-a",
                            "prompt"),
                        ExecutionPermission.ReadOnly,
                        Environment(),
                        null,
                        null,
                        TimeSpan.FromTicks(-1)));

        Assert.Equal(
            "timeout_",
            exception.ParamName);
    }

    [Fact]
    public void Create_RejectsTimeoutAboveV5Maximum()
    {
        TimeSpan aboveMaximum =
            TimeSpan.FromTicks(
                checked(
                    (MaxTimeoutMilliseconds + 1L) *
                    TimeSpan.TicksPerMillisecond));

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    ExecutionEnvelope.Create(
                        Work("task-a"),
                        new CompiledPrompt(
                            "task-a",
                            "prompt"),
                        ExecutionPermission.ReadOnly,
                        Environment(),
                        null,
                        null,
                        aboveMaximum));

        Assert.Equal(
            "timeout_",
            exception.ParamName);
    }

    [Fact]
    public void Create_PreservesCapabilitySetOrdinalOrder()
    {
        AgentCapabilitySet modelCapabilities =
            Capabilities(
                "zeta",
                "alpha",
                "middle");

        AgentCapabilitySet agentCapabilities =
            Capabilities(
                "write",
                "read");

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                Array.Empty<ValidationRequirement>(),
                [
                    new ModelRequirement(
                        "task-a",
                        modelCapabilities)
                ],
                [
                    new AgentRequirement(
                        "task-a",
                        agentCapabilities)
                ]);

        ExecutionEnvelope envelope =
            ExecutionEnvelope.Create(
                work,
                new CompiledPrompt(
                    "task-a",
                    "prompt"),
                ExecutionPermission.ReadOnly,
                Environment(),
                null,
                null,
                null);

        Assert.Equal(
            [
                "alpha",
                "middle",
                "zeta"
            ],
            envelope
                .ModelRequiredCapabilities
                .Select(
                    capability_ =>
                        capability_.Value)
                .ToArray());

        Assert.Equal(
            [
                "read",
                "write"
            ],
            envelope
                .AgentRequiredCapabilities
                .Select(
                    capability_ =>
                        capability_.Value)
                .ToArray());
    }

    [Fact]
    public void Create_RepeatedCallsAreDeterministic()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a")
                ],
                Array.Empty<ExecutableTaskDependency>(),
                Array.Empty<ValidationRequirement>(),
                [
                    new ModelRequirement(
                        "task-a",
                        Capabilities(
                            "model.b",
                            "model.a"))
                ],
                [
                    new AgentRequirement(
                        "task-a",
                        Capabilities(
                            "agent.b",
                            "agent.a"))
                ]);

        CompiledPrompt prompt =
            new(
                "task-a",
                "prompt");

        ExecutionEnvironment environment =
            Environment();

        ExecutionEnvelope first =
            ExecutionEnvelope.Create(
                work,
                prompt,
                ExecutionPermission.WorkspaceWrite,
                environment,
                null,
                null,
                TimeSpan.FromSeconds(5));

        ExecutionEnvelope second =
            ExecutionEnvelope.Create(
                work,
                prompt,
                ExecutionPermission.WorkspaceWrite,
                environment,
                null,
                null,
                TimeSpan.FromSeconds(5));

        Assert.Equal(
            first,
            second);
    }

    private static ExecutableWork Work(
        string taskId_)
    {
        return new ExecutableWork(
            1,
            [
                Task(taskId_)
            ]);
    }

    private static ExecutableTask Task(
        string taskId_)
    {
        return new ExecutableTask(
            taskId_,
            $"source-{taskId_}",
            $"instruction-{taskId_}");
    }

    private static ExecutionEnvironment Environment()
    {
        return new ExecutionEnvironment(
            Path.GetFullPath("."));
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
