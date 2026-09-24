namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Agents;
using AiRepoKit.Execution;
using Xunit;

public sealed class DeterministicWorkEstimatorTests
{
    [Fact]
    public void EstimateTokens_RejectsNull()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    DeterministicWorkEstimator.EstimateTokens(
                        null!));

        Assert.Equal(
            "text_",
            exception.ParamName);
    }

    [Fact]
    public void EstimateTokens_EmptyStringReturnsZero()
    {
        Assert.Equal(
            0,
            DeterministicWorkEstimator.EstimateTokens(
                string.Empty));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void EstimateTokens_LengthsOneThroughFourReturnOne(
        string text_)
    {
        Assert.Equal(
            1,
            DeterministicWorkEstimator.EstimateTokens(
                text_));
    }

    [Fact]
    public void EstimateTokens_LengthFiveReturnsTwo()
    {
        Assert.Equal(
            2,
            DeterministicWorkEstimator.EstimateTokens(
                "abcde"));
    }

    [Fact]
    public void EstimateTokens_UsesUtf16CodeUnits()
    {
        string threeSupplementaryCharacters =
            "\U0001F600\U0001F600\U0001F600";

        Assert.Equal(
            6,
            threeSupplementaryCharacters.Length);

        Assert.Equal(
            2,
            DeterministicWorkEstimator.EstimateTokens(
                threeSupplementaryCharacters));
    }

    [Fact]
    public void EstimateTokens_DoesNotTrimInput()
    {
        Assert.Equal(
            1,
            DeterministicWorkEstimator.EstimateTokens(
                "    "));

        Assert.Equal(
            2,
            DeterministicWorkEstimator.EstimateTokens(
                "     "));
    }

    [Fact]
    public void EstimateTokens_DoesNotNormalizeUnicode()
    {
        string composed =
            "\u00E9\u00E9\u00E9\u00E9\u00E9";

        string decomposed =
            "e\u0301e\u0301e\u0301e\u0301e\u0301";

        Assert.Equal(
            5,
            composed.Length);

        Assert.Equal(
            10,
            decomposed.Length);

        Assert.Equal(
            2,
            DeterministicWorkEstimator.EstimateTokens(
                composed));

        Assert.Equal(
            3,
            DeterministicWorkEstimator.EstimateTokens(
                decomposed));
    }

    [Fact]
    public void EstimateTokens_IsDeterministic()
    {
        string text =
            "deterministic input";

        int first =
            DeterministicWorkEstimator.EstimateTokens(
                text);

        int second =
            DeterministicWorkEstimator.EstimateTokens(
                text);

        Assert.Equal(
            first,
            second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ExecutableTaskEstimate_RejectsInvalidTaskId(
        string? taskId_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ExecutableTaskEstimate(
                        taskId_!,
                        1,
                        1));

        Assert.Equal(
            "taskId_",
            exception.ParamName);
    }

    [Fact]
    public void ExecutableTaskEstimate_PreservesTaskIdExactly()
    {
        string taskId =
            "  Task-Identity  ";

        ExecutableTaskEstimate estimate =
            new(
                taskId,
                2,
                1);

        Assert.Equal(
            taskId,
            estimate.TaskId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ExecutableTaskEstimate_RejectsNonPositiveComplexity(
        int complexity_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new ExecutableTaskEstimate(
                        "task-1",
                        complexity_,
                        1));

        Assert.Equal(
            "complexityScore_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ExecutableTaskEstimate_RejectsNonPositiveEstimatedTokens(
        int estimatedTokens_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new ExecutableTaskEstimate(
                        "task-1",
                        1,
                        estimatedTokens_));

        Assert.Equal(
            "estimatedInstructionTokens_",
            exception.ParamName);
    }

    [Fact]
    public void ExecutableTaskEstimate_UsesDeterministicValueEquality()
    {
        ExecutableTaskEstimate first =
            new(
                "task-1",
                5,
                2);

        ExecutableTaskEstimate equal =
            new(
                "task-1",
                5,
                2);

        ExecutableTaskEstimate different =
            new(
                "task-1",
                6,
                2);

        Assert.Equal(
            first,
            equal);

        Assert.Equal(
            first.GetHashCode(),
            equal.GetHashCode());

        Assert.NotEqual(
            first,
            different);
    }

    [Fact]
    public void Estimate_RejectsNullWork()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    DeterministicWorkEstimator.Estimate(
                        null!));

        Assert.Equal(
            "work_",
            exception.ParamName);
    }

    [Fact]
    public void Estimate_EmptyWorkReturnsEmptyReadOnlyCollection()
    {
        ExecutableWork work =
            Work(
                Array.Empty<ExecutableTask>());

        IReadOnlyList<ExecutableTaskEstimate> estimates =
            DeterministicWorkEstimator.Estimate(
                work);

        Assert.Empty(
            estimates);

        Assert.IsAssignableFrom<IList<ExecutableTaskEstimate>>(
            estimates);

        IList<ExecutableTaskEstimate> mutable =
            (IList<ExecutableTaskEstimate>) estimates;

        Assert.True(
            mutable.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                mutable.Add(
                    new ExecutableTaskEstimate(
                        "task-1",
                        1,
                        1)));
    }

    [Fact]
    public void Estimate_ReturnsOneResultPerTaskInExactTaskOrder()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-c"),
                    Task("task-a"),
                    Task("task-b")
                ]);

        IReadOnlyList<ExecutableTaskEstimate> estimates =
            DeterministicWorkEstimator.Estimate(
                work);

        Assert.Equal(
            3,
            estimates.Count);

        Assert.Equal(
            "task-c",
            estimates[0].TaskId);

        Assert.Equal(
            "task-a",
            estimates[1].TaskId);

        Assert.Equal(
            "task-b",
            estimates[2].TaskId);
    }

    [Fact]
    public void Estimate_ResultCollectionIsReadOnly()
    {
        IReadOnlyList<ExecutableTaskEstimate> estimates =
            DeterministicWorkEstimator.Estimate(
                Work(
                    [
                        Task("task-1")
                    ]));

        Assert.IsAssignableFrom<IList<ExecutableTaskEstimate>>(
            estimates);

        IList<ExecutableTaskEstimate> mutable =
            (IList<ExecutableTaskEstimate>) estimates;

        Assert.True(
            mutable.IsReadOnly);

        Assert.Throws<NotSupportedException>(
            () =>
                mutable[0] =
                    new ExecutableTaskEstimate(
                        "replacement",
                        1,
                        1));

        Assert.Throws<NotSupportedException>(
            () =>
                mutable.Clear());
    }

    [Fact]
    public void Estimate_UsesExactInstructionTokenEstimate()
    {
        ExecutableWork work =
            Work(
                [
                    Task(
                        "task-1",
                        "abcde")
                ]);

        ExecutableTaskEstimate estimate =
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    work));

        Assert.Equal(
            2,
            estimate.EstimatedInstructionTokens);

        Assert.Equal(
            2,
            estimate.ComplexityScore);
    }

    [Fact]
    public void Complexity_MinimumSimpleTaskScoreIsOne()
    {
        ExecutableTaskEstimate estimate =
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    Work(
                        [
                            Task(
                                "task-1",
                                "a")
                        ])));

        Assert.Equal(
            1,
            estimate.EstimatedInstructionTokens);

        Assert.Equal(
            1,
            estimate.ComplexityScore);
    }

    [Fact]
    public void Complexity_DirectPrerequisiteContributesOne()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a")
                ]);

        IReadOnlyList<ExecutableTaskEstimate> estimates =
            DeterministicWorkEstimator.Estimate(
                work);

        Assert.Equal(
            1,
            estimates[0].ComplexityScore);

        Assert.Equal(
            2,
            estimates[1].ComplexityScore);
    }

    [Fact]
    public void Complexity_OutgoingDependentDoesNotContribute()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a")
                ]);

        ExecutableTaskEstimate taskA =
            DeterministicWorkEstimator.Estimate(
                work)[0];

        Assert.Equal(
            "task-a",
            taskA.TaskId);

        Assert.Equal(
            1,
            taskA.ComplexityScore);
    }

    [Fact]
    public void Complexity_TransitivePrerequisitesDoNotContributeBeyondDirectEdges()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ]);

        ExecutableTaskEstimate taskC =
            DeterministicWorkEstimator.Estimate(
                work)[2];

        Assert.Equal(
            2,
            taskC.ComplexityScore);
    }

    [Fact]
    public void Complexity_EachValidationRequirementContributesOne()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-1")
                ],
                validationRequirements_:
                [
                    Validation(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Build,
                        "short"),
                    Validation(
                        "validation-2",
                        "task-1",
                        ValidationStrategy.Test,
                        "longer statement")
                ]);

        ExecutableTaskEstimate estimate =
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    work));

        Assert.Equal(
            3,
            estimate.ComplexityScore);
    }

    [Fact]
    public void Complexity_ValidationStrategyDoesNotAlterWeight()
    {
        ExecutableWork buildWork =
            Work(
                [
                    Task("task-1")
                ],
                validationRequirements_:
                [
                    Validation(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Build,
                        "statement")
                ]);

        ExecutableWork policyWork =
            Work(
                [
                    Task("task-1")
                ],
                validationRequirements_:
                [
                    Validation(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Policy,
                        "statement")
                ]);

        Assert.Equal(
            DeterministicWorkEstimator.Estimate(
                buildWork)[0].ComplexityScore,
            DeterministicWorkEstimator.Estimate(
                policyWork)[0].ComplexityScore);
    }

    [Fact]
    public void Complexity_ValidationStatementLengthDoesNotAlterWeight()
    {
        ExecutableWork shortWork =
            Work(
                [
                    Task("task-1")
                ],
                validationRequirements_:
                [
                    Validation(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Test,
                        "x")
                ]);

        ExecutableWork longWork =
            Work(
                [
                    Task("task-1")
                ],
                validationRequirements_:
                [
                    Validation(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Test,
                        new string(
                            'x',
                            1000))
                ]);

        Assert.Equal(
            DeterministicWorkEstimator.Estimate(
                shortWork)[0].ComplexityScore,
            DeterministicWorkEstimator.Estimate(
                longWork)[0].ComplexityScore);
    }

    [Fact]
    public void Complexity_ModelCapabilityCountContributesExactlyCount()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-1")
                ],
                modelRequirements_:
                [
                    Model(
                        "task-1",
                        "code.read",
                        "repo.write")
                ]);

        ExecutableTaskEstimate estimate =
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    work));

        Assert.Equal(
            3,
            estimate.ComplexityScore);
    }

    [Fact]
    public void Complexity_EmptyModelCapabilitySetContributesZero()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-1")
                ],
                modelRequirements_:
                [
                    new ModelRequirement(
                        "task-1",
                        AgentCapabilitySet.Empty)
                ]);

        Assert.Equal(
            1,
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    work)).ComplexityScore);
    }

    [Fact]
    public void Complexity_AgentCapabilityCountContributesExactlyCount()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-1")
                ],
                agentRequirements_:
                [
                    Agent(
                        "task-1",
                        "code.read",
                        "repo.write")
                ]);

        ExecutableTaskEstimate estimate =
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    work));

        Assert.Equal(
            3,
            estimate.ComplexityScore);
    }

    [Fact]
    public void Complexity_EmptyAgentCapabilitySetContributesZero()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-1")
                ],
                agentRequirements_:
                [
                    new AgentRequirement(
                        "task-1",
                        AgentCapabilitySet.Empty)
                ]);

        Assert.Equal(
            1,
            Assert.Single(
                DeterministicWorkEstimator.Estimate(
                    work)).ComplexityScore);
    }

    [Fact]
    public void Complexity_CapabilityValueLengthDoesNotAlterWeight()
    {
        ExecutableWork shortCapability =
            Work(
                [
                    Task("task-1")
                ],
                modelRequirements_:
                [
                    Model(
                        "task-1",
                        "a")
                ]);

        ExecutableWork longCapability =
            Work(
                [
                    Task("task-1")
                ],
                modelRequirements_:
                [
                    Model(
                        "task-1",
                        "very.long-capability")
                ]);

        Assert.Equal(
            DeterministicWorkEstimator.Estimate(
                shortCapability)[0].ComplexityScore,
            DeterministicWorkEstimator.Estimate(
                longCapability)[0].ComplexityScore);
    }

    [Fact]
    public void Estimate_UsesOrdinalCaseSensitiveTaskIdentity()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-a"),
                    Task("TASK-A")
                ],
                modelRequirements_:
                [
                    Model(
                        "TASK-A",
                        "code.read")
                ]);

        IReadOnlyList<ExecutableTaskEstimate> estimates =
            DeterministicWorkEstimator.Estimate(
                work);

        Assert.Equal(
            1,
            estimates[0].ComplexityScore);

        Assert.Equal(
            2,
            estimates[1].ComplexityScore);
    }

    [Fact]
    public void Estimate_CollectionOrderingDoesNotAlterScores()
    {
        ExecutableTask[] tasks =
        [
            Task("task-a"),
            Task("task-b"),
            Task("task-c")
        ];

        ExecutableWork first =
            Work(
                tasks,
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ],
                [
                    Validation(
                        "validation-c",
                        "task-c",
                        ValidationStrategy.Build,
                        "c"),
                    Validation(
                        "validation-a",
                        "task-a",
                        ValidationStrategy.Test,
                        "a")
                ],
                [
                    Model(
                        "task-c",
                        "code.read"),
                    Model(
                        "task-a",
                        "repo.write")
                ],
                [
                    Agent(
                        "task-c",
                        "repo.write"),
                    Agent(
                        "task-b",
                        "code.read")
                ]);

        ExecutableWork reordered =
            Work(
                tasks,
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a")
                ],
                [
                    Validation(
                        "validation-a",
                        "task-a",
                        ValidationStrategy.Test,
                        "a"),
                    Validation(
                        "validation-c",
                        "task-c",
                        ValidationStrategy.Build,
                        "c")
                ],
                [
                    Model(
                        "task-a",
                        "repo.write"),
                    Model(
                        "task-c",
                        "code.read")
                ],
                [
                    Agent(
                        "task-b",
                        "code.read"),
                    Agent(
                        "task-c",
                        "repo.write")
                ]);

        Assert.Equal(
            DeterministicWorkEstimator.Estimate(
                first).ToArray(),
            DeterministicWorkEstimator.Estimate(
                reordered).ToArray());
    }

    [Fact]
    public void Estimate_RepeatedCallsProduceEqualValues()
    {
        ExecutableWork work =
            Work(
                [
                    Task(
                        "task-1",
                        "abcde")
                ],
                validationRequirements_:
                [
                    Validation(
                        "validation-1",
                        "task-1",
                        ValidationStrategy.Build,
                        "statement")
                ],
                modelRequirements_:
                [
                    Model(
                        "task-1",
                        "code.read")
                ],
                agentRequirements_:
                [
                    Agent(
                        "task-1",
                        "repo.write")
                ]);

        ExecutableTaskEstimate[] first =
            DeterministicWorkEstimator.Estimate(
                work).ToArray();

        ExecutableTaskEstimate[] second =
            DeterministicWorkEstimator.Estimate(
                work).ToArray();

        Assert.Equal(
            first,
            second);
    }

    [Fact]
    public void Estimate_DoesNotMutateExecutableWorkOrEqualityHash()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a")
                ],
                [
                    Validation(
                        "validation-1",
                        "task-b",
                        ValidationStrategy.Test,
                        "statement")
                ],
                [
                    Model(
                        "task-b",
                        "code.read")
                ],
                [
                    Agent(
                        "task-b",
                        "repo.write")
                ]);

        ExecutableWork equal =
            Work(
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a")
                ],
                [
                    Validation(
                        "validation-1",
                        "task-b",
                        ValidationStrategy.Test,
                        "statement")
                ],
                [
                    Model(
                        "task-b",
                        "code.read")
                ],
                [
                    Agent(
                        "task-b",
                        "repo.write")
                ]);

        int hashBefore =
            work.GetHashCode();

        _ =
            DeterministicWorkEstimator.Estimate(
                work);

        Assert.Equal(
            equal,
            work);

        Assert.Equal(
            hashBefore,
            work.GetHashCode());

        Assert.Equal(
            equal.GetHashCode(),
            work.GetHashCode());
    }

    [Fact]
    public void EstimatedInstructionTokens_ExcludeOtherWorkMetadata()
    {
        ExecutableWork work =
            Work(
                [
                    Task("task-a"),
                    Task(
                        "task-b",
                        "a")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a")
                ],
                [
                    Validation(
                        "validation-1",
                        "task-b",
                        ValidationStrategy.Policy,
                        new string(
                            'x',
                            1000))
                ],
                [
                    Model(
                        "task-b",
                        "code.read",
                        "repo.write")
                ],
                [
                    Agent(
                        "task-b",
                        "code.read")
                ]);

        ExecutableTaskEstimate taskB =
            DeterministicWorkEstimator.Estimate(
                work)[1];

        Assert.Equal(
            1,
            taskB.EstimatedInstructionTokens);

        Assert.Equal(
            6,
            taskB.ComplexityScore);
    }

    private static ExecutableTask Task(
        string id_,
        string instruction_ = "a")
    {
        return new ExecutableTask(
            id_,
            $"source-{id_}",
            instruction_);
    }

    private static ValidationRequirement Validation(
        string id_,
        string taskId_,
        ValidationStrategy strategy_,
        string statement_)
    {
        return new ValidationRequirement(
            id_,
            taskId_,
            $"criterion-{id_}",
            strategy_,
            statement_);
    }

    private static ModelRequirement Model(
        string taskId_,
        params string[] capabilities_)
    {
        return new ModelRequirement(
            taskId_,
            new AgentCapabilitySet(
                capabilities_
                    .Select(
                        capability_ =>
                            new AgentCapability(
                                capability_))));
    }

    private static AgentRequirement Agent(
        string taskId_,
        params string[] capabilities_)
    {
        return new AgentRequirement(
            taskId_,
            new AgentCapabilitySet(
                capabilities_
                    .Select(
                        capability_ =>
                            new AgentCapability(
                                capability_))));
    }

    private static ExecutableWork Work(
        IReadOnlyList<ExecutableTask> tasks_,
        IReadOnlyList<ExecutableTaskDependency>? dependencies_ = null,
        IReadOnlyList<ValidationRequirement>? validationRequirements_ = null,
        IReadOnlyList<ModelRequirement>? modelRequirements_ = null,
        IReadOnlyList<AgentRequirement>? agentRequirements_ = null)
    {
        return new ExecutableWork(
            1,
            tasks_,
            dependencies_ ??
                Array.Empty<ExecutableTaskDependency>(),
            validationRequirements_ ??
                Array.Empty<ValidationRequirement>(),
            modelRequirements_ ??
                Array.Empty<ModelRequirement>(),
            agentRequirements_ ??
                Array.Empty<AgentRequirement>());
    }
}
