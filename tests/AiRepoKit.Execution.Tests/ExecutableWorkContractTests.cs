namespace AiRepoKit.Execution.Tests;

using AiRepoKit.Execution;
using Xunit;

public sealed class ExecutableWorkContractTests
{
    [Fact]
    public void SchemaId_EqualsExpectedConstant()
    {
        ExecutableWork work =
            new(1, Array.Empty<ExecutableTask>());

        Assert.Equal(
            "ai.repokit.executable-work",
            work.SchemaId);
        Assert.Equal(
            ExecutableWork.CurrentSchemaId,
            work.SchemaId);
    }

    [Fact]
    public void SchemaVersion_EqualsExpectedConstant()
    {
        ExecutableWork work =
            new(1, Array.Empty<ExecutableTask>());

        Assert.Equal(
            4,
            work.SchemaVersion);
        Assert.Equal(
            ExecutableWork.CurrentSchemaVersion,
            work.SchemaVersion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    public void Constructor_AcceptsPositiveSourceRevision(
        int positiveRevision_)
    {
        ExecutableWork work =
            new(positiveRevision_, Array.Empty<ExecutableTask>());

        Assert.Equal(
            positiveRevision_,
            work.SourceImplementationPlanRevision);
    }

    [Fact]
    public void Constructor_RejectsZeroSourceRevision()
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new ExecutableWork(0, Array.Empty<ExecutableTask>()));

        Assert.Equal(
            "sourceImplementationPlanRevision_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-42)]
    public void Constructor_RejectsNegativeSourceRevision(
        int negativeRevision_)
    {
        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () =>
                    new ExecutableWork(negativeRevision_, Array.Empty<ExecutableTask>()));

        Assert.Equal(
            "sourceImplementationPlanRevision_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullTaskCollection()
    {
        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () =>
                    new ExecutableWork(1, null!));

        Assert.Equal(
            "tasks_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_AcceptsEmptyTaskCollection()
    {
        ExecutableWork work =
            new(1, Array.Empty<ExecutableTask>());

        Assert.NotNull(
            work.Tasks);
        Assert.Empty(
            work.Tasks);
    }

    [Fact]
    public void Constructor_RejectsNullTaskElement()
    {
        ExecutableTask?[] tasksWithNull =
            [
                new ExecutableTask("task-1", "step-1", "instruction 1"),
                null
            ];

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(1, tasksWithNull!));

        Assert.Equal(
            "tasks_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_PreservesTaskOrderExactly()
    {
        ExecutableTask taskA =
            new("A", "step-1", "First");
        ExecutableTask taskB =
            new("B", "step-2", "Second");
        ExecutableTask taskC =
            new("C", "step-3", "Third");

        ExecutableWork work =
            new(1, [taskC, taskA, taskB]);

        Assert.Equal(
            3,
            work.Tasks.Count);
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
    public void MutationOfCallerCollectionAfterConstruction_DoesNotAlterWork()
    {
        ExecutableTask task1 =
            new("t1", "step-1", "instruction 1");
        List<ExecutableTask> list =
            [task1];

        ExecutableWork work =
            new(1, list);

        list.Add(
            new ExecutableTask("t2", "step-2", "instruction 2"));
        list[0] =
            new ExecutableTask("t3", "step-3", "instruction 3");
        list.Clear();

        Assert.Single(
            work.Tasks);
        Assert.Same(
            task1,
            work.Tasks[0]);
    }

    [Fact]
    public void ExposedTasksCollection_CannotBeUsedToMutateWork()
    {
        ExecutableTask task =
            new("t1", "step-1", "instruction 1");
        ExecutableWork work =
            new(1, [task]);

        Assert.IsNotType<ExecutableTask[]>(
            work.Tasks);
        Assert.IsNotType<List<ExecutableTask>>(
            work.Tasks);

        if (work.Tasks is IList<ExecutableTask> mutableList)
        {
            Assert.True(
                mutableList.IsReadOnly);

            Assert.Throws<NotSupportedException>(
                () =>
                    mutableList.Add(new ExecutableTask("t2", "step-2", "instruction 2")));

            Assert.Throws<NotSupportedException>(
                () =>
                    mutableList.Clear());

            Assert.Throws<NotSupportedException>(
                () =>
                    mutableList.Remove(task));

            Assert.Throws<NotSupportedException>(
                () =>
                    mutableList[0] = new ExecutableTask("t2", "step-2", "instruction 2"));
        }
    }

    [Fact]
    public void Constructor_RejectsDuplicateTaskIds_UsingOrdinalSemantics()
    {
        ExecutableTask task1 =
            new("TASK-1", "step-1", "First");
        ExecutableTask task2 =
            new("TASK-1", "step-2", "Second");

        ArgumentException exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new ExecutableWork(1, [task1, task2]));

        Assert.Equal(
            "tasks_",
            exception.ParamName);
    }

    [Fact]
    public void Constructor_AcceptsTaskIdsDifferingOnlyByCase()
    {
        ExecutableTask task1 =
            new("TASK-1", "step-1", "Upper");
        ExecutableTask task2 =
            new("task-1", "step-2", "Lower");

        ExecutableWork work =
            new(1, [task1, task2]);

        Assert.Equal(
            2,
            work.Tasks.Count);
        Assert.Equal(
            "TASK-1",
            work.Tasks[0].Id);
        Assert.Equal(
            "task-1",
            work.Tasks[1].Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ExecutableTask_Constructor_RejectsNullEmptyOrWhitespaceId(
        string? invalidId_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ExecutableTask(invalidId_!, "step-1", "instruction"));

        Assert.Equal(
            "id_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ExecutableTask_Constructor_RejectsNullEmptyOrWhitespaceSourcePlanStepId(
        string? invalidStepId_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ExecutableTask("task-1", invalidStepId_!, "instruction"));

        Assert.Equal(
            "sourcePlanStepId_",
            exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void ExecutableTask_Constructor_RejectsNullEmptyOrWhitespaceInstruction(
        string? invalidInstruction_)
    {
        ArgumentException exception =
            Assert.ThrowsAny<ArgumentException>(
                () =>
                    new ExecutableTask("task-1", "step-1", invalidInstruction_!));

        Assert.Equal(
            "instruction_",
            exception.ParamName);
    }

    [Fact]
    public void ExecutableTask_PreservesTaskIdTextExactly()
    {
        string rawId =
            "  TASK-01_opaque  ";

        ExecutableTask task =
            new(rawId, "step-1", "instruction");

        Assert.Equal(
            rawId,
            task.Id);
    }

    [Fact]
    public void ExecutableTask_PreservesSourcePlanStepIdTextExactly()
    {
        string rawStepId =
            "  custom-step-id-no-prefix  ";

        ExecutableTask task =
            new("task-1", rawStepId, "instruction");

        Assert.Equal(
            rawStepId,
            task.SourcePlanStepId);
    }

    [Fact]
    public void ExecutableTask_PreservesInstructionTextExactly()
    {
        string rawInstruction =
            "  Do something with spaces and \r\n multiple lines \t ";

        ExecutableTask task =
            new("task-1", "step-1", rawInstruction);

        Assert.Equal(
            rawInstruction,
            task.Instruction);
    }

    [Fact]
    public void MultipleExecutableTasks_MayReferenceSameSourcePlanStepId()
    {
        string sharedStepId =
            "PLAN-STEP-01";

        ExecutableTask task1 =
            new("task-1", sharedStepId, "First action");
        ExecutableTask task2 =
            new("task-2", sharedStepId, "Second action");

        ExecutableWork work =
            new(1, [task1, task2]);

        Assert.Equal(
            2,
            work.Tasks.Count);
        Assert.Equal(
            sharedStepId,
            work.Tasks[0].SourcePlanStepId);
        Assert.Equal(
            sharedStepId,
            work.Tasks[1].SourcePlanStepId);
    }
}
