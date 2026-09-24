namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Execution;
using Xunit;

public sealed class ExecutableWorkDependencyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Dependency_RejectsInvalidTaskId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ExecutableTaskDependency(
                        value_!,
                        "task-b"));

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
    public void Dependency_RejectsInvalidDependsOnTaskId(
        string? value_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ExecutableTaskDependency(
                        "task-b",
                        value_!));

        Assert.Equal(
            "dependsOnTaskId_",
            exception.ParamName);
    }

    [Fact]
    public void Dependency_PreservesIdentifiersExactly()
    {
        ExecutableTaskDependency dependency =
            new(
                "  TASK-B  ",
                "  task-A  ");

        Assert.Equal(
            "  TASK-B  ",
            dependency.TaskId);

        Assert.Equal(
            "  task-A  ",
            dependency.DependsOnTaskId);
    }

    [Fact]
    public void Dependency_RejectsDirectSelfDependency()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableTaskDependency(
                    "task-a",
                    "task-a"));
    }

    [Fact]
    public void Dependency_UsesOrdinalCaseSensitiveIdentity()
    {
        ExecutableTaskDependency dependency =
            new(
                "TASK-A",
                "task-a");

        Assert.Equal(
            "TASK-A",
            dependency.TaskId);
    }

    [Fact]
    public void TwoArgumentConstructor_ProducesEmptyDependencies()
    {
        ExecutableWork work =
            new(
                1,
                [Task("task-a")]);

        Assert.Empty(
            work.Dependencies);
    }

    [Fact]
    public void Constructor_AcceptsZeroTasksAndZeroDependencies()
    {
        ExecutableWork work =
            new(
                1,
                Array.Empty<ExecutableTask>(),
                Array.Empty<ExecutableTaskDependency>());

        Assert.Empty(
            work.Tasks);

        Assert.Empty(
            work.Dependencies);
    }

    [Fact]
    public void Constructor_RejectsNullDependencyCollection()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new ExecutableWork(
                        1,
                        [Task("task-a")],
                        null!));

        Assert.Equal(
            "dependencies_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullDependencyElement()
    {
        ExecutableTaskDependency?[] dependencies =
        [
            new ExecutableTaskDependency(
                "task-b",
                "task-a"),
            null
        ];

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(
                        1,
                        [
                            Task("task-a"),
                            Task("task-b")
                        ],
                        dependencies!));

        Assert.Equal(
            "dependencies_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_TakesDefensiveDependencySnapshot()
    {
        ExecutableTaskDependency original =
            new(
                "task-b",
                "task-a");

        List<ExecutableTaskDependency> dependencies =
        [
            original
        ];

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                dependencies);

        dependencies.Clear();

        Assert.Single(
            work.Dependencies);

        Assert.Same(
            original,
            work.Dependencies[0]);
    }

    [Fact]
    public void ExposedDependenciesCollection_IsReadOnly()
    {
        ExecutableTaskDependency dependency =
            new(
                "task-b",
                "task-a");

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                [dependency]);

        if (work.Dependencies is IList<ExecutableTaskDependency> list)
        {
            Assert.True(
                list.IsReadOnly);

            Assert.Throws<NotSupportedException>(
                () =>
                    list.Clear());
        }
    }

    [Fact]
    public void Constructor_PreservesDependencyOrderExactly()
    {
        ExecutableTaskDependency first =
            new(
                "task-c",
                "task-a");

        ExecutableTaskDependency second =
            new(
                "task-c",
                "task-b");

        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    second,
                    first
                ]);

        Assert.Same(
            second,
            work.Dependencies[0]);

        Assert.Same(
            first,
            work.Dependencies[1]);
    }

    [Fact]
    public void Constructor_RejectsUnknownTaskIdEndpoint()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [
                        Task("task-a"),
                        Task("task-b")
                    ],
                    [
                        new ExecutableTaskDependency(
                            "missing",
                            "task-a")
                    ]));
    }

    [Fact]
    public void Constructor_RejectsUnknownDependsOnTaskIdEndpoint()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [
                        Task("task-a"),
                        Task("task-b")
                    ],
                    [
                        new ExecutableTaskDependency(
                            "task-b",
                            "missing")
                    ]));
    }

    [Fact]
    public void Constructor_DoesNotResolveSourcePlanStepIdAsTaskEndpoint()
    {
        ExecutableTask taskA =
            new(
                "task-a",
                "source-step-a",
                "instruction");

        ExecutableTask taskB =
            new(
                "task-b",
                "source-step-b",
                "instruction");

        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [
                        taskA,
                        taskB
                    ],
                    [
                        new ExecutableTaskDependency(
                            "task-b",
                            "source-step-a")
                    ]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateDependencyEdge()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [
                        Task("task-a"),
                        Task("task-b")
                    ],
                    [
                        new ExecutableTaskDependency(
                            "task-b",
                            "task-a"),
                        new ExecutableTaskDependency(
                            "task-b",
                            "task-a")
                    ]));
    }

    [Fact]
    public void Constructor_UsesOrdinalCaseSensitiveEdgeSemantics()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("TASK-A"),
                    Task("task-b")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-b",
                        "TASK-A")
                ]);

        Assert.Equal(
            2,
            work.Dependencies.Count);
    }

    [Fact]
    public void Constructor_AcceptsForwardReference()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-a",
                        "task-b")
                ]);

        Assert.Single(
            work.Dependencies);
    }

    [Fact]
    public void Constructor_AcceptsMultiplePrerequisites()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ]);

        Assert.Equal(
            2,
            work.Dependencies.Count);
    }

    [Fact]
    public void Constructor_AcceptsSharedPrerequisite()
    {
        ExecutableWork work =
            new(
                1,
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
                        "task-a")
                ]);

        Assert.Equal(
            2,
            work.Dependencies.Count);
    }

    [Fact]
    public void Constructor_AcceptsDisconnectedDag()
    {
        ExecutableWork work =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c"),
                    Task("task-d")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-b",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-d",
                        "task-c")
                ]);

        Assert.Equal(
            2,
            work.Dependencies.Count);
    }

    [Fact]
    public void Constructor_AcceptsExplicitTransitiveEdge()
    {
        ExecutableWork work =
            new(
                1,
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
                        "task-b"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a")
                ]);

        Assert.Equal(
            3,
            work.Dependencies.Count);
    }

    [Fact]
    public void Constructor_RejectsTwoTaskCycle()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [
                        Task("task-a"),
                        Task("task-b")
                    ],
                    [
                        new ExecutableTaskDependency(
                            "task-a",
                            "task-b"),
                        new ExecutableTaskDependency(
                            "task-b",
                            "task-a")
                    ]));
    }

    [Fact]
    public void Constructor_RejectsMultiTaskCycle()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
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
                            "task-b"),
                        new ExecutableTaskDependency(
                            "task-a",
                            "task-c")
                    ]));
    }

    [Fact]
    public void Constructor_RejectsCycleInsideDisconnectedGraph()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new ExecutableWork(
                    1,
                    [
                        Task("task-a"),
                        Task("task-b"),
                        Task("task-c"),
                        Task("task-d"),
                        Task("task-e")
                    ],
                    [
                        new ExecutableTaskDependency(
                            "task-b",
                            "task-a"),
                        new ExecutableTaskDependency(
                            "task-d",
                            "task-e"),
                        new ExecutableTaskDependency(
                            "task-e",
                            "task-d")
                    ]));
    }

    [Fact]
    public void Dependencies_DoNotReorderTasks()
    {
        ExecutableTask taskA =
            Task("task-a");

        ExecutableTask taskB =
            Task("task-b");

        ExecutableTask taskC =
            Task("task-c");

        ExecutableWork work =
            new(
                1,
                [
                    taskC,
                    taskA,
                    taskB
                ],
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ]);

        Assert.Same(
            taskC,
            work.Tasks[0]);

        Assert.Same(
            taskA,
            work.Tasks[1]);

        Assert.Same(
            taskB,
            work.Tasks[2]);
    }

    [Fact]
    public void Equality_IncludesDependencySequence()
    {
        ExecutableWork left =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ]);

        ExecutableWork right =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ]);

        Assert.Equal(
            left,
            right);

        Assert.Equal(
            left.GetHashCode(),
            right.GetHashCode());
    }

    [Fact]
    public void Equality_PreservesDependencyOrder()
    {
        ExecutableWork left =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b")
                ]);

        ExecutableWork right =
            new(
                1,
                [
                    Task("task-a"),
                    Task("task-b"),
                    Task("task-c")
                ],
                [
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-b"),
                    new ExecutableTaskDependency(
                        "task-c",
                        "task-a")
                ]);

        Assert.NotEqual(
            left,
            right);
    }

    private static ExecutableTask Task(
        string id_)
    {
        return new ExecutableTask(
            id_,
            $"source-{id_}",
            $"instruction-{id_}");
    }
}