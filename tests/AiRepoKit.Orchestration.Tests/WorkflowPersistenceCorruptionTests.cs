namespace AiRepoKit.Orchestration.Tests;

using System.Text;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using Xunit;

public sealed class WorkflowPersistenceCorruptionTests
{
    [Fact]
    public void Load_RejectsUtf8Bom()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        string recordPath =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1);

        byte[] original =
            File.ReadAllBytes(
                recordPath);

        byte[] withBom =
            new byte[original.Length + 3];

        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;

        Buffer.BlockCopy(
            original,
            0,
            withBom,
            3,
            original.Length);

        File.WriteAllBytes(
            recordPath,
            withBom);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsInvalidUtf8()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        File.WriteAllBytes(
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1),
            [
                0xFF,
                0xFE,
                0xFD
            ]);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsMalformedJson()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        File.WriteAllText(
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1),
            "{",
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsWrongSchemaId()
    {
        AssertCorruptionRejected(
            json_ =>
                json_.Replace(
                    WorkflowPersistenceStore.SchemaId,
                    "ai.repokit.invalid-schema",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Load_RejectsUnsupportedSchemaVersion()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":999"));
    }

    [Fact]
    public void Load_RejectsWrongWorkflowId()
    {
        AssertCorruptionRejected(
            json_ =>
                json_.Replace(
                    "\"workflowId\":\"workflow-a\"",
                    "\"workflowId\":\"workflow-b\"",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Load_RejectsRevisionGap()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        string first =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1);

        string second =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                2);

        File.Move(
            first,
            second);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsFilenameRecordRevisionMismatch()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"revision\":1",
                    "\"revision\":2"));
    }

    [Fact]
    public void Load_RejectsRevisionEventSequenceMismatch()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"sequence\":1",
                    "\"sequence\":2"));
    }

    [Fact]
    public void Load_RejectsMalformedInitializationEventShape()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"taskId\":null",
                    "\"taskId\":\"task-a\""));
    }

    [Fact]
    public void Load_RejectsInitialStateEventMismatch()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"status\":1",
                    "\"status\":2"));
    }

    [Fact]
    public void Load_RejectsPersistedIllegalTransition()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        WorkflowState created =
            store.Load()!.State;

        WorkflowState running =
            WorkflowStateMachine.TransitionWorkflow(
                created,
                WorkflowStatus.Running);

        _ =
            store.Append(
                running,
                1);

        string secondPath =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                2);

        string json =
            File.ReadAllText(
                secondPath,
                Encoding.UTF8);

        json =
            ReplaceOnce(
                json,
                "\"targetWorkflowStatus\":2",
                "\"targetWorkflowStatus\":3");

        json =
            ReplaceOnce(
                json,
                "\"status\":2",
                "\"status\":3");

        WriteCanonicalText(
            secondPath,
            json);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsNonCanonicalRecordFilename()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        string recordsDirectory =
            Path.GetDirectoryName(
                RecordPath(
                    temp.Path,
                    store.WorkflowId,
                    1))!;

        File.WriteAllText(
            Path.Combine(
                recordsDirectory,
                "notes.txt"),
            "not-a-record",
            Encoding.UTF8);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsDuplicateLogicalRevision()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        File.Copy(
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1),
            RecordPath(
                temp.Path,
                store.WorkflowId,
                2));

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void Load_RejectsInvalidPersistedTaskId()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"taskId\":\"task-a\",\"status\":1",
                    "\"taskId\":\"\",\"status\":1"));
    }

    [Fact]
    public void Load_RejectsDuplicatePersistedTaskIds()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            new(
                temp.Path,
                new WorkflowId(
                    "workflow-a"));

        _ =
            store.Initialize(
                CreatedState(
                    "task-a",
                    "task-b"));

        string recordPath =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1);

        string json =
            File.ReadAllText(
                recordPath,
                Encoding.UTF8);

        json =
            ReplaceOnce(
                json,
                "\"taskId\":\"task-b\",\"status\":1",
                "\"taskId\":\"task-a\",\"status\":1");

        WriteCanonicalText(
            recordPath,
            json);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    [Fact]
    public void ReadEvents_AlsoValidatesPersistedHistory()
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        string recordPath =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1);

        string json =
            File.ReadAllText(
                recordPath,
                Encoding.UTF8);

        json =
            ReplaceOnce(
                json,
                "\"schemaVersion\":1",
                "\"schemaVersion\":999");

        WriteCanonicalText(
            recordPath,
            json);

        Assert.Throws<InvalidDataException>(
            () =>
                store.ReadEvents());
    }

    [Fact]
    public void Load_RejectsUnknownJsonProperties()
    {
        AssertCorruptionRejected(
            json_ =>
                ReplaceOnce(
                    json_,
                    "\"schemaVersion\":1",
                    "\"schemaVersion\":1,\"unexpected\":true"));
    }

    private static void AssertCorruptionRejected(
        Func<string, string> corrupt_)
    {
        using TempDirectory temp =
            new();

        WorkflowPersistenceStore store =
            InitializedStore(
                temp.Path);

        string recordPath =
            RecordPath(
                temp.Path,
                store.WorkflowId,
                1);

        string original =
            File.ReadAllText(
                recordPath,
                Encoding.UTF8);

        string corrupted =
            corrupt_(
                original);

        Assert.NotEqual(
            original,
            corrupted);

        WriteCanonicalText(
            recordPath,
            corrupted);

        Assert.Throws<InvalidDataException>(
            () =>
                store.Load());
    }

    private static WorkflowPersistenceStore InitializedStore(
        string root_)
    {
        WorkflowPersistenceStore store =
            new(
                root_,
                new WorkflowId(
                    "workflow-a"));

        _ =
            store.Initialize(
                CreatedState(
                    "task-a"));

        return store;
    }

    private static WorkflowState CreatedState(
        params string[] taskIds_)
    {
        ExecutableTask[] tasks =
            new ExecutableTask[taskIds_.Length];

        for (int index = 0; index < taskIds_.Length; index++)
        {
            string taskId =
                taskIds_[index];

            tasks[index] =
                new ExecutableTask(
                    taskId,
                    $"plan-{taskId}",
                    $"instruction-{taskId}");
        }

        return WorkflowStateMachine.Create(
            new ExecutableWork(
                7,
                tasks));
    }

    private static string RecordPath(
        string root_,
        WorkflowId workflowId_,
        long revision_)
    {
        string encoded =
            Convert
                .ToHexString(
                    Encoding.UTF8.GetBytes(
                        workflowId_.Value))
                .ToLowerInvariant();

        return Path.Combine(
            Path.GetFullPath(
                root_),
            "workflows",
            encoded,
            "records",
            $"{revision_:D20}.json");
    }

    private static string ReplaceOnce(
        string value_,
        string oldValue_,
        string newValue_)
    {
        int index =
            value_.IndexOf(
                oldValue_,
                StringComparison.Ordinal);

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Expected JSON fragment not found: {oldValue_}");
        }

        return
            value_[..index] +
            newValue_ +
            value_[(index + oldValue_.Length)..];
    }

    private static void WriteCanonicalText(
        string path_,
        string value_)
    {
        File.WriteAllText(
            path_,
            value_,
            new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false));
    }

    private sealed class TempDirectory :
        IDisposable
    {
        public string Path
        {
            get;
        }

        public TempDirectory()
        {
            this.Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"airepokit-orchestration-corruption-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                this.Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(
                    this.Path,
                    recursive: true);
            }
            catch
            {
            }
        }
    }
}
