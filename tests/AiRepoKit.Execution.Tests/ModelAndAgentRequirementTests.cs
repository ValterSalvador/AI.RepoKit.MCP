namespace AiRepoKit.Execution.Tests;

using System.Reflection;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using Xunit;

public sealed class ModelAndAgentRequirementTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ModelRequirement_RejectsInvalidTaskId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ModelRequirement(
                        value_!,
                        AgentCapabilitySet.Empty));

        Assert.Equal(
            "taskId_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void AgentRequirement_RejectsInvalidTaskId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new AgentRequirement(
                        value_!,
                        AgentCapabilitySet.Empty));

        Assert.Equal(
            "taskId_",
            exception.ParamName);
    }

    [Fact]
    public void ModelRequirement_RejectsNullCapabilities()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new ModelRequirement(
                        "task-1",
                        null!));

        Assert.Equal(
            "requiredCapabilities_",
            exception.ParamName);
    }

    [Fact]
    public void AgentRequirement_RejectsNullCapabilities()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new AgentRequirement(
                        "task-1",
                        null!));

        Assert.Equal(
            "requiredCapabilities_",
            exception.ParamName);
    }

    [Fact]
    public void Requirements_PreserveTaskIdExactly()
    {
        string taskId =
            "  TASK-opaque  ";

        ModelRequirement model =
            new(
                taskId,
                AgentCapabilitySet.Empty);

        AgentRequirement agent =
            new(
                taskId,
                AgentCapabilitySet.Empty);

        Assert.Equal(
            taskId,
            model.TaskId);

        Assert.Equal(
            taskId,
            agent.TaskId);
    }

    [Fact]
    public void Requirements_RetainExactCapabilitySetReference()
    {
        AgentCapabilitySet capabilities =
            new(
                [
                    new AgentCapability("code.read"),
                    new AgentCapability("repo.write")
                ]);

        ModelRequirement model =
            new(
                "task-1",
                capabilities);

        AgentRequirement agent =
            new(
                "task-1",
                capabilities);

        Assert.Same(
            capabilities,
            model.RequiredCapabilities);

        Assert.Same(
            capabilities,
            agent.RequiredCapabilities);
    }

    [Fact]
    public void EmptyCapabilitySet_IsValidForBothRequirements()
    {
        ModelRequirement model =
            new(
                "task-1",
                AgentCapabilitySet.Empty);

        AgentRequirement agent =
            new(
                "task-1",
                AgentCapabilitySet.Empty);

        Assert.Empty(
            model.RequiredCapabilities);

        Assert.Empty(
            agent.RequiredCapabilities);
    }

    [Fact]
    public void RequirementRecordEquality_UsesCapabilitySetValueSemantics()
    {
        AgentCapabilitySet first =
            new(
                [
                    new AgentCapability("repo.write"),
                    new AgentCapability("code.read")
                ]);

        AgentCapabilitySet second =
            new(
                [
                    new AgentCapability("code.read"),
                    new AgentCapability("repo.write")
                ]);

        Assert.NotSame(
            first,
            second);

        Assert.Equal(
            first,
            second);

        Assert.Equal(
            new ModelRequirement("task-1", first),
            new ModelRequirement("task-1", second));

        Assert.Equal(
            new AgentRequirement("task-1", first),
            new AgentRequirement("task-1", second));
    }

    [Fact]
    public void ExecutableWork_HasNoFiveArgumentConstructor()
    {
        ConstructorInfo[] constructors =
            typeof(ExecutableWork)
                .GetConstructors();

        Assert.DoesNotContain(
            constructors,
            constructor_ =>
                constructor_.GetParameters().Length == 5);
    }

    [Fact]
    public void ExecutableWork_HasExactlyOneSixArgumentConstructor()
    {
        ConstructorInfo[] matches =
            typeof(ExecutableWork)
                .GetConstructors()
                .Where(
                    constructor_ =>
                        constructor_.GetParameters().Length == 6)
                .ToArray();

        Assert.Single(
            matches);

        ParameterInfo[] parameters =
            matches[0].GetParameters();

        Assert.Equal(
            typeof(IReadOnlyList<ModelRequirement>),
            parameters[4].ParameterType);

        Assert.Equal(
            typeof(IReadOnlyList<AgentRequirement>),
            parameters[5].ParameterType);
    }

    [Fact]
    public void ExistingConstructors_CreateEmptyModelAndAgentRequirements()
    {
        ExecutableTask[] tasks =
        [
            Task("task-1")
        ];

        ExecutableWork twoArgument =
            new(
                1,
                tasks);

        ExecutableWork threeArgument =
            new(
                1,
                tasks,
                Array.Empty<ExecutableTaskDependency>());

        ExecutableWork fourArgument =
            new(
                1,
                tasks,
                Array.Empty<ExecutableTaskDependency>(),
                Array.Empty<ValidationRequirement>());

        foreach (ExecutableWork work in new[]
        {
            twoArgument,
            threeArgument,
            fourArgument
        })
        {
            Assert.Empty(
                work.ModelRequirements);

            Assert.Empty(
                work.AgentRequirements);
        }
    }

    [Fact]
    public void SixArgumentConstructor_RejectsNullModelRequirementCollection()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        Array.Empty<ValidationRequirement>(),
                        null!,
                        Array.Empty<AgentRequirement>()));

        Assert.Equal(
            "modelRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void SixArgumentConstructor_RejectsNullAgentRequirementCollection()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        Array.Empty<ValidationRequirement>(),
                        Array.Empty<ModelRequirement>(),
                        null!));

        Assert.Equal(
            "agentRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void SixArgumentConstructor_RejectsNullModelRequirementElement()
    {
        ModelRequirement?[] requirements =
        [
            new ModelRequirement(
                "task-1",
                AgentCapabilitySet.Empty),
            null
        ];

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        Array.Empty<ValidationRequirement>(),
                        requirements!,
                        Array.Empty<AgentRequirement>()));

        Assert.Equal(
            "modelRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void SixArgumentConstructor_RejectsNullAgentRequirementElement()
    {
        AgentRequirement?[] requirements =
        [
            new AgentRequirement(
                "task-1",
                AgentCapabilitySet.Empty),
            null
        ];

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-1")],
                        Array.Empty<ExecutableTaskDependency>(),
                        Array.Empty<ValidationRequirement>(),
                        Array.Empty<ModelRequirement>(),
                        requirements!));

        Assert.Equal(
            "agentRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void Work_TakesDefensiveRequirementSnapshots()
    {
        ModelRequirement model =
            Model(
                "task-1",
                "model.capability");

        AgentRequirement agent =
            Agent(
                "task-1",
                "agent.capability");

        List<ModelRequirement> models =
        [
            model
        ];

        List<AgentRequirement> agents =
        [
            agent
        ];

        ExecutableWork work =
            Work(
                [Task("task-1")],
                models,
                agents);

        models.Clear();
        agents.Clear();

        Assert.Single(
            work.ModelRequirements);

        Assert.Single(
            work.AgentRequirements);

        Assert.Same(
            model,
            work.ModelRequirements[0]);

        Assert.Same(
            agent,
            work.AgentRequirements[0]);
    }

    [Fact]
    public void Work_ExposesReadOnlyRequirementCollections()
    {
        ExecutableWork work =
            Work(
                [Task("task-1")],
                [Model("task-1", "model.capability")],
                [Agent("task-1", "agent.capability")]);

        IList<ModelRequirement> models =
            Assert.IsAssignableFrom<IList<ModelRequirement>>(
                work.ModelRequirements);

        IList<AgentRequirement> agents =
            Assert.IsAssignableFrom<IList<AgentRequirement>>(
                work.AgentRequirements);

        Assert.True(
            models.IsReadOnly);

        Assert.True(
            agents.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                models.Clear());

        Assert.Throws<NotSupportedException>(
            () =>
                agents.Clear());
    }

    [Fact]
    public void Work_PreservesRequirementOrderExactly()
    {
        ModelRequirement model1 =
            Model(
                "task-1",
                "model.first");

        ModelRequirement model2 =
            Model(
                "task-2",
                "model.second");

        AgentRequirement agent1 =
            Agent(
                "task-1",
                "agent.first");

        AgentRequirement agent2 =
            Agent(
                "task-2",
                "agent.second");

        ExecutableWork work =
            Work(
                [
                    Task("task-1"),
                    Task("task-2")
                ],
                [
                    model2,
                    model1
                ],
                [
                    agent2,
                    agent1
                ]);

        Assert.Same(
            model2,
            work.ModelRequirements[0]);

        Assert.Same(
            model1,
            work.ModelRequirements[1]);

        Assert.Same(
            agent2,
            work.AgentRequirements[0]);

        Assert.Same(
            agent1,
            work.AgentRequirements[1]);
    }

    [Fact]
    public void Work_RejectsDuplicateModelRequirementTaskId()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    Work(
                        [Task("task-1")],
                        [
                            Model("task-1", "model.first"),
                            Model("task-1", "model.second")
                        ],
                        Array.Empty<AgentRequirement>()));

        Assert.Equal(
            "modelRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void Work_RejectsDuplicateAgentRequirementTaskId()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    Work(
                        [Task("task-1")],
                        Array.Empty<ModelRequirement>(),
                        [
                            Agent("task-1", "agent.first"),
                            Agent("task-1", "agent.second")
                        ]));

        Assert.Equal(
            "agentRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void RequirementTaskIdentity_IsOrdinalCaseSensitive()
    {
        ExecutableWork work =
            Work(
                [
                    Task("TASK-1"),
                    Task("task-1")
                ],
                [
                    Model(
                        "TASK-1",
                        "model.upper"),
                    Model(
                        "task-1",
                        "model.lower")
                ],
                [
                    Agent(
                        "TASK-1",
                        "agent.upper"),
                    Agent(
                        "task-1",
                        "agent.lower")
                ]);

        Assert.Equal(
            2,
            work.ModelRequirements.Count);

        Assert.Equal(
            2,
            work.AgentRequirements.Count);
    }

    [Fact]
    public void Work_RejectsDanglingModelRequirementTask()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    Work(
                        [Task("task-1")],
                        [
                            Model(
                                "missing-task",
                                "model.capability")
                        ],
                        Array.Empty<AgentRequirement>()));

        Assert.Equal(
            "modelRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void Work_RejectsDanglingAgentRequirementTask()
    {
        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    Work(
                        [Task("task-1")],
                        Array.Empty<ModelRequirement>(),
                        [
                            Agent(
                                "missing-task",
                                "agent.capability")
                        ]));

        Assert.Equal(
            "agentRequirements_",
            exception.ParamName);
    }

    [Fact]
    public void Requirements_DoNotResolveThroughSourcePlanStepId()
    {
        ExecutableTask task =
            new(
                "task-1",
                "source-step-1",
                "instruction");

        Assert.Throws<ArgumentException>(
            () =>
                Work(
                    [task],
                    [
                        Model(
                            "source-step-1",
                            "model.capability")
                    ],
                    Array.Empty<AgentRequirement>()));

        Assert.Throws<ArgumentException>(
            () =>
                Work(
                    [task],
                    Array.Empty<ModelRequirement>(),
                    [
                        Agent(
                            "source-step-1",
                            "agent.capability")
                    ]));
    }

    [Fact]
    public void SameTaskMayHaveBothModelAndAgentRequirement()
    {
        ExecutableWork work =
            Work(
                [Task("task-1")],
                [
                    Model(
                        "task-1",
                        "model.capability")
                ],
                [
                    Agent(
                        "task-1",
                        "agent.capability")
                ]);

        Assert.Single(
            work.ModelRequirements);

        Assert.Single(
            work.AgentRequirements);
    }

    [Fact]
    public void TaskMayHaveNeitherRequirement()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-1"),
                    Task("task-2")
                ],
                [
                    Model(
                        "task-1",
                        "model.capability")
                ],
                Array.Empty<AgentRequirement>());

        Assert.Equal(
            2,
            work.Tasks.Count);

        Assert.DoesNotContain(
            work.ModelRequirements,
            requirement_ =>
                requirement_.TaskId == "task-2");

        Assert.DoesNotContain(
            work.AgentRequirements,
            requirement_ =>
                requirement_.TaskId == "task-2");
    }

    [Fact]
    public void Requirements_DoNotAlterExistingCollections()
    {
        ExecutableTask task1 =
            Task("task-1");

        ExecutableTask task2 =
            Task("task-2");

        ExecutableTaskDependency dependency =
            new(
                "task-2",
                "task-1");

        ValidationRequirement validation =
            new(
                "validation-1",
                "task-2",
                "criterion-1",
                ValidationStrategy.Test,
                "statement");

        ExecutableWork work =
            new(
                1,
                [
                    task2,
                    task1
                ],
                [
                    dependency
                ],
                [
                    validation
                ],
                [
                    Model(
                        "task-2",
                        "model.capability")
                ],
                [
                    Agent(
                        "task-2",
                        "agent.capability")
                ]);

        Assert.Same(
            task2,
            work.Tasks[0]);

        Assert.Same(
            task1,
            work.Tasks[1]);

        Assert.Same(
            dependency,
            work.Dependencies[0]);

        Assert.Same(
            validation,
            work.ValidationRequirements[0]);
    }

    [Fact]
    public void EqualityAndHash_IncludeModelAndAgentRequirements()
    {
        ExecutableWork left =
            Work(
                [Task("task-1")],
                [
                    Model(
                        "task-1",
                        "model.capability")
                ],
                [
                    Agent(
                        "task-1",
                        "agent.capability")
                ]);

        ExecutableWork equal =
            Work(
                [Task("task-1")],
                [
                    Model(
                        "task-1",
                        "model.capability")
                ],
                [
                    Agent(
                        "task-1",
                        "agent.capability")
                ]);

        ExecutableWork differentModel =
            Work(
                [Task("task-1")],
                [
                    Model(
                        "task-1",
                        "model.other")
                ],
                [
                    Agent(
                        "task-1",
                        "agent.capability")
                ]);

        ExecutableWork differentAgent =
            Work(
                [Task("task-1")],
                [
                    Model(
                        "task-1",
                        "model.capability")
                ],
                [
                    Agent(
                        "task-1",
                        "agent.other")
                ]);

        Assert.Equal(
            left,
            equal);

        Assert.Equal(
            left.GetHashCode(),
            equal.GetHashCode());

        Assert.NotEqual(
            left,
            differentModel);

        Assert.NotEqual(
            left,
            differentAgent);
    }

    [Fact]
    public void Equality_PreservesModelAndAgentRequirementOrder()
    {
        ExecutableTask[] tasks =
        [
            Task("task-1"),
            Task("task-2")
        ];

        ModelRequirement model1 =
            Model(
                "task-1",
                "model.first");

        ModelRequirement model2 =
            Model(
                "task-2",
                "model.second");

        AgentRequirement agent1 =
            Agent(
                "task-1",
                "agent.first");

        AgentRequirement agent2 =
            Agent(
                "task-2",
                "agent.second");

        ExecutableWork left =
            Work(
                tasks,
                [
                    model1,
                    model2
                ],
                [
                    agent1,
                    agent2
                ]);

        ExecutableWork modelReordered =
            Work(
                tasks,
                [
                    model2,
                    model1
                ],
                [
                    agent1,
                    agent2
                ]);

        ExecutableWork agentReordered =
            Work(
                tasks,
                [
                    model1,
                    model2
                ],
                [
                    agent2,
                    agent1
                ]);

        Assert.NotEqual(
            left,
            modelReordered);

        Assert.NotEqual(
            left,
            agentReordered);
    }

    private static ExecutableTask Task(
        string id_)
    {
        return new ExecutableTask(
            id_,
            $"source-{id_}",
            $"instruction-{id_}");
    }

    private static ModelRequirement Model(
        string taskId_,
        string capability_)
    {
        return new ModelRequirement(
            taskId_,
            new AgentCapabilitySet(
                [
                    new AgentCapability(capability_)
                ]));
    }

    private static AgentRequirement Agent(
        string taskId_,
        string capability_)
    {
        return new AgentRequirement(
            taskId_,
            new AgentCapabilitySet(
                [
                    new AgentCapability(capability_)
                ]));
    }

    private static ExecutableWork Work(
        IReadOnlyList<ExecutableTask> tasks_,
        IReadOnlyList<ModelRequirement> modelRequirements_,
        IReadOnlyList<AgentRequirement> agentRequirements_)
    {
        return new ExecutableWork(
            1,
            tasks_,
            Array.Empty<ExecutableTaskDependency>(),
            Array.Empty<ValidationRequirement>(),
            modelRequirements_,
            agentRequirements_);
    }
}