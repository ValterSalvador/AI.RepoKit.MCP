namespace AiRepoKit.WorkflowExecution.Tests;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Microsoft.Win32.SafeHandles;
using Xunit;

public sealed class WorkflowEvidencePackExporterTests : IDisposable
{
    private const string ValidHash1 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ValidHash2 = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    private const string ValidHash3 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ValidHash4 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ValidHash5 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static void WriteFileNoBom(string path_, string content_)
    {
        File.WriteAllText(path_, content_, Utf8NoBom);
    }

    private readonly string _tempDirectory;

    public WorkflowEvidencePackExporterTests()
    {
        this._tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "repokit-p11-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDirectory))
        {
            try
            {
                Directory.Delete(this._tempDirectory, recursive: true);
            }
            catch
            {
            }
        }
    }

    // =========================================================================
    // Category 1: Public Surface, Constants & Constructor Validation (Tests 1-8)
    // =========================================================================

    [Fact]
    public void Constants_HaveFrozenValues()
    {
        Assert.Equal("ai.repokit.workflow-evidence-pack/v1", WorkflowEvidencePackExporter.AlgorithmId);
        Assert.Equal("ai.repokit.workflow-evidence-manifest", WorkflowEvidencePackExporter.ManifestSchemaId);
        Assert.Equal(1, WorkflowEvidencePackExporter.CurrentSchemaVersion);
    }

    [Fact]
    public void Constructor_NullPersistenceStore_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkflowEvidencePackExporter(null!));
    }

    [Fact]
    public void Constructor_ValidPersistenceStore_Succeeds()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);
        Assert.NotNull(exporter);
    }

    [Fact]
    public async Task ExportAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(cts.Token));
    }

    [Fact]
    public async Task ExportAsync_UninitializedStore_ThrowsInvalidOperationException()
    {
        WorkflowPersistenceStore uninitializedStore = new(this._tempDirectory, new WorkflowId("wf-uninit"));
        WorkflowEvidencePackExporter exporter = new(uninitializedStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task VerifyPackAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            exporter.VerifyPackAsync(1, ValidHash1, ValidHash2, cts.Token));
    }

    [Fact]
    public async Task VerifyPackAsync_OverloadWithPack_NullPack_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<ArgumentNullException>(() => exporter.VerifyPackAsync(null!));
    }

    [Fact]
    public async Task VerifyPackAsync_NegativeOrZeroRevision_ThrowsArgumentOutOfRangeException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            exporter.VerifyPackAsync(0, ValidHash1, ValidHash2));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            exporter.VerifyPackAsync(-1, ValidHash1, ValidHash2));
    }

    // =========================================================================
    // Category 2: Basic Export & Workflow States (Tests 9-16)
    // =========================================================================

    [Fact]
    public async Task ExportAsync_CreatedState_ExportsValidEvidencePack()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.NotNull(pack);
        Assert.Equal(1, pack.CapturedPersistenceRevision);
        Assert.Equal(WorkflowStatus.Created, pack.Manifest.WorkflowStatus);
        Assert.Single(pack.Manifest.Events);
        Assert.Equal(WorkflowExecutionEventKind.WorkflowInitialized, pack.Manifest.Events[0].Kind);
    }

    [Fact]
    public async Task ExportAsync_RunningState_ExportsValidEvidencePack()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(2, pack.CapturedPersistenceRevision);
        Assert.Equal(WorkflowStatus.Running, pack.Manifest.WorkflowStatus);
        Assert.Equal(2, pack.Manifest.Events.Count);
        Assert.Equal(WorkflowExecutionEventKind.WorkflowStatusTransitioned, pack.Manifest.Events[1].Kind);
    }

    [Fact]
    public async Task ExportAsync_CompletedState_ExportsValidEvidencePack()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowState stepRunning = WorkflowStateMachine.TransitionStep(running, "task-1", WorkflowStepStatus.Running);
        store.Append(stepRunning, 2);

        WorkflowState stepAwaiting = WorkflowStateMachine.TransitionStep(stepRunning, "task-1", WorkflowStepStatus.AwaitingValidation);
        store.Append(stepAwaiting, 3);

        WorkflowState stepCompleted = WorkflowStateMachine.TransitionStep(stepAwaiting, "task-1", WorkflowStepStatus.Completed);
        store.Append(stepCompleted, 4);

        WorkflowState completed = WorkflowStateMachine.TransitionWorkflow(stepCompleted, WorkflowStatus.Completed);
        store.Append(completed, 5);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(6, pack.CapturedPersistenceRevision);
        Assert.Equal(WorkflowStatus.Completed, pack.Manifest.WorkflowStatus);
        Assert.Equal(6, pack.Manifest.Events.Count);
    }

    [Fact]
    public async Task ExportAsync_FailedState_ExportsValidEvidencePack()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowState stepRunning = WorkflowStateMachine.TransitionStep(running, "task-1", WorkflowStepStatus.Running);
        store.Append(stepRunning, 2);

        WorkflowState stepFailed = WorkflowStateMachine.TransitionStep(stepRunning, "task-1", WorkflowStepStatus.Failed);
        store.Append(stepFailed, 3);

        WorkflowState failed = WorkflowStateMachine.TransitionWorkflow(stepFailed, WorkflowStatus.Failed);
        store.Append(failed, 4);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(5, pack.CapturedPersistenceRevision);
        Assert.Equal(WorkflowStatus.Failed, pack.Manifest.WorkflowStatus);
    }

    [Fact]
    public async Task ExportAsync_CancelledState_ExportsValidEvidencePack()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowState cancelled = WorkflowStateMachine.TransitionWorkflow(running, WorkflowStatus.Cancelled);
        store.Append(cancelled, 2);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(3, pack.CapturedPersistenceRevision);
        Assert.Equal(WorkflowStatus.Cancelled, pack.Manifest.WorkflowStatus);
    }

    [Fact]
    public async Task ExportAsync_MultipleTasksWorkflow_ExportsAllTasks()
    {
        ExecutableWork work = new(
            1,
            [
                new ExecutableTask("task-1", "step-1", "work 1"),
                new ExecutableTask("task-2", "step-2", "work 2"),
                new ExecutableTask("task-3", "step-3", "work 3")
            ],
            [],
            [],
            [],
            []);

        WorkflowPersistenceStore store = new(this._tempDirectory, new WorkflowId("wf-multi"));
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(3, pack.Manifest.Tasks.Count);
        Assert.Equal("task-1", pack.Manifest.Tasks[0].TaskId);
        Assert.Equal("task-2", pack.Manifest.Tasks[1].TaskId);
        Assert.Equal("task-3", pack.Manifest.Tasks[2].TaskId);
    }

    [Fact]
    public async Task ExportAsync_MultipleStatusTransitions_EventsPreserveExactRevisionsAndSequence()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowState stepRunning = WorkflowStateMachine.TransitionStep(running, "task-1", WorkflowStepStatus.Running);
        store.Append(stepRunning, 2);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(3, pack.Manifest.Events.Count);
        for (int i = 0; i < pack.Manifest.Events.Count; i++)
        {
            Assert.Equal(i + 1, pack.Manifest.Events[i].Revision);
        }
    }

    [Fact]
    public async Task ExportAsync_ReturnedPack_HasExpectedPropertiesMatchingManifest()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(pack.Manifest.PackId, pack.PackId);
        Assert.Equal(pack.Manifest.WorkflowId, pack.WorkflowId);
        Assert.Equal(pack.Manifest.CapturedPersistenceRevision, pack.CapturedPersistenceRevision);
        Assert.True(File.Exists(pack.ManifestPath));
        Assert.True(Directory.Exists(pack.PackDirectory));
    }

    // =========================================================================
    // Category 3: Manifest Encoding, JSON Structure & Property Order (Tests 17-25)
    // =========================================================================

    [Fact]
    public async Task ExportAsync_ManifestFile_HasNoUtf8Bom()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        byte[] bytes = File.ReadAllBytes(pack.ManifestPath);

        Assert.True(bytes.Length >= 3);
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_RootPropertiesInFrozenOrder()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedRootKeys =
        [
            "schemaId", "schemaVersion", "algorithmId", "packId", "workflowId",
            "sourceImplementationPlanRevision", "capturedPersistenceRevision",
            "workflowStatus", "sourceSetFingerprint", "sourceRecords", "events", "tasks"
        ];

        int lastIndex = -1;
        foreach (string key in expectedRootKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, StringComparison.Ordinal);
            Assert.True(index > lastIndex, $"Key '{key}' was expected after position {lastIndex}, but found at {index}.");
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_SourceRecordsInFrozenOrder()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedSourceKeys = ["relativePath", "byteLength", "sha256Fingerprint"];

        int lastIndex = json.IndexOf("\"sourceRecords\":", StringComparison.Ordinal);
        foreach (string key in expectedSourceKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_EventsInFrozenOrder()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedEventKeys =
        [
            "workflowId", "revision", "kind", "taskId", "previousWorkflowStatus",
            "targetWorkflowStatus", "previousStepStatus", "targetStepStatus"
        ];

        int lastIndex = json.IndexOf("\"events\":", StringComparison.Ordinal);
        foreach (string key in expectedEventKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_TasksInFrozenOrder()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedTaskKeys = ["taskId", "stepStatus", "checkpointJournals", "gateJournals"];

        int lastIndex = json.IndexOf("\"tasks\":", StringComparison.Ordinal);
        foreach (string key in expectedTaskKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_CheckpointJournalsInFrozenOrder()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedCheckpointKeys =
        [
            "basePersistenceRevision", "baseWorkflowStateFingerprint", "inputFingerprint",
            "registryFingerprint", "executionCompleted", "finalStateFingerprint",
            "attemptCount", "budgetExhausted", "expectedProjectionTransitionCount",
            "projectionCompleted", "finalPersistenceRevision", "records"
        ];

        int lastIndex = json.IndexOf("\"checkpointJournals\":", StringComparison.Ordinal);
        foreach (string key in expectedCheckpointKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_GateJournalsInFrozenOrder()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedGateKeys =
        [
            "basePersistenceRevision", "authorized", "principalId",
            "mechanismId", "evidenceFingerprint", "challenge"
        ];

        int lastIndex = json.IndexOf("\"gateJournals\":", StringComparison.Ordinal);
        foreach (string key in expectedGateKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_GateChallengeInFrozenOrder()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedChallengeKeys =
        [
            "gateId", "workflowId", "taskId", "basePersistenceRevision",
            "sourceImplementationPlanRevision", "requiredPermission", "gateOrdinal",
            "baseStateFingerprint", "inputFingerprint", "registryFingerprint", "policyFingerprint"
        ];

        int lastIndex = json.IndexOf("\"challenge\":", StringComparison.Ordinal);
        foreach (string key in expectedChallengeKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    [Fact]
    public async Task ExportAsync_ManifestFile_JournalRecordsInFrozenOrder()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string[] expectedRecordKeys =
        [
            "journalSequence", "recordKind", "operationOrdinal", "operationId", "operationKind",
            "invocationGeneration", "requestFingerprint", "providerId", "validationExternalIdentityDigest",
            "isNullReturn", "agentStatus", "agentOutputDigest", "agentDiagnosticDigest",
            "validationPassed", "validationEvidenceDigest", "finalStateFingerprint",
            "attemptCount", "budgetExhausted", "expectedProjectionTransitionCount", "finalPersistenceRevision"
        ];

        int lastIndex = json.IndexOf("\"records\":", StringComparison.Ordinal);
        foreach (string key in expectedRecordKeys)
        {
            string token = $"\"{key}\":";
            int index = json.IndexOf(token, lastIndex, StringComparison.Ordinal);
            Assert.True(index > lastIndex);
            lastIndex = index;
        }
    }

    // =========================================================================
    // Category 4: Deterministic PackId & SourceSet Fingerprint (Tests 26-32)
    // =========================================================================

    [Fact]
    public async Task ExportAsync_PackId_IsDeterministicAcrossIdenticalRuns()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack1 = await exporter.ExportAsync();
        WorkflowEvidencePack pack2 = await exporter.ExportAsync();

        Assert.Equal(pack1.PackId, pack2.PackId);
        Assert.Equal(pack1.ManifestFingerprint, pack2.ManifestFingerprint);
    }

    [Fact]
    public async Task ExportAsync_PackId_IsSha256LowerHex64()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(64, pack.PackId.Length);
        Assert.True(IsLowerHex(pack.PackId));
    }

    [Fact]
    public async Task ExportAsync_SourceSetFingerprint_IsSha256LowerHex64()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(64, pack.Manifest.SourceSetFingerprint.Length);
        Assert.True(IsLowerHex(pack.Manifest.SourceSetFingerprint));
    }

    [Fact]
    public async Task ExportAsync_ManifestFingerprint_MatchesExactBytesSha256()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        byte[] bytes = File.ReadAllBytes(pack.ManifestPath);
        string computed = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Assert.Equal(computed, pack.ManifestFingerprint);
    }

    [Fact]
    public async Task ExportAsync_ManifestFingerprint_NotContainedInManifestJson()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        string json = File.ReadAllText(pack.ManifestPath);

        Assert.DoesNotContain(pack.ManifestFingerprint, json, StringComparison.Ordinal);
        Assert.DoesNotContain("manifestFingerprint", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_SourceRecords_SortedOrdinally()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        for (int i = 1; i < pack.Manifest.SourceRecords.Count; i++)
        {
            int cmp = string.CompareOrdinal(
                pack.Manifest.SourceRecords[i - 1].RelativePath,
                pack.Manifest.SourceRecords[i].RelativePath);
            Assert.True(cmp < 0);
        }
    }

    [Fact]
    public async Task ExportAsync_SourceRecords_ContainsEveryCanonicalPersistenceRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Contains(pack.Manifest.SourceRecords, r => r.RelativePath.EndsWith("00000000000000000001.json", StringComparison.Ordinal));
        Assert.Contains(pack.Manifest.SourceRecords, r => r.RelativePath.EndsWith("00000000000000000002.json", StringComparison.Ordinal));
    }

    // =========================================================================
    // Category 5: Checkpoint Journals & All Six P09 Record Kinds (Tests 33-42)
    // =========================================================================

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesCheckpointStartedRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        WorkflowJournalRecordEvidence started = Assert.Single(cp.Records, r => r.RecordKind == "CheckpointStarted");
        Assert.Equal(1, started.JournalSequence);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesInvocationIntentRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        InvocationIntentRecord intent = new()
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-id-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            ExternalIdentity = "test.agent",
            RequestFingerprint = ValidHash1
        };
        journal.AppendRecord(intent);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        WorkflowJournalRecordEvidence record = Assert.Single(cp.Records, r => r.RecordKind == "InvocationIntent");
        Assert.Equal(2, record.JournalSequence);
        Assert.Equal(1, record.OperationOrdinal);
        Assert.Equal("op-id-1", record.OperationId);
        Assert.Equal("Agent", record.OperationKind);
        Assert.Equal(1, record.InvocationGeneration);
        Assert.Equal(ValidHash1, record.RequestFingerprint);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesInvocationOutcomeRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        InvocationOutcomeRecord outcome = new()
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-id-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentStatus = 0,
            AgentOutputText = "completed successfully",
            AgentDiagnosticText = "no warnings",
            AgentSessionReference = "secret-session-id"
        };
        journal.AppendRecord(outcome);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        WorkflowJournalRecordEvidence record = Assert.Single(cp.Records, r => r.RecordKind == "InvocationOutcome");
        Assert.Equal(2, record.JournalSequence);
        Assert.False(record.IsNullReturn);
        Assert.Equal(0, record.AgentStatus);
        Assert.NotNull(record.AgentOutputDigest);
        Assert.NotNull(record.AgentDiagnosticDigest);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesReconciliationNotExecutedRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        ReconciliationNotExecutedRecord rne = new()
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-id-1",
            InvocationGeneration = 1
        };
        journal.AppendRecord(rne);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        WorkflowJournalRecordEvidence record = Assert.Single(cp.Records, r => r.RecordKind == "ReconciliationNotExecuted");
        Assert.Equal(2, record.JournalSequence);
        Assert.Equal(1, record.OperationOrdinal);
        Assert.Equal("op-id-1", record.OperationId);
        Assert.Equal(1, record.InvocationGeneration);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesExecutionCompletedRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        ExecutionCompletedRecord ec = new()
        {
            Sequence = 2,
            FinalStateFingerprint = ValidHash2,
            AttemptCount = 3,
            BudgetExhausted = false,
            ExpectedProjectionTransitionCount = 2
        };
        journal.AppendRecord(ec);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.True(cp.ExecutionCompleted);
        Assert.Equal(ValidHash2, cp.FinalStateFingerprint);
        Assert.Equal(3, cp.AttemptCount);
        Assert.False(cp.BudgetExhausted);
        Assert.Equal(2, cp.ExpectedProjectionTransitionCount);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesProjectionCompletedRecord()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        ProjectionCompletedRecord pc = new()
        {
            Sequence = 2,
            FinalPersistenceRevision = 1
        };
        journal.AppendRecord(pc);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.True(cp.ProjectionCompleted);
        Assert.Equal(1, cp.FinalPersistenceRevision);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_AllSixRecordKindsInSingleJournal()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationIntentRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            ExternalIdentity = "prov-1",
            RequestFingerprint = ValidHash1
        });

        journal.AppendRecord(new ReconciliationNotExecutedRecord
        {
            Sequence = 3,
            OperationOrdinal = 1,
            OperationId = "op-1",
            InvocationGeneration = 1
        });

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 4,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 2,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentStatus = 0
        });

        journal.AppendRecord(new ExecutionCompletedRecord
        {
            Sequence = 5,
            FinalStateFingerprint = ValidHash2,
            AttemptCount = 1,
            BudgetExhausted = false,
            ExpectedProjectionTransitionCount = 1
        });

        journal.AppendRecord(new ProjectionCompletedRecord
        {
            Sequence = 6,
            FinalPersistenceRevision = 1
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Equal(6, cp.Records.Count);
        Assert.Equal("CheckpointStarted", cp.Records[0].RecordKind);
        Assert.Equal("InvocationIntent", cp.Records[1].RecordKind);
        Assert.Equal("ReconciliationNotExecuted", cp.Records[2].RecordKind);
        Assert.Equal("InvocationOutcome", cp.Records[3].RecordKind);
        Assert.Equal("ExecutionCompleted", cp.Records[4].RecordKind);
        Assert.Equal("ProjectionCompleted", cp.Records[5].RecordKind);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesOperationOrdinalAndOperationId()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationIntentRecord
        {
            Sequence = 2,
            OperationOrdinal = 42,
            OperationId = "op-42-id",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            ExternalIdentity = "provider-a",
            RequestFingerprint = ValidHash1
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        WorkflowJournalRecordEvidence rec = cp.Records[1];
        Assert.Equal(42, rec.OperationOrdinal);
        Assert.Equal("op-42-id", rec.OperationId);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesExpectedProjectionTransitionCount()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new ExecutionCompletedRecord
        {
            Sequence = 2,
            FinalStateFingerprint = ValidHash1,
            AttemptCount = 2,
            BudgetExhausted = false,
            ExpectedProjectionTransitionCount = 5
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Equal(5, cp.ExpectedProjectionTransitionCount);
    }

    [Fact]
    public async Task ExportAsync_TaskWithCheckpointJournal_PreservesInvocationGenerations()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationIntentRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 3,
            ExternalIdentity = "prov-x",
            RequestFingerprint = ValidHash1
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Equal(3, cp.Records[1].InvocationGeneration);
    }

    // =========================================================================
    // Category 6: Privacy Boundary (Tests 43-52)
    // =========================================================================

    [Fact]
    public async Task Privacy_ValidAgentProviderId_PreservedVerbatimInProviderId()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationIntentRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            ExternalIdentity = "anthropic.claude",
            RequestFingerprint = ValidHash1
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Equal("anthropic.claude", cp.Records[1].ProviderId);
        Assert.Null(cp.Records[1].ValidationExternalIdentityDigest);
    }

    [Fact]
    public async Task Privacy_InvalidAgentProviderId_HashedIntoValidationExternalIdentityDigest()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationIntentRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            ExternalIdentity = "-invalid-leading-hyphen-",
            RequestFingerprint = ValidHash1
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Null(cp.Records[1].ProviderId);
        Assert.NotNull(cp.Records[1].ValidationExternalIdentityDigest);
        Assert.Equal(64, cp.Records[1].ValidationExternalIdentityDigest!.Length);
    }

    [Fact]
    public async Task Privacy_ValidationRequirementId_HashedIntoValidationExternalIdentityDigest()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationIntentRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Validation",
            InvocationGeneration = 1,
            ExternalIdentity = "1:val-req-99",
            RequestFingerprint = ValidHash1
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Null(cp.Records[1].ProviderId);
        Assert.NotNull(cp.Records[1].ValidationExternalIdentityDigest);
        string expectedDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1:val-req-99"))).ToLowerInvariant();
        Assert.Equal(expectedDigest, cp.Records[1].ValidationExternalIdentityDigest);
    }

    [Fact]
    public async Task Privacy_AgentOutputText_HashedIntoAgentOutputDigest()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentStatus = 0,
            AgentOutputText = "sensitive model output containing code"
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        string expectedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("sensitive model output containing code"))).ToLowerInvariant();
        Assert.Equal(expectedSha, cp.Records[1].AgentOutputDigest);
    }

    [Fact]
    public async Task Privacy_AgentDiagnosticText_HashedIntoAgentDiagnosticDigest()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentStatus = 0,
            AgentDiagnosticText = "sensitive internal stack trace"
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        string expectedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("sensitive internal stack trace"))).ToLowerInvariant();
        Assert.Equal(expectedSha, cp.Records[1].AgentDiagnosticDigest);
    }

    [Fact]
    public async Task Privacy_ValidationEvidence_HashedIntoValidationEvidenceDigest()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Validation",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            ValidationPassed = true,
            ValidationEvidence = "test stdout log with private paths"
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        string expectedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("test stdout log with private paths"))).ToLowerInvariant();
        Assert.Equal(expectedSha, cp.Records[1].ValidationEvidenceDigest);
    }

    [Fact]
    public async Task Privacy_NullAgentOutputs_ProduceNullDigests()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = true,
            AgentOutputText = null,
            AgentDiagnosticText = null
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Null(cp.Records[1].AgentOutputDigest);
        Assert.Null(cp.Records[1].AgentDiagnosticDigest);
    }

    [Fact]
    public async Task Privacy_NullValidationEvidence_ProducesNullDigest()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Validation",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            ValidationPassed = true,
            ValidationEvidence = null
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Null(cp.Records[1].ValidationEvidenceDigest);
    }

    [Fact]
    public async Task Privacy_AgentSessionReference_CompletelyExcludedFromExport()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentStatus = 0,
            AgentSessionReference = "super-secret-user-token-12345"
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        Assert.DoesNotContain("super-secret-user-token-12345", json, StringComparison.Ordinal);
        Assert.DoesNotContain("agentSessionReference", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Privacy_NoPlaintextAgentOutputsOrDiagnosticsInManifestJson()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        journal.AppendRecord(new InvocationOutcomeRecord
        {
            Sequence = 2,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentOutputText = "my-secret-prompt-output",
            AgentDiagnosticText = "my-secret-diagnostic"
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        Assert.DoesNotContain("my-secret-prompt-output", json, StringComparison.Ordinal);
        Assert.DoesNotContain("my-secret-diagnostic", json, StringComparison.Ordinal);
    }

    // =========================================================================
    // Category 7: Human Gate Journals & P10 Durable Decisions (Tests 53-62)
    // =========================================================================

    [Fact]
    public async Task ExportAsync_TaskWithGateJournal_AuthorizedGranted_PreservesDurableGrant()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "admin-user",
            MechanismId = "webauthn-hardware-key",
            EvidenceFingerprint = ValidHash3
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.True(gj.Authorized);
        Assert.Equal("admin-user", gj.PrincipalId);
        Assert.Equal("webauthn-hardware-key", gj.MechanismId);
        Assert.Equal(ValidHash3, gj.EvidenceFingerprint);
    }

    [Fact]
    public async Task ExportAsync_TaskWithGateJournal_AuthorizedDenied_PreservesDurableDenial()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        journal.AppendRecord(new AuthorizationDeniedRecord
        {
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "sec-officer",
            MechanismId = "policy-engine",
            EvidenceFingerprint = ValidHash4
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.False(gj.Authorized);
        Assert.Equal("sec-officer", gj.PrincipalId);
        Assert.Equal("policy-engine", gj.MechanismId);
        Assert.Equal(ValidHash4, gj.EvidenceFingerprint);
    }

    [Fact]
    public async Task ExportAsync_TaskWithGateJournal_CrashWindow_ChallengeCreatedOnly_HasNullAuthorized()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.Null(gj.Authorized);
        Assert.Null(gj.PrincipalId);
        Assert.Null(gj.MechanismId);
        Assert.Null(gj.EvidenceFingerprint);
    }

    [Fact]
    public async Task ExportAsync_GateJournal_PreservesPrincipalIdAndMechanismId()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "reviewer-123",
            MechanismId = "github-pr-review",
            EvidenceFingerprint = ValidHash5
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.Equal("reviewer-123", gj.PrincipalId);
        Assert.Equal("github-pr-review", gj.MechanismId);
    }

    [Fact]
    public async Task ExportAsync_GateJournal_PreservesEvidenceFingerprint()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "p-1",
            MechanismId = "m-1",
            EvidenceFingerprint = ValidHash2
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.Equal(ValidHash2, gj.EvidenceFingerprint);
    }

    [Fact]
    public async Task ExportAsync_GateJournal_RawProofOpaqueEvidence_Excluded()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "p-1",
            MechanismId = "m-1",
            EvidenceFingerprint = ValidHash2
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        Assert.DoesNotContain("opaqueEvidence", json, StringComparison.Ordinal);
        Assert.DoesNotContain("rawProof", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_GateJournal_VerifierFailureReason_Excluded()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        journal.AppendRecord(new AuthorizationDeniedRecord
        {
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "p-1",
            MechanismId = "m-1",
            EvidenceFingerprint = ValidHash2
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        Assert.DoesNotContain("failureReason", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_GateJournal_RecomputesAndVerifiesDeterministicGateId()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.Equal(journal.ChallengeRecord.GateId, gj.Challenge.GateId);
    }

    [Fact]
    public async Task ExportAsync_TaskWithoutGateJournal_HasEmptyGateJournals_NoInferredBypass()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Empty(pack.Manifest.Tasks[0].GateJournals);
    }

    [Fact]
    public async Task ExportAsync_TaskWithoutCheckpointJournal_HasEmptyCheckpointJournals_NoInferredResumed()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Empty(pack.Manifest.Tasks[0].CheckpointJournals);
    }

    // =========================================================================
    // Category 8: Fingerprint Ordering Distinctness & Recomputation (Tests 63-68)
    // =========================================================================

    [Fact]
    public async Task Fingerprints_P09BaseWorkflowStateFingerprint_PreservesOriginalStepOrder()
    {
        ExecutableWork work = new(
            1,
            [
                new ExecutableTask("z-task", "step-1", "work z"),
                new ExecutableTask("a-task", "step-2", "work a")
            ],
            [],
            [],
            [],
            []);

        WorkflowPersistenceStore store = new(this._tempDirectory, new WorkflowId("wf-order-p09"));
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "z-task", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowCheckpointJournalEvidence cp = Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Equal(journal.StartedRecord.BaseWorkflowStateFingerprint, cp.BaseWorkflowStateFingerprint);
    }

    [Fact]
    public async Task Fingerprints_P10BaseStateFingerprint_PreservesOrdinalTaskIdOrder()
    {
        ExecutableWork work = new(
            1,
            [
                new ExecutableTask("z-task", "step-1", "work z"),
                new ExecutableTask("a-task", "step-2", "work a")
            ],
            [],
            [],
            [],
            []);

        WorkflowPersistenceStore store = new(this._tempDirectory, new WorkflowId("wf-order-p10"));
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "z-task", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowGateJournalEvidence gj = Assert.Single(pack.Manifest.Tasks[0].GateJournals);
        Assert.Equal(journal.ChallengeRecord.BaseStateFingerprint, gj.Challenge.BaseStateFingerprint);
    }

    [Fact]
    public void Fingerprints_P09VsP10OrderDistinction_ProducesDifferentFingerprintsWhenStepOrderDiffers()
    {
        ExecutableWork work = new(
            1,
            [
                new ExecutableTask("z-task", "step-1", "work z"),
                new ExecutableTask("a-task", "step-2", "work a")
            ],
            [],
            [],
            [],
            []);

        WorkflowState created = WorkflowStateMachine.Create(work);

        string p09Fp = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(created);
        string p10Fp = WorkflowGateCoordinator.ComputeBaseStateFingerprint(created);

        Assert.NotEqual(p09Fp, p10Fp);
    }

    [Fact]
    public async Task ExportAsync_MismatchedCheckpointBaseStateFingerprint_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        // Tamper with started record's base state fingerprint in file
        string recFile = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string content = File.ReadAllText(recFile);
        content = content.Replace(journal.StartedRecord.BaseWorkflowStateFingerprint, ValidHash5, StringComparison.Ordinal);
        WriteFileNoBom(recFile, content);

        WorkflowEvidencePackExporter exporter = new(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task ExportAsync_MismatchedGateBaseStateFingerprint_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        // Tamper with challenge record's base state fingerprint in file
        string recFile = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string content = File.ReadAllText(recFile);
        content = content.Replace(journal.ChallengeRecord.BaseStateFingerprint, ValidHash5, StringComparison.Ordinal);
        WriteFileNoBom(recFile, content);

        WorkflowEvidencePackExporter exporter = new(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task ExportAsync_MismatchedGateId_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        // Tamper with challenge record's GateId in file
        string recFile = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string content = File.ReadAllText(recFile);
        content = content.Replace(journal.ChallengeRecord.GateId, ValidHash5, StringComparison.Ordinal);
        WriteFileNoBom(recFile, content);

        WorkflowEvidencePackExporter exporter = new(store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    // =========================================================================
    // Category 9: Atomic Publication & Idempotence (Tests 69-76)
    // =========================================================================

    [Fact]
    public async Task AtomicPublication_TempDirectoryFlushedAndRenamed()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.True(Directory.Exists(pack.PackDirectory));
        Assert.True(File.Exists(pack.ManifestPath));
    }

    [Fact]
    public async Task AtomicPublication_PublishesToDeterministicStorageLayout()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string expectedSubPath = Path.Combine("evidence-packs", "00000000000000000001", pack.PackId);
        Assert.Contains(expectedSubPath, pack.PackDirectory, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Idempotence_ReexportIdenticalPack_ReturnsExistingPackWithoutMutation()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack1 = await exporter.ExportAsync();
        DateTime writeTime1 = File.GetLastWriteTimeUtc(pack1.ManifestPath);

        await Task.Delay(50);

        WorkflowEvidencePack pack2 = await exporter.ExportAsync();
        DateTime writeTime2 = File.GetLastWriteTimeUtc(pack2.ManifestPath);

        Assert.Equal(pack1.PackId, pack2.PackId);
        Assert.Equal(writeTime1, writeTime2);
    }

    [Fact]
    public async Task Idempotence_ReexportIdenticalPack_ManifestFileUnchanged()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack1 = await exporter.ExportAsync();
        byte[] bytes1 = File.ReadAllBytes(pack1.ManifestPath);

        WorkflowEvidencePack pack2 = await exporter.ExportAsync();
        byte[] bytes2 = File.ReadAllBytes(pack2.ManifestPath);

        Assert.Equal(bytes1, bytes2);
    }

    [Fact]
    public async Task DestinationCollision_CorruptExistingDestination_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Corrupt destination manifest
        WriteFileNoBom(pack.ManifestPath, "{ invalid json }");

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task DestinationCollision_NonIdenticalManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Tamper with manifest content to simulate collision with different pack
        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"workflowStatus\":\"Created\"", "\"workflowStatus\":\"Completed\"", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task DestinationCollision_MissingManifestInDestinationDirectory_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        File.Delete(pack.ManifestPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task CurrentRevisionDrift_DuringExport_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        exporter.OnBeforePublicationForTest = () =>
        {
            WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
            store.Append(running, 1);
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());

        // Verify that no orphan temporary directories remain
        string workflowsDir = Path.Combine(store.StorageRoot, "workflows");
        if (Directory.Exists(workflowsDir))
        {
            string[] tmpDirs = Directory.GetDirectories(workflowsDir, "*.tmp", SearchOption.AllDirectories);
            Assert.Empty(tmpDirs);
        }
    }

    [Fact]
    public async Task SourceJournalDrift_DuringExport_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        exporter.OnBeforePublicationForTest = () =>
        {
            journal.AppendRecord(new InvocationIntentRecord
            {
                SchemaId = WorkflowCheckpointJournal.SchemaId,
                SchemaVersion = WorkflowCheckpointJournal.CurrentSchemaVersion,
                Sequence = 2,
                Kind = JournalRecordKind.InvocationIntent,
                OperationOrdinal = 1,
                OperationId = "op-1",
                OperationKind = "Agent",
                InvocationGeneration = 1,
                RequestFingerprint = ValidHash1,
                ExternalIdentity = "agent-1"
            });
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task SourceContentModified_DuringExport_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        exporter.OnBeforePublicationForTest = () =>
        {
            string recPath = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
            byte[] bytes = File.ReadAllBytes(recPath);
            bytes[bytes.Length - 1] = (byte)(bytes[bytes.Length - 1] ^ 0xFF);
            File.WriteAllBytes(recPath, bytes);
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task PersistenceRecordCorrupted_DuringExport_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        exporter.OnBeforePublicationForTest = () =>
        {
            string encodedWf = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
            string recPath = Path.Combine(store.StorageRoot, "workflows", encodedWf, "records", "00000000000000000001.json");
            byte[] bytes = File.ReadAllBytes(recPath);
            bytes[bytes.Length - 1] = (byte)(bytes[bytes.Length - 1] ^ 0xFF);
            File.WriteAllBytes(recPath, bytes);
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task TemporaryDirectoryOwnership_PreservesPreexistingSiblingFiles()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        string encodedWf = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string revDir = Path.Combine(store.StorageRoot, "workflows", encodedWf, "evidence-packs", "00000000000000000001");
        Directory.CreateDirectory(revDir);

        string unrelatedDir = Path.Combine(revDir, ".unrelated-existing-staging.tmp");
        Directory.CreateDirectory(unrelatedDir);
        string unrelatedFile = Path.Combine(unrelatedDir, "data.txt");
        WriteFileNoBom(unrelatedFile, "important-preexisting-content");

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.True(Directory.Exists(unrelatedDir));
        Assert.True(File.Exists(unrelatedFile));
        Assert.Equal("important-preexisting-content", File.ReadAllText(unrelatedFile));
    }

    [Fact]
    public async Task ConcurrentExports_IdenticalWorkflow_BothSucceedIdempotently()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter1 = new(store);
        WorkflowEvidencePackExporter exporter2 = new(store);

        Task<WorkflowEvidencePack> t1 = exporter1.ExportAsync();
        Task<WorkflowEvidencePack> t2 = exporter2.ExportAsync();

        WorkflowEvidencePack[] packs = await Task.WhenAll(t1, t2);

        Assert.Equal(packs[0].PackId, packs[1].PackId);
        Assert.Equal(packs[0].ManifestFingerprint, packs[1].ManifestFingerprint);

        string packsDir = Path.Combine(store.StorageRoot, "workflows");
        string[] tmpDirs = Directory.GetDirectories(packsDir, "*.tmp", SearchOption.AllDirectories);
        Assert.Empty(tmpDirs);
    }

    // =========================================================================
    // Category 10: Verification - Parameter Validation (Tests 77-83)
    // =========================================================================

    [Fact]
    public async Task VerifyPackAsync_NullOrEmptyPackId_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, null!, ValidHash1));
        await Assert.ThrowsAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, "", ValidHash1));
        await Assert.ThrowsAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, "   ", ValidHash1));
    }

    [Fact]
    public async Task VerifyPackAsync_InvalidPackIdLength_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, "short", ValidHash1));
    }

    [Fact]
    public async Task VerifyPackAsync_InvalidPackIdCharacters_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string invalidHex = new string('Z', 64);
        await Assert.ThrowsAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, invalidHex, ValidHash1));
    }

    [Fact]
    public async Task VerifyPackAsync_NullOrEmptyExpectedFingerprint_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, ValidHash1, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, ValidHash1, ""));
    }

    [Fact]
    public async Task VerifyPackAsync_InvalidExpectedFingerprintLength_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<ArgumentException>(() => exporter.VerifyPackAsync(1, ValidHash1, "too-short"));
    }

    [Fact]
    public async Task VerifyPackAsync_NonExistentPackDirectory_ThrowsDirectoryNotFoundException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            exporter.VerifyPackAsync(1, ValidHash1, ValidHash2));
    }

    [Fact]
    public async Task VerifyPackAsync_MissingManifestJson_ThrowsFileNotFoundException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        File.Delete(pack.ManifestPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
    }

    // =========================================================================
    // Category 11: Verification - Manifest & Trust Anchor Integrity (Tests 84-91)
    // =========================================================================

    [Fact]
    public async Task VerifyPackAsync_MismatchedManifestFingerprint_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, ValidHash5));
    }

    [Fact]
    public async Task VerifyPackAsync_ManifestWithUtf8Bom_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        byte[] original = File.ReadAllBytes(pack.ManifestPath);
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. original];
        File.WriteAllBytes(pack.ManifestPath, withBom);

        string newFp = Convert.ToHexString(SHA256.HashData(withBom)).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_MismatchedPackIdInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace($"\"{pack.PackId}\"", $"\"{ValidHash5}\"", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_MismatchedCapturedRevisionInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"capturedPersistenceRevision\":1", "\"capturedPersistenceRevision\":99", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_MismatchedWorkflowIdInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace($"\"{pack.WorkflowId.Value}\"", "\"other-workflow-id\"", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_InvalidSchemaIdInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("ai.repokit.workflow-evidence-manifest", "invalid.schema", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_InvalidSchemaVersionInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":999", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_InvalidAlgorithmIdInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("ai.repokit.workflow-evidence-pack/v1", "unsupported/v2", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task CanonicalValidation_UnexpectedPropertyInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"packId\":", "\"unexpectedProperty\":\"evil\",\"packId\":", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task CanonicalValidation_NonCanonicalPropertyOrder_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        string oldHead = "\"schemaId\":\"ai.repokit.workflow-evidence-manifest\",\"schemaVersion\":1";
        string newHead = "\"schemaVersion\":1,\"schemaId\":\"ai.repokit.workflow-evidence-manifest\"";
        json = json.Replace(oldHead, newHead, StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    // =========================================================================
    // Category 12: Historical Verification & Later Appends (Tests 92-97)
    // =========================================================================

    [Fact]
    public async Task HistoricalVerification_ValidPack_VerifiedSuccessfullyAfterRestart()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack exported = await exporter.ExportAsync();

        // Restart with new exporter instance
        WorkflowEvidencePackExporter newExporter = new(store);
        WorkflowEvidencePack verified = await newExporter.VerifyPackAsync(
            exported.CapturedPersistenceRevision,
            exported.PackId,
            exported.ManifestFingerprint);

        Assert.Equal(exported.PackId, verified.PackId);
        Assert.Equal(exported.ManifestFingerprint, verified.ManifestFingerprint);
    }

    [Fact]
    public async Task HistoricalVerification_ValidPack_VerifiedSuccessfullyAfterNewPersistenceRevisionAppended()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack packAtRev1 = await exporter.ExportAsync();

        // Append new revisions to store
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowState stepRunning = WorkflowStateMachine.TransitionStep(running, "task-1", WorkflowStepStatus.Running);
        store.Append(stepRunning, 2);

        // Verification of packAtRev1 must still succeed!
        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(
            packAtRev1.CapturedPersistenceRevision,
            packAtRev1.PackId,
            packAtRev1.ManifestFingerprint);

        Assert.Equal(packAtRev1.PackId, verified.PackId);
        Assert.Equal(1, verified.CapturedPersistenceRevision);
    }

    [Fact]
    public async Task HistoricalVerification_ValidPack_VerifiedSuccessfullyAfterNewCheckpointJournalAdded()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack packBeforeJournal = await exporter.ExportAsync();

        // Append new revision and journal later
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        this.AddSampleCheckpointJournal(store, running, "task-1", 2);

        // Verification of original pack must still succeed!
        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(
            packBeforeJournal.CapturedPersistenceRevision,
            packBeforeJournal.PackId,
            packBeforeJournal.ManifestFingerprint);

        Assert.Equal(packBeforeJournal.PackId, verified.PackId);
    }

    [Fact]
    public async Task HistoricalVerification_ValidPack_VerifiedSuccessfullyAfterNewGateJournalAdded()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack packBeforeGate = await exporter.ExportAsync();

        // Append new revision and gate journal later
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        this.AddSampleGateJournal(store, running, "task-1", 2);

        // Verification of original pack must still succeed!
        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(
            packBeforeGate.CapturedPersistenceRevision,
            packBeforeGate.PackId,
            packBeforeGate.ManifestFingerprint);

        Assert.Equal(packBeforeGate.PackId, verified.PackId);
    }

    [Fact]
    public async Task HistoricalVerification_BoundExclusivelyToCapturedSources()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Adding an untracked file to storage root does not invalidate verification
        string dummyFile = Path.Combine(store.StorageRoot, "dummy-untracked.txt");
        File.WriteAllText(dummyFile, "dummy");

        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(
            pack.CapturedPersistenceRevision,
            pack.PackId,
            pack.ManifestFingerprint);

        Assert.Equal(pack.PackId, verified.PackId);
    }

    [Fact]
    public async Task HistoricalVerification_VerifyFromDescriptor_ReturnsIdenticalPack()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(pack);

        Assert.Equal(pack.PackId, verified.PackId);
        Assert.Equal(pack.ManifestFingerprint, verified.ManifestFingerprint);
    }

    [Fact]
    public async Task HistoricalVerification_AppendRecordToExistingJournalRoot_OriginalPackStillVerifies()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack exported = await exporter.ExportAsync();

        // Later append to the journal directory on disk (e.g. sequence 2)
        journal.AppendRecord(new InvocationIntentRecord
        {
            SchemaId = WorkflowCheckpointJournal.SchemaId,
            SchemaVersion = WorkflowCheckpointJournal.CurrentSchemaVersion,
            Sequence = 2,
            Kind = JournalRecordKind.InvocationIntent,
            OperationOrdinal = 1,
            OperationId = "op-later",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            ExternalIdentity = "agent-1"
        });

        // The original exported pack only captured sequence 1.
        // Historical verification MUST NOT read sequence 2 and must verify successfully!
        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(
            exported.CapturedPersistenceRevision,
            exported.PackId,
            exported.ManifestFingerprint);

        Assert.Equal(exported.PackId, verified.PackId);
    }

    [Fact]
    public async Task VerifyPackAsync_TamperedGateAuthorizedClaim_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournalWithGrant(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Tamper authorized from true to false in manifest
        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"authorized\":true", "\"authorized\":false", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_TamperedGatePrincipalIdClaim_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournalWithGrant(store, created, "task-1", 1, principalId_: "admin-1");
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Tamper principalId in manifest
        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"principalId\":\"admin-1\"", "\"principalId\":\"attacker-compromise\"", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_TamperedCheckpointOutcomeDigest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournalWithOutcome(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Tamper agentOutputDigest in manifest
        WorkflowJournalRecordEvidence outcomeRec = pack.Manifest.Tasks[0].CheckpointJournals[0].Records[2];
        Assert.NotNull(outcomeRec.AgentOutputDigest);

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace($"\"{outcomeRec.AgentOutputDigest}\"", $"\"{ValidHash5}\"", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_TamperedCheckpointAttemptCount_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournalWithOutcome(store, created, "task-1", 1, attemptCount_: 1);
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace("\"attemptCount\":1", "\"attemptCount\":99", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task VerifyPackAsync_FabricatedCheckpointJournalInManifest_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Inject fabricated checkpoint journal in manifest tasks
        string json = File.ReadAllText(pack.ManifestPath);
        string fakeJournal = $"{{\"basePersistenceRevision\":99,\"baseWorkflowStateFingerprint\":\"{ValidHash1}\",\"inputFingerprint\":\"{ValidHash2}\",\"registryFingerprint\":\"{ValidHash3}\",\"executionCompleted\":false,\"projectionCompleted\":false,\"records\":[]}}";
        json = json.Replace("\"checkpointJournals\":[]", $"\"checkpointJournals\":[{fakeJournal}]", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    // =========================================================================
    // Category 13: Source File Corruption & Security Boundaries (Tests 98-106)
    // =========================================================================

    [Fact]
    public async Task Corruption_ModifiedPersistenceRecordBytes_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Corrupt persistence record on disk
        string recPath = Path.Combine(store.StorageRoot, pack.Manifest.SourceRecords[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes = File.ReadAllBytes(recPath);
        bytes[bytes.Length - 1] = (byte) (bytes[bytes.Length - 1] ^ 0xFF);
        File.WriteAllBytes(recPath, bytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
    }

    [Fact]
    public async Task Corruption_ModifiedCheckpointJournalBytes_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Corrupt journal record on disk
        string recPath = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        byte[] bytes = File.ReadAllBytes(recPath);
        bytes[bytes.Length - 1] = (byte) (bytes[bytes.Length - 1] ^ 0xFF);
        File.WriteAllBytes(recPath, bytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
    }

    [Fact]
    public async Task Corruption_ModifiedGateJournalBytes_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Corrupt gate journal record on disk
        string recPath = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        byte[] bytes = File.ReadAllBytes(recPath);
        bytes[bytes.Length - 1] = (byte) (bytes[bytes.Length - 1] ^ 0xFF);
        File.WriteAllBytes(recPath, bytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
    }

    [Fact]
    public async Task Corruption_DeletedPersistenceRecord_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string recPath = Path.Combine(store.StorageRoot, pack.Manifest.SourceRecords[0].RelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(recPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
    }

    [Fact]
    public async Task Corruption_DeletedJournalRecord_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string recPath = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        File.Delete(recPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
    }

    [Fact]
    public async Task Corruption_TamperedSourceByteLength_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Tamper with byteLength in manifest
        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace($"\"byteLength\":{pack.Manifest.SourceRecords[0].ByteLength}", "\"byteLength\":99999", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task Security_PathTraversalInPackId_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            exporter.VerifyPackAsync(1, "../../../../../../../../../../../../../../../etc/passwd", ValidHash1));
    }

    [Fact]
    public async Task Security_NonCanonicalRelativePathInSourceRecord_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath);
        json = json.Replace(pack.Manifest.SourceRecords[0].RelativePath, "workflows/../outside.json", StringComparison.Ordinal);
        WriteFileNoBom(pack.ManifestPath, json);
        string newFp = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pack.ManifestPath))).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFp));
    }

    [Fact]
    public async Task Security_IntermediateAncestorReparsePoint_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string outsideTarget = Path.Combine(Path.GetTempPath(), "outside-ancestor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);

        string revisionDir = Directory.GetParent(pack.PackDirectory)!.FullName;
        string evidencePacksDir = Directory.GetParent(revisionDir)!.FullName;
        string outsideDir = Path.Combine(outsideTarget, "evidence-packs");

        try
        {
            Directory.Move(evidencePacksDir, outsideDir);

            // Create reparse point at evidencePacksDir pointing to outside target
            bool created = TryCreateReparsePoint(evidencePacksDir, outsideDir);
            if (!created)
            {
                throw Xunit.Sdk.SkipException.ForSkip("Platform cannot create symlink or junction in test environment.");
            }

            // VerifyPackAsync must detect the intermediate ancestor reparse point and throw
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
        }
        finally
        {
            if (Directory.Exists(evidencePacksDir))
            {
                try { Directory.Delete(evidencePacksDir); } catch { }
            }
            if (Directory.Exists(outsideTarget))
            {
                try { Directory.Delete(outsideTarget, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Security_IntermediateReparsePointInVerify_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string outsideTarget = Path.Combine(Path.GetTempPath(), "outside-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);

        try
        {
            string outsidePackDir = Path.Combine(outsideTarget, pack.PackId);
            Directory.Move(pack.PackDirectory, outsidePackDir);

            // Create reparse point at pack directory location pointing to outside
            bool created = TryCreateReparsePoint(pack.PackDirectory, outsidePackDir);
            if (!created)
            {
                throw Xunit.Sdk.SkipException.ForSkip("Platform cannot create symlink or junction in test environment.");
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));
        }
        finally
        {
            if (Directory.Exists(pack.PackDirectory))
            {
                try { Directory.Delete(pack.PackDirectory); } catch { }
            }
            if (Directory.Exists(outsideTarget))
            {
                try { Directory.Delete(outsideTarget, recursive: true); } catch { }
            }
        }
    }

    // =========================================================================
    // Category 14: Multi-Task, Multiple Journals & Immutability (Tests 107-112)
    // =========================================================================

    [Fact]
    public async Task MultiTask_MultipleTasksWithIndependentJournals_ExportedAndVerified()
    {
        ExecutableWork work = new(
            1,
            [
                new ExecutableTask("task-1", "step-1", "work 1"),
                new ExecutableTask("task-2", "step-2", "work 2")
            ],
            [],
            [],
            [],
            []);

        WorkflowPersistenceStore store = new(this._tempDirectory, new WorkflowId("wf-2task"));
        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        this.AddSampleGateJournal(store, created, "task-2", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.Equal(2, pack.Manifest.Tasks.Count);
        Assert.Single(pack.Manifest.Tasks[0].CheckpointJournals);
        Assert.Empty(pack.Manifest.Tasks[0].GateJournals);
        Assert.Empty(pack.Manifest.Tasks[1].CheckpointJournals);
        Assert.Single(pack.Manifest.Tasks[1].GateJournals);

        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(pack);
        Assert.Equal(pack.PackId, verified.PackId);
    }

    [Fact]
    public async Task MultipleJournals_SameTaskAcrossMultipleRevisions_PreservedInAscendingOrder()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);

        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        this.AddSampleCheckpointJournal(store, running, "task-1", 2);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowTaskEvidence task = Assert.Single(pack.Manifest.Tasks);
        Assert.Equal(2, task.CheckpointJournals.Count);
        Assert.Equal(1, task.CheckpointJournals[0].BasePersistenceRevision);
        Assert.Equal(2, task.CheckpointJournals[1].BasePersistenceRevision);
    }

    [Fact]
    public async Task Immutability_SourceRecordsCollection_IsReadOnly()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.IsAssignableFrom<IReadOnlyList<WorkflowEvidenceSourceRecord>>(pack.Manifest.SourceRecords);
    }

    [Fact]
    public async Task Immutability_EventsCollection_IsReadOnly()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.IsAssignableFrom<IReadOnlyList<WorkflowEvidenceEvent>>(pack.Manifest.Events);
    }

    [Fact]
    public async Task Immutability_TasksCollection_IsReadOnly()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.IsAssignableFrom<IReadOnlyList<WorkflowTaskEvidence>>(pack.Manifest.Tasks);
    }

    [Fact]
    public async Task Immutability_CheckpointAndGateJournals_AreReadOnly()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        WorkflowTaskEvidence task = Assert.Single(pack.Manifest.Tasks);
        Assert.IsAssignableFrom<IReadOnlyList<WorkflowCheckpointJournalEvidence>>(task.CheckpointJournals);
        Assert.IsAssignableFrom<IReadOnlyList<WorkflowGateJournalEvidence>>(task.GateJournals);
        Assert.IsAssignableFrom<IReadOnlyList<WorkflowJournalRecordEvidence>>(task.CheckpointJournals[0].Records);
    }

    // =========================================================================
    // Category 15: Recovery II Independent-Review Regressions
    // =========================================================================

    [Fact]
    public async Task ExportAsync_GateJournalGrantedMismatchGateId_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);
        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            SchemaId = WorkflowGateJournal.SchemaId,
            SchemaVersion = WorkflowGateJournal.CurrentSchemaVersion,
            Sequence = 2,
            GateId = "mismatched-gate-id",
            PrincipalId = "admin-1",
            MechanismId = "cli-token",
            EvidenceFingerprint = ValidHash4
        });

        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task ExportAsync_GateJournalDeniedMismatchGateId_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);
        journal.AppendRecord(new AuthorizationDeniedRecord
        {
            SchemaId = WorkflowGateJournal.SchemaId,
            SchemaVersion = WorkflowGateJournal.CurrentSchemaVersion,
            Sequence = 2,
            GateId = "mismatched-gate-id",
            PrincipalId = "admin-1",
            MechanismId = "cli-token",
            EvidenceFingerprint = ValidHash4
        });

        WorkflowEvidencePackExporter exporter = new(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
    }

    [Fact]
    public async Task VerifyPackAsync_GateJournalDecisionMismatchGateId_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowGateJournal journal = this.AddSampleGateJournal(store, created, "task-1", 1);
        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            SchemaId = WorkflowGateJournal.SchemaId,
            SchemaVersion = WorkflowGateJournal.CurrentSchemaVersion,
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = "admin-1",
            MechanismId = "cli-token",
            EvidenceFingerprint = ValidHash4
        });

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Mutate the second record (decision record) in the gate records directory
        string recordsDir = WorkflowGateJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, "task-1", 1);
        string record2Path = Path.Combine(recordsDir, 2L.ToString("D20") + ".json");
        string record2Content = File.ReadAllText(record2Path, Utf8NoBom);
        string mutatedRecord2Content = record2Content.Replace(
            journal.ChallengeRecord.GateId,
            "mismatched-gate-id",
            StringComparison.Ordinal);
        File.WriteAllText(record2Path, mutatedRecord2Content, Utf8NoBom);

        byte[] mutatedGateRecordBytes = File.ReadAllBytes(record2Path);
        string mutatedSha = Convert.ToHexString(SHA256.HashData(mutatedGateRecordBytes)).ToLowerInvariant();
        long mutatedLen = mutatedGateRecordBytes.Length;

        string targetRelativePath = Path.GetRelativePath(store.StorageRoot, record2Path).Replace('\\', '/');

        List<WorkflowEvidenceSourceRecord> updatedSourceRecords = new();
        foreach (WorkflowEvidenceSourceRecord item in pack.Manifest.SourceRecords)
        {
            if (string.Equals(item.RelativePath.Replace('\\', '/'), targetRelativePath, StringComparison.Ordinal))
            {
                updatedSourceRecords.Add(new WorkflowEvidenceSourceRecord(item.RelativePath, mutatedLen, mutatedSha));
            }
            else
            {
                updatedSourceRecords.Add(item);
            }
        }

        string updatedSourceSetFingerprint =
            WorkflowEvidenceManifestJson.ComputeSourceSetFingerprint(updatedSourceRecords);

        string updatedPackId =
            WorkflowEvidenceManifestJson.ComputePackId(
                pack.Manifest.AlgorithmId,
                pack.Manifest.SchemaId,
                pack.Manifest.SchemaVersion,
                pack.Manifest.WorkflowId.Value,
                pack.Manifest.SourceImplementationPlanRevision,
                pack.Manifest.CapturedPersistenceRevision,
                updatedSourceSetFingerprint);

        string updatedPackDirectory = Path.Combine(
            Path.GetDirectoryName(pack.PackDirectory)!,
            updatedPackId);

        if (!string.Equals(pack.PackDirectory, updatedPackDirectory, StringComparison.Ordinal))
        {
            Directory.Move(pack.PackDirectory, updatedPackDirectory);
        }

        WorkflowEvidenceManifest updatedManifest = new(
            pack.Manifest.SchemaId,
            pack.Manifest.SchemaVersion,
            pack.Manifest.AlgorithmId,
            updatedPackId,
            pack.Manifest.WorkflowId,
            pack.Manifest.SourceImplementationPlanRevision,
            pack.Manifest.CapturedPersistenceRevision,
            pack.Manifest.WorkflowStatus,
            updatedSourceSetFingerprint,
            updatedSourceRecords,
            pack.Manifest.Events,
            pack.Manifest.Tasks);

        byte[] updatedManifestBytes = WorkflowEvidenceManifestJson.Serialize(updatedManifest);
        string updatedManifestPath = Path.Combine(updatedPackDirectory, "manifest.json");
        File.WriteAllBytes(updatedManifestPath, updatedManifestBytes);
        string newFingerprint = Convert.ToHexString(SHA256.HashData(updatedManifestBytes)).ToLowerInvariant();

        // Verification must fail closed because decision record GateId != challenge GateId, even though source hashes, packId, and manifest fingerprint match
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, updatedPackId, newFingerprint));

        Assert.Contains("Gate grant record GateId 'mismatched-gate-id' does not match challenge GateId", ex.Message);
    }

    [Fact]
    public async Task VerifyPackAsync_ManifestEventWorkflowIdMismatch_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        List<WorkflowEvidenceEvent> mutatedEvents = new(pack.Manifest.Events);
        WorkflowEvidenceEvent orig = mutatedEvents[0];
        mutatedEvents[0] = new WorkflowEvidenceEvent(
            new WorkflowId("mismatched-workflow-id"),
            orig.Revision,
            orig.Kind,
            orig.TaskId,
            orig.PreviousWorkflowStatus,
            orig.TargetWorkflowStatus,
            orig.PreviousStepStatus,
            orig.TargetStepStatus);

        WorkflowEvidenceManifest mutatedManifest = new(
            pack.Manifest.SchemaId,
            pack.Manifest.SchemaVersion,
            pack.Manifest.AlgorithmId,
            pack.Manifest.PackId,
            pack.Manifest.WorkflowId,
            pack.Manifest.SourceImplementationPlanRevision,
            pack.Manifest.CapturedPersistenceRevision,
            pack.Manifest.WorkflowStatus,
            pack.Manifest.SourceSetFingerprint,
            pack.Manifest.SourceRecords,
            mutatedEvents,
            pack.Manifest.Tasks);

        byte[] mutatedBytes = WorkflowEvidenceManifestJson.Serialize(mutatedManifest);
        File.WriteAllBytes(pack.ManifestPath, mutatedBytes);
        string newFingerprint = Convert.ToHexString(SHA256.HashData(mutatedBytes)).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFingerprint));
    }

    [Fact]
    public async Task VerifyPackAsync_ManifestEventSequenceMismatch_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        List<WorkflowEvidenceEvent> mutatedEvents = new(pack.Manifest.Events);
        WorkflowEvidenceEvent orig = mutatedEvents[0];
        mutatedEvents[0] = new WorkflowEvidenceEvent(
            orig.WorkflowId,
            orig.Revision + 10,
            orig.Kind,
            orig.TaskId,
            orig.PreviousWorkflowStatus,
            orig.TargetWorkflowStatus,
            orig.PreviousStepStatus,
            orig.TargetStepStatus);

        WorkflowEvidenceManifest mutatedManifest = new(
            pack.Manifest.SchemaId,
            pack.Manifest.SchemaVersion,
            pack.Manifest.AlgorithmId,
            pack.Manifest.PackId,
            pack.Manifest.WorkflowId,
            pack.Manifest.SourceImplementationPlanRevision,
            pack.Manifest.CapturedPersistenceRevision,
            pack.Manifest.WorkflowStatus,
            pack.Manifest.SourceSetFingerprint,
            pack.Manifest.SourceRecords,
            mutatedEvents,
            pack.Manifest.Tasks);

        byte[] mutatedBytes = WorkflowEvidenceManifestJson.Serialize(mutatedManifest);
        File.WriteAllBytes(pack.ManifestPath, mutatedBytes);
        string newFingerprint = Convert.ToHexString(SHA256.HashData(mutatedBytes)).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, newFingerprint));
    }

    [Fact]
    public async Task Security_ExportTimeJournalReparsePoint_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string outsideTarget = Path.Combine(Path.GetTempPath(), "outside-export-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);

        string cpCheckpointDir = WorkflowCheckpointJournal.GetCheckpointDirectory(store.StorageRoot, store.WorkflowId, "task-1", 1);

        try
        {
            CopyDirectoryRecursively(cpCheckpointDir, outsideTarget);
            Directory.Delete(cpCheckpointDir, recursive: true);

            bool createdReparse = TryCreateReparsePoint(cpCheckpointDir, outsideTarget);
            if (!createdReparse)
            {
                throw Xunit.Sdk.SkipException.ForSkip("Platform cannot create symlink or junction in test environment.");
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        }
        finally
        {
            if (Directory.Exists(cpCheckpointDir))
            {
                try { Directory.Delete(cpCheckpointDir); } catch { }
            }
            if (Directory.Exists(outsideTarget))
            {
                try { Directory.Delete(outsideTarget, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Security_PrepublicationDriftReparsePointRedirect_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string outsideTarget = Path.Combine(Path.GetTempPath(), "outside-drift-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideTarget);

        string cpCheckpointDir = WorkflowCheckpointJournal.GetCheckpointDirectory(store.StorageRoot, store.WorkflowId, "task-1", 1);

        exporter.OnBeforePublicationForTest = () =>
        {
            CopyDirectoryRecursively(cpCheckpointDir, outsideTarget);
            Directory.Delete(cpCheckpointDir, recursive: true);

            bool createdReparse = TryCreateReparsePoint(cpCheckpointDir, outsideTarget);
            if (!createdReparse)
            {
                throw Xunit.Sdk.SkipException.ForSkip("Platform cannot create symlink or junction in test environment.");
            }
            return Task.CompletedTask;
        };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        }
        finally
        {
            if (Directory.Exists(cpCheckpointDir))
            {
                try { Directory.Delete(cpCheckpointDir); } catch { }
            }
            if (Directory.Exists(outsideTarget))
            {
                try { Directory.Delete(outsideTarget, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ExportAsync_StagingDirectoryPreExistingCollision_ThrowsAndPreservesDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "collision-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        Directory.CreateDirectory(parentDir);

        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        Directory.CreateDirectory(stagingDir);
        string canaryFile = Path.Combine(stagingDir, "canary.txt");
        File.WriteAllText(canaryFile, "pre-existing data");

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());

        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(canaryFile));
        Assert.Equal("pre-existing data", File.ReadAllText(canaryFile));
    }

    [Fact]
    public async Task ExportAsync_StagingDirectoryReplacedDuringExport_ThrowsAndPreservesReplacedDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "replaced-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string foreignCanary = Path.Combine(stagingDir, "foreign.txt");

        exporter.OnBeforePublicationForTest = () =>
        {
            string ownerTokenPath = Path.Combine(stagingDir, ".staging-owner");
            if (File.Exists(ownerTokenPath))
            {
                File.Delete(ownerTokenPath);
            }
            File.WriteAllText(foreignCanary, "foreign actor data");
            return Task.CompletedTask;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());

        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(foreignCanary));
        Assert.Equal("foreign actor data", File.ReadAllText(foreignCanary));
    }

    [Fact]
    public async Task VerifyPackAsync_EquivalentManifestWithNonCanonicalWhitespace_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath, Utf8NoBom);
        string nonCanonicalJson = json.Replace("\"schemaId\":", "\"schemaId\": ", StringComparison.Ordinal);
        byte[] nonCanonicalBytes = Utf8NoBom.GetBytes(nonCanonicalJson);
        File.WriteAllBytes(pack.ManifestPath, nonCanonicalBytes);

        string recomputedFingerprint = Convert.ToHexString(SHA256.HashData(nonCanonicalBytes)).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, recomputedFingerprint));
    }

    [Fact]
    public async Task VerifyPackAsync_EquivalentManifestWithNonCanonicalEscaping_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string json = File.ReadAllText(pack.ManifestPath, Utf8NoBom);
        string nonCanonicalJson = json.Replace("\"task-1\"", "\"\\u0074ask-1\"", StringComparison.Ordinal);
        byte[] nonCanonicalBytes = Utf8NoBom.GetBytes(nonCanonicalJson);
        File.WriteAllBytes(pack.ManifestPath, nonCanonicalBytes);

        string recomputedFingerprint = Convert.ToHexString(SHA256.HashData(nonCanonicalBytes)).ToLowerInvariant();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, recomputedFingerprint));
    }

    [Fact]
    public async Task ExportAsync_CheckpointJournalCorruptedWorkflowId_ThrowsAndRejectsBeforePublication()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string taskIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes("task-1")).ToLowerInvariant();
        string rec1Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "execution-checkpoints", taskIdHex, 1L.ToString("D20"), "records", 1L.ToString("D20") + ".json");

        string content = File.ReadAllText(rec1Path, Utf8NoBom);
        string corruptedContent = content.Replace($"\"{store.WorkflowId.Value}\"", "\"corrupted-workflow-id\"", StringComparison.Ordinal);
        File.WriteAllText(rec1Path, corruptedContent, Utf8NoBom);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Checkpoint journal workflowId 'corrupted-workflow-id' does not match expected", ex.Message);

        string packBase = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs");
        Assert.False(Directory.Exists(packBase) && Directory.EnumerateFileSystemEntries(packBase).Any());
    }

    [Fact]
    public async Task ExportAsync_CheckpointJournalCorruptedTaskId_ThrowsAndRejectsBeforePublication()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string taskIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes("task-1")).ToLowerInvariant();
        string rec1Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "execution-checkpoints", taskIdHex, 1L.ToString("D20"), "records", 1L.ToString("D20") + ".json");

        string content = File.ReadAllText(rec1Path, Utf8NoBom);
        string corruptedContent = content.Replace("\"task-1\"", "\"corrupted-task-id\"", StringComparison.Ordinal);
        File.WriteAllText(rec1Path, corruptedContent, Utf8NoBom);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Checkpoint journal taskId 'corrupted-task-id' does not match expected", ex.Message);

        string packBase = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs");
        Assert.False(Directory.Exists(packBase) && Directory.EnumerateFileSystemEntries(packBase).Any());
    }

    [Fact]
    public async Task ExportAsync_CheckpointJournalCorruptedBaseRevision_ThrowsAndRejectsBeforePublication()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string taskIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes("task-1")).ToLowerInvariant();
        string rec1Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "execution-checkpoints", taskIdHex, 1L.ToString("D20"), "records", 1L.ToString("D20") + ".json");

        string content = File.ReadAllText(rec1Path, Utf8NoBom);
        string corruptedContent = content.Replace("\"basePersistenceRevision\":1", "\"basePersistenceRevision\":99", StringComparison.Ordinal);
        File.WriteAllText(rec1Path, corruptedContent, Utf8NoBom);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Checkpoint journal base persistence revision 99 does not match expected 1", ex.Message);

        string packBase = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs");
        Assert.False(Directory.Exists(packBase) && Directory.EnumerateFileSystemEntries(packBase).Any());
    }

    [Fact]
    public async Task ExportAsync_GateJournalCorruptedWorkflowId_ThrowsAndRejectsBeforePublication()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string taskIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes("task-1")).ToLowerInvariant();
        string rec1Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "human-gates", taskIdHex, 1L.ToString("D20"), "records", 1L.ToString("D20") + ".json");

        string content = File.ReadAllText(rec1Path, Utf8NoBom);
        string corruptedContent = content.Replace($"\"{store.WorkflowId.Value}\"", "\"corrupted-workflow-id\"", StringComparison.Ordinal);
        File.WriteAllText(rec1Path, corruptedContent, Utf8NoBom);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Gate challenge workflowId 'corrupted-workflow-id' does not match expected", ex.Message);

        string packBase = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs");
        Assert.False(Directory.Exists(packBase) && Directory.EnumerateFileSystemEntries(packBase).Any());
    }

    [Fact]
    public async Task ExportAsync_GateJournalCorruptedTaskId_ThrowsAndRejectsBeforePublication()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string taskIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes("task-1")).ToLowerInvariant();
        string rec1Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "human-gates", taskIdHex, 1L.ToString("D20"), "records", 1L.ToString("D20") + ".json");

        string content = File.ReadAllText(rec1Path, Utf8NoBom);
        string corruptedContent = content.Replace("\"task-1\"", "\"corrupted-task-id\"", StringComparison.Ordinal);
        File.WriteAllText(rec1Path, corruptedContent, Utf8NoBom);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Gate challenge taskId 'corrupted-task-id' does not match expected", ex.Message);

        string packBase = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs");
        Assert.False(Directory.Exists(packBase) && Directory.EnumerateFileSystemEntries(packBase).Any());
    }

    [Fact]
    public async Task ExportAsync_GateJournalCorruptedBaseRevision_ThrowsAndRejectsBeforePublication()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleGateJournal(store, created, "task-1", 1);
        WorkflowEvidencePackExporter exporter = new(store);

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string taskIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes("task-1")).ToLowerInvariant();
        string rec1Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "human-gates", taskIdHex, 1L.ToString("D20"), "records", 1L.ToString("D20") + ".json");

        string content = File.ReadAllText(rec1Path, Utf8NoBom);
        string corruptedContent = content.Replace("\"basePersistenceRevision\":1", "\"basePersistenceRevision\":99", StringComparison.Ordinal);
        File.WriteAllText(rec1Path, corruptedContent, Utf8NoBom);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Gate challenge base persistence revision 99 does not match expected 1", ex.Message);

        string packBase = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs");
        Assert.False(Directory.Exists(packBase) && Directory.EnumerateFileSystemEntries(packBase).Any());
    }

    [Fact]
    public async Task VerifyPackAsync_CryptographicallyReanchoredInvalidPersistenceTransition_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowState running = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(running, 1);

        WorkflowEvidencePackExporter exporter = new(store);
        WorkflowEvidencePack pack = await exporter.ExportAsync();

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string rec2Path = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "records", 2L.ToString("D20") + ".json");
        string rec2Content = File.ReadAllText(rec2Path, Utf8NoBom);

        // Mutate revision 2 event targetWorkflowStatus to Completed (3) while step is still Pending (1)
        // Under P01, a running workflow cannot complete until every step is completed.
        string mutatedRec2 = rec2Content.Replace(
            "\"targetWorkflowStatus\":2",
            "\"targetWorkflowStatus\":3",
            StringComparison.Ordinal);
        File.WriteAllText(rec2Path, mutatedRec2, Utf8NoBom);

        byte[] mutatedBytes = File.ReadAllBytes(rec2Path);
        string mutatedSha = Convert.ToHexString(SHA256.HashData(mutatedBytes)).ToLowerInvariant();
        long mutatedLen = mutatedBytes.Length;

        string targetRelPath = Path.GetRelativePath(store.StorageRoot, rec2Path).Replace('\\', '/');

        List<WorkflowEvidenceSourceRecord> updatedSourceRecords = [];
        foreach (WorkflowEvidenceSourceRecord item in pack.Manifest.SourceRecords)
        {
            if (string.Equals(item.RelativePath.Replace('\\', '/'), targetRelPath, StringComparison.Ordinal))
            {
                updatedSourceRecords.Add(new WorkflowEvidenceSourceRecord(item.RelativePath, mutatedLen, mutatedSha));
            }
            else
            {
                updatedSourceRecords.Add(item);
            }
        }

        string updatedSourceSetFingerprint =
            WorkflowEvidenceManifestJson.ComputeSourceSetFingerprint(updatedSourceRecords);

        string updatedPackId =
            WorkflowEvidenceManifestJson.ComputePackId(
                pack.Manifest.AlgorithmId,
                pack.Manifest.SchemaId,
                pack.Manifest.SchemaVersion,
                pack.Manifest.WorkflowId.Value,
                pack.Manifest.SourceImplementationPlanRevision,
                pack.Manifest.CapturedPersistenceRevision,
                updatedSourceSetFingerprint);

        string updatedPackDirectory = Path.Combine(
            Path.GetDirectoryName(pack.PackDirectory)!,
            updatedPackId);

        if (!string.Equals(pack.PackDirectory, updatedPackDirectory, StringComparison.Ordinal))
        {
            Directory.Move(pack.PackDirectory, updatedPackDirectory);
        }

        List<WorkflowEvidenceEvent> updatedEvents = new(pack.Manifest.Events);
        WorkflowEvidenceEvent origEv = updatedEvents[1];
        updatedEvents[1] = new WorkflowEvidenceEvent(
            origEv.WorkflowId,
            origEv.Revision,
            origEv.Kind,
            origEv.TaskId,
            origEv.PreviousWorkflowStatus,
            WorkflowStatus.Completed,
            origEv.PreviousStepStatus,
            origEv.TargetStepStatus);

        WorkflowEvidenceManifest updatedManifest = new(
            pack.Manifest.SchemaId,
            pack.Manifest.SchemaVersion,
            pack.Manifest.AlgorithmId,
            updatedPackId,
            pack.Manifest.WorkflowId,
            pack.Manifest.SourceImplementationPlanRevision,
            pack.Manifest.CapturedPersistenceRevision,
            pack.Manifest.WorkflowStatus,
            updatedSourceSetFingerprint,
            updatedSourceRecords,
            updatedEvents,
            pack.Manifest.Tasks);

        byte[] updatedManifestBytes = WorkflowEvidenceManifestJson.Serialize(updatedManifest);
        string updatedManifestPath = Path.Combine(updatedPackDirectory, "manifest.json");
        File.WriteAllBytes(updatedManifestPath, updatedManifestBytes);
        string newFingerprint = Convert.ToHexString(SHA256.HashData(updatedManifestBytes)).ToLowerInvariant();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, updatedPackId, newFingerprint));

        Assert.Contains("Persisted workflow transition is not legal under the P01 state machine", ex.Message);
    }

    [Fact]
    public async Task ExportAsync_PublishedPackNeverContainsMarkerAtVisibilityPoint()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        bool checkedBeforePublication = false;
        string? destinationPackDir = null;

        exporter.OnBeforePublicationForTest = () =>
        {
            checkedBeforePublication = true;
            return Task.CompletedTask;
        };

        WorkflowEvidencePack pack = await exporter.ExportAsync();
        destinationPackDir = pack.PackDirectory;

        Assert.True(checkedBeforePublication);
        Assert.True(Directory.Exists(destinationPackDir));
        Assert.True(File.Exists(pack.ManifestPath));

        // The final published pack directory must NEVER contain .staging-owner
        Assert.False(File.Exists(Path.Combine(destinationPackDir, ".staging-owner")));

        string[] entries = Directory.GetFileSystemEntries(destinationPackDir);
        Assert.Single(entries);
        Assert.Equal("manifest.json", Path.GetFileName(entries[0]));
    }

    [Fact]
    public async Task ExportAsync_DestinationCollisionDuringAtomicMove_CleansUpAndReturnsVerifiedPack()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        bool collisionTriggered = false;

        exporter.OnBeforePublicationForTest = async () =>
        {
            collisionTriggered = true;
            // Pre-publish via a second exporter to create destination packDirectory collision
            WorkflowEvidencePackExporter racingExporter = new(store);
            await racingExporter.ExportAsync();
        };

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        Assert.True(collisionTriggered);
        Assert.True(Directory.Exists(pack.PackDirectory));
        Assert.True(File.Exists(pack.ManifestPath));
        Assert.False(File.Exists(Path.Combine(pack.PackDirectory, ".staging-owner")));

        string parentDir = Path.GetDirectoryName(pack.PackDirectory)!;
        string[] lingeringStagingDirs = Directory.GetDirectories(parentDir, ".staging-*");
        Assert.Empty(lingeringStagingDirs);
    }

    // =========================================================================
    // Category 20: Staging Payload Integrity & Initialization Cleanup (Recovery IV Findings A & B)
    // =========================================================================

    [Fact]
    public async Task ExportAsync_StagedManifestDeletedBeforePublication_ThrowsAndLeavesNoPackDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "del-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);

        exporter.OnBeforePublicationForTest = () =>
        {
            string[] subdirs = Directory.GetDirectories(stagingDir);
            Assert.Single(subdirs);
            string payloadDir = subdirs[0];
            string stagedManifest = Path.Combine(payloadDir, "manifest.json");
            Assert.True(File.Exists(stagedManifest));
            File.Delete(stagedManifest);
            return Task.CompletedTask;
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Staging payload directory must contain exactly one entry ('manifest.json')", ex.Message);

        if (Directory.Exists(parentDir))
        {
            string[] nonStagingEntries = Directory.GetFileSystemEntries(parentDir)
                .Where(e => !Path.GetFileName(e).StartsWith(".staging-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(nonStagingEntries);
        }
    }

    [Fact]
    public async Task ExportAsync_StagedManifestBytesCorruptedBeforePublication_ThrowsAndLeavesNoPackDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "corrupt-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);

        exporter.OnBeforePublicationForTest = () =>
        {
            string[] subdirs = Directory.GetDirectories(stagingDir);
            Assert.Single(subdirs);
            string payloadDir = subdirs[0];
            string stagedManifest = Path.Combine(payloadDir, "manifest.json");
            byte[] bytes = File.ReadAllBytes(stagedManifest);
            bytes[0] ^= 0xFF;
            File.WriteAllBytes(stagedManifest, bytes);
            return Task.CompletedTask;
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Staged manifest bytes do not match expected manifest bytes", ex.Message);

        if (Directory.Exists(parentDir))
        {
            string[] nonStagingEntries = Directory.GetFileSystemEntries(parentDir)
                .Where(e => !Path.GetFileName(e).StartsWith(".staging-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(nonStagingEntries);
        }
    }

    [Fact]
    public async Task ExportAsync_StagedManifestEmptyBeforePublication_ThrowsAndLeavesNoPackDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "empty-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);

        exporter.OnBeforePublicationForTest = () =>
        {
            string[] subdirs = Directory.GetDirectories(stagingDir);
            Assert.Single(subdirs);
            string payloadDir = subdirs[0];
            string stagedManifest = Path.Combine(payloadDir, "manifest.json");
            File.WriteAllBytes(stagedManifest, Array.Empty<byte>());
            return Task.CompletedTask;
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Staged manifest file is empty", ex.Message);

        if (Directory.Exists(parentDir))
        {
            string[] nonStagingEntries = Directory.GetFileSystemEntries(parentDir)
                .Where(e => !Path.GetFileName(e).StartsWith(".staging-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(nonStagingEntries);
        }
    }

    [Fact]
    public async Task ExportAsync_StagedPayloadContainsUnexpectedFile_ThrowsAndPreservesForeignContent()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "extra-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string? payloadDirSaved = null;
        string? foreignFileSaved = null;

        exporter.OnBeforePublicationForTest = () =>
        {
            string[] subdirs = Directory.GetDirectories(stagingDir);
            Assert.Single(subdirs);
            payloadDirSaved = subdirs[0];
            foreignFileSaved = Path.Combine(payloadDirSaved, "extra-foreign.txt");
            File.WriteAllText(foreignFileSaved, "preserve-this-payload-content", Utf8NoBom);
            return Task.CompletedTask;
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Staging payload directory must contain exactly one entry ('manifest.json'), but found 2", ex.Message);

        Assert.True(Directory.Exists(stagingDir));
        Assert.NotNull(payloadDirSaved);
        Assert.True(Directory.Exists(payloadDirSaved));
        Assert.NotNull(foreignFileSaved);
        Assert.True(File.Exists(foreignFileSaved));
        Assert.Equal("preserve-this-payload-content", File.ReadAllText(foreignFileSaved, Utf8NoBom));

        if (Directory.Exists(parentDir))
        {
            string[] nonStagingEntries = Directory.GetFileSystemEntries(parentDir)
                .Where(e => !Path.GetFileName(e).StartsWith(".staging-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(nonStagingEntries);
        }
    }

    [Fact]
    public async Task ExportAsync_StagedPayloadSubstitutedFile_ThrowsAndPreservesForeignContent()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "subst-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string? foreignFileSaved = null;

        exporter.OnBeforePublicationForTest = () =>
        {
            string[] subdirs = Directory.GetDirectories(stagingDir);
            Assert.Single(subdirs);
            string payloadDir = subdirs[0];
            string stagedManifest = Path.Combine(payloadDir, "manifest.json");
            File.Delete(stagedManifest);
            foreignFileSaved = Path.Combine(payloadDir, "substituted.json");
            File.WriteAllText(foreignFileSaved, "{\"fake\":true}", Utf8NoBom);
            return Task.CompletedTask;
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Unexpected entry found in staging payload directory", ex.Message);

        Assert.True(Directory.Exists(stagingDir));
        Assert.NotNull(foreignFileSaved);
        Assert.True(File.Exists(foreignFileSaved));
        Assert.Equal("{\"fake\":true}", File.ReadAllText(foreignFileSaved, Utf8NoBom));

        if (Directory.Exists(parentDir))
        {
            string[] nonStagingEntries = Directory.GetFileSystemEntries(parentDir)
                .Where(e => !Path.GetFileName(e).StartsWith(".staging-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(nonStagingEntries);
        }
    }

    [Fact]
    public async Task ExportAsync_StagingContainerContainsUnexpectedEntry_ThrowsAndPreservesForeignContent()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "cntentry-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string foreignCanary = Path.Combine(stagingDir, "foreign-container-file.txt");

        exporter.OnBeforePublicationForTest = () =>
        {
            File.WriteAllText(foreignCanary, "foreign-container-content", Utf8NoBom);
            return Task.CompletedTask;
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Staging container must contain exactly two entries, but found 3", ex.Message);

        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(foreignCanary));
        Assert.Equal("foreign-container-content", File.ReadAllText(foreignCanary, Utf8NoBom));

        if (Directory.Exists(parentDir))
        {
            string[] nonStagingEntries = Directory.GetFileSystemEntries(parentDir)
                .Where(e => !Path.GetFileName(e).StartsWith(".staging-", StringComparison.Ordinal))
                .ToArray();
            Assert.Empty(nonStagingEntries);
        }
    }

    [Fact]
    public async Task ExportAsync_InitializationFailureBeforeMarkerWritten_CleansUpOrphanContainer()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "initfail-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);

        bool hookRan = false;
        exporter.OnStagingInitializationForTest = () =>
        {
            hookRan = true;
            Assert.True(Directory.Exists(stagingDir));
            Assert.False(File.Exists(Path.Combine(stagingDir, ".staging-owner")));
            throw new InvalidOperationException("Injected staging initialization failure");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected staging initialization failure", ex.Message);
        Assert.True(hookRan);

        // Staging directory must have been cleanly removed by finally/CleanupOwnedStagingContainer
        Assert.False(Directory.Exists(stagingDir));

        if (Directory.Exists(parentDir))
        {
            string[] stagingDirs = Directory.GetDirectories(parentDir, ".staging-*");
            Assert.Empty(stagingDirs);
        }
    }

    [Fact]
    public async Task ExportAsync_InitializationFailureWithForeignContent_PreservesForeignDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "initforeign-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string foreignFile = Path.Combine(stagingDir, "foreign-init-actor.txt");

        bool hookRan = false;
        exporter.OnStagingInitializationForTest = () =>
        {
            hookRan = true;
            Assert.True(Directory.Exists(stagingDir));
            File.WriteAllText(foreignFile, "preserve-foreign-init", Utf8NoBom);
            throw new InvalidOperationException("Injected staging initialization failure with foreign data");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected staging initialization failure with foreign data", ex.Message);
        Assert.True(hookRan);

        // Staging directory and foreign file must be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(foreignFile));
        Assert.Equal("preserve-foreign-init", File.ReadAllText(foreignFile, Utf8NoBom));
    }

    // =========================================================================
    // Category 21: Recovery V - Guarded Staging Lifecycle & Owner Marker Safety
    // =========================================================================

    [Fact]
    public async Task ExportAsync_StagingDirectoryCreationFailure_CleansUpOrphanDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "dirfail-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);

        bool hookRan = false;
        exporter.OnStagingDirectoryCreationForTest = (dirPath) =>
        {
            hookRan = true;
            Directory.CreateDirectory(dirPath);
            Assert.True(Directory.Exists(dirPath));
            throw new InvalidOperationException("Injected staging directory creation failure");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected staging directory creation failure", ex.Message);
        Assert.True(hookRan);

        // Guarded cleanup must ensure orphan staging directory is removed
        Assert.False(Directory.Exists(stagingDir));

        if (Directory.Exists(parentDir))
        {
            string[] stagingDirs = Directory.GetDirectories(parentDir, ".staging-*");
            Assert.Empty(stagingDirs);
        }
    }

    [Fact]
    public async Task ExportAsync_OwnerMarkerCreationFailure_PreservesEmptyMarkerAndStagingDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "markercreatefail-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        exporter.OnStagingOwnerMarkerWriteForTest = (path, token) =>
        {
            hookRan = true;
            File.Create(path).Dispose();
            Assert.True(File.Exists(path));
            Assert.Equal(0, new FileInfo(path).Length);
            throw new IOException("Injected marker creation failure");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected marker creation failure", ex.Message);
        Assert.True(hookRan);

        // Ownership not established by exporter: empty marker and staging directory are preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal(0, new FileInfo(markerPath).Length);
    }

    [Fact]
    public async Task ExportAsync_OwnerMarkerPartialWriteFailure_PreservesPartialMarkerAndStagingDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "partialwrite-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        byte[]? partialBytes = null;
        string? expectedPrefix = null;
        exporter.OnStagingOwnerMarkerWriteForTest = (path, token) =>
        {
            hookRan = true;
            expectedPrefix = token[..16];
            partialBytes = Encoding.UTF8.GetBytes(expectedPrefix);
            using (FileStream fs = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(partialBytes);
                fs.Flush(flushToDisk: true);
            }

            Assert.True(File.Exists(path));
            Assert.Equal(16, new FileInfo(path).Length);
            throw new IOException("Injected partial marker write failure");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected partial marker write failure", ex.Message);
        Assert.True(hookRan);

        // Ownership not established by exporter: partial marker and directory are preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal(16, new FileInfo(markerPath).Length);
        Assert.NotNull(partialBytes);
        Assert.Equal(partialBytes, File.ReadAllBytes(markerPath));
        Assert.Equal(expectedPrefix, File.ReadAllText(markerPath, Utf8NoBom));
    }

    [Fact]
    public async Task ExportAsync_OwnerMarkerFlushFailure_PreservesMarkerAndStagingDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "flushfail-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        byte[]? tokenBytes = null;
        exporter.OnStagingOwnerMarkerWriteForTest = (path, token) =>
        {
            hookRan = true;
            tokenBytes = Encoding.UTF8.GetBytes(token);
            using (FileStream fs = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(tokenBytes);
            }

            Assert.True(File.Exists(path));
            Assert.Equal(64, new FileInfo(path).Length);
            throw new IOException("Injected marker flush failure");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected marker flush failure", ex.Message);
        Assert.True(hookRan);

        // Ownership not established by exporter: marker and directory are preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal(64, new FileInfo(markerPath).Length);
        Assert.NotNull(tokenBytes);
        Assert.Equal(tokenBytes, File.ReadAllBytes(markerPath));
    }

    [Fact]
    public async Task ExportAsync_LegacyMarkerWriteWithMatchingPrefixWithoutExclusiveCreation_PreservesMarkerExactBytesAndDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "legacymatchprefix-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        byte[]? exactPrefixBytes = null;
        string? expectedPrefixText = null;

        exporter.OnStagingOwnerMarkerWriteForTest = (path, token) =>
        {
            hookRan = true;
            expectedPrefixText = token[..28];
            exactPrefixBytes = Encoding.UTF8.GetBytes(expectedPrefixText);

            // Legacy callback writes matching prefix without exclusive creation by exporter
            File.WriteAllBytes(path, exactPrefixBytes);

            Assert.True(File.Exists(path));
            Assert.Equal(28, new FileInfo(path).Length);

            throw new IOException("Injected failure during legacy marker write with matching prefix");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected failure during legacy marker write with matching prefix", ex.Message);
        Assert.True(hookRan);

        // Legacy hook wrote matching prefix without successful exclusive creation by exporter:
        // Marker, exact bytes, and staging directory must be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.NotNull(exactPrefixBytes);
        Assert.Equal(exactPrefixBytes, File.ReadAllBytes(markerPath));
        Assert.Equal(expectedPrefixText, File.ReadAllText(markerPath, Utf8NoBom));
        Assert.Equal(28, new FileInfo(markerPath).Length);
    }

    [Fact]
    public async Task ExportAsync_InitializationFailureWithForeignOwnerMarker_PreservesForeignMarkerAndDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "foreignmarker-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        exporter.OnStagingInitializationForTest = () =>
        {
            hookRan = true;
            File.WriteAllText(markerPath, "foreign-unauthorized-owner-token", Utf8NoBom);
            throw new InvalidOperationException("Injected initialization failure with foreign owner marker");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected initialization failure with foreign owner marker", ex.Message);
        Assert.True(hookRan);

        // Ownership cannot be established: foreign marker and directory MUST be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal("foreign-unauthorized-owner-token", File.ReadAllText(markerPath, Utf8NoBom));
    }

    [Fact]
    public async Task ExportAsync_InitializationFailureWithForeign0ByteMarker_PreservesForeignMarkerAndDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "foreign0byte-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        exporter.OnStagingInitializationForTest = () =>
        {
            hookRan = true;
            File.Create(markerPath).Dispose();
            throw new InvalidOperationException("Injected initialization failure with foreign empty marker");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected initialization failure with foreign empty marker", ex.Message);
        Assert.True(hookRan);

        // Empty marker was not created by this invocation's marker step: MUST be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal(0, new FileInfo(markerPath).Length);
    }

    [Fact]
    public async Task ExportAsync_OwnerMarkerPartialWriteWithForeignFile_PreservesForeignContentAndDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "partialforeign-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");
        string foreignCanary = Path.Combine(stagingDir, "foreign-actor-canary.txt");

        bool hookRan = false;
        exporter.OnStagingOwnerMarkerWriteForTest = (path, token) =>
        {
            hookRan = true;
            byte[] partialBytes = Encoding.UTF8.GetBytes(token[..20]);
            using (FileStream fs = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(partialBytes);
                fs.Flush(flushToDisk: true);
            }

            File.WriteAllText(foreignCanary, "foreign-content-must-be-preserved", Utf8NoBom);
            throw new IOException("Injected partial write with foreign file");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected partial write with foreign file", ex.Message);
        Assert.True(hookRan);

        // Foreign file present: directory and all content MUST be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.True(File.Exists(foreignCanary));
        Assert.Equal("foreign-content-must-be-preserved", File.ReadAllText(foreignCanary, Utf8NoBom));
    }

    [Fact]
    public async Task ExportAsync_PartialMarkerFailureWithForeignPayloadInserted_PreservesForeignPayloadAndDirectory()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "foreignpayload-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        string relPath = $"workflows/{workflowIdHex}/records/00000000000000000001.json";
        string fullPath = Path.Combine(store.StorageRoot, relPath);
        byte[] recBytes = File.ReadAllBytes(fullPath);
        string sha = WorkflowEvidenceManifestJson.ComputeSha256LowerHex(recBytes);
        WorkflowEvidenceSourceRecord sr = new(relPath, recBytes.Length, sha);
        string srcFp = WorkflowEvidenceManifestJson.ComputeSourceSetFingerprint([sr]);
        string expectedPackId = WorkflowEvidenceManifestJson.ComputePackId(
            WorkflowEvidencePackExporter.AlgorithmId,
            WorkflowEvidencePackExporter.ManifestSchemaId,
            WorkflowEvidencePackExporter.CurrentSchemaVersion,
            store.WorkflowId.Value,
            created.SourceImplementationPlanRevision,
            1L,
            srcFp);

        string foreignPayloadDir = Path.Combine(stagingDir, expectedPackId);
        string foreignManifestPath = Path.Combine(foreignPayloadDir, "manifest.json");
        byte[] foreignManifestBytes = Encoding.UTF8.GetBytes("{\"foreign\":true,\"data\":\"cannot-be-deleted\"}");
        byte[] partialBytes = null!;

        bool hookRan = false;
        exporter.OnStagingOwnerMarkerStreamWriteForTest = (stream, token) =>
        {
            hookRan = true;
            partialBytes = Encoding.UTF8.GetBytes(token[..16]);
            stream.Write(partialBytes);
            stream.Flush(flushToDisk: true);

            Directory.CreateDirectory(foreignPayloadDir);
            File.WriteAllBytes(foreignManifestPath, foreignManifestBytes);

            throw new IOException("Injected partial write failure after foreign payload insertion");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected partial write failure after foreign payload insertion", ex.Message);
        Assert.True(hookRan);

        // Since ownerMarkerWritten is false, packId payload must not exist; foreign payload, manifest.json and container MUST be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal(partialBytes, File.ReadAllBytes(markerPath));
        Assert.True(Directory.Exists(foreignPayloadDir));
        Assert.True(File.Exists(foreignManifestPath));
        Assert.Equal(foreignManifestBytes, File.ReadAllBytes(foreignManifestPath));
    }

    [Fact]
    public async Task ExportAsync_ForeignTempOwnerMarkerWithMatchingTokenPrefix_PreservesTempMarkerAndDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "foreigntmp-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string knownToken = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
        exporter.StagingOwnerTokenForTest = knownToken;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string tmpMarkerPath = Path.Combine(stagingDir, ".staging-owner.tmp");

        byte[] foreignTmpBytes = Encoding.UTF8.GetBytes(knownToken[..32]);

        bool hookRan = false;
        exporter.OnStagingInitializationForTest = () =>
        {
            hookRan = true;
            File.WriteAllBytes(tmpMarkerPath, foreignTmpBytes);
            throw new InvalidOperationException("Injected initialization failure with foreign temp marker");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected initialization failure with foreign temp marker", ex.Message);
        Assert.True(hookRan);

        // .staging-owner.tmp is not created by production write path and cannot be assumed owned: MUST be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(tmpMarkerPath));
        Assert.Equal(foreignTmpBytes, File.ReadAllBytes(tmpMarkerPath));
        Assert.Equal(knownToken[..32], File.ReadAllText(tmpMarkerPath, Utf8NoBom));
    }

    [Fact]
    public async Task ExportAsync_ForeignPartialOwnerMarkerWithoutOwnership_PreservesMarkerAndDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "foreignpartial-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string knownToken = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        exporter.StagingOwnerTokenForTest = knownToken;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        byte[] foreignPartialBytes = Encoding.UTF8.GetBytes(knownToken[..24]);

        bool hookRan = false;
        exporter.OnStagingInitializationForTest = () =>
        {
            hookRan = true;
            File.WriteAllBytes(markerPath, foreignPartialBytes);
            throw new InvalidOperationException("Injected initialization failure with foreign partial marker");
        };

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync());
        Assert.Contains("Injected initialization failure with foreign partial marker", ex.Message);
        Assert.True(hookRan);

        // Ownership was not independently established by this invocation: foreign partial marker and directory MUST be preserved
        Assert.True(Directory.Exists(stagingDir));
        Assert.True(File.Exists(markerPath));
        Assert.Equal(foreignPartialBytes, File.ReadAllBytes(markerPath));
        Assert.Equal(knownToken[..24], File.ReadAllText(markerPath, Utf8NoBom));
    }

    [Fact]
    public async Task ExportAsync_OwnedPartialMarkerAfterWriteFailure_CleansUpMarkerAndStagingDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "ownedpartialwrite-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        exporter.OnStagingOwnerMarkerStreamWriteForTest = (stream, token) =>
        {
            hookRan = true;
            byte[] partialBytes = Encoding.UTF8.GetBytes(token[..16]);
            stream.Write(partialBytes);
            stream.Flush(flushToDisk: true);

            Assert.True(File.Exists(markerPath));
            Assert.Equal(16, new FileInfo(markerPath).Length);
            throw new IOException("Injected genuine write failure on owned stream");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected genuine write failure on owned stream", ex.Message);
        Assert.True(hookRan);

        // Demonstrably owned partial marker: cleanup succeeds
        Assert.False(Directory.Exists(stagingDir));
        Assert.False(File.Exists(markerPath));

        if (Directory.Exists(parentDir))
        {
            string[] stagingDirs = Directory.GetDirectories(parentDir, ".staging-*");
            Assert.Empty(stagingDirs);
        }
    }

    [Fact]
    public async Task ExportAsync_OwnedMarkerFlushFailureOnStream_CleansUpMarkerAndStagingDirectory()
    {
        (WorkflowPersistenceStore store, _) = this.CreateStoreWithWorkflow();
        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "ownedflushfail-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);
        string stagingDir = Path.Combine(parentDir, ".staging-" + suffix);
        string markerPath = Path.Combine(stagingDir, ".staging-owner");

        bool hookRan = false;
        exporter.OnStagingOwnerMarkerStreamWriteForTest = (stream, token) =>
        {
            hookRan = true;
            byte[] tokenBytes = Encoding.UTF8.GetBytes(token);
            stream.Write(tokenBytes);
            Assert.True(File.Exists(markerPath));
            throw new IOException("Injected genuine flush failure on owned stream");
        };

        IOException ex = await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync());
        Assert.Contains("Injected genuine flush failure on owned stream", ex.Message);
        Assert.True(hookRan);

        // Marker and directory cleaned up
        Assert.False(Directory.Exists(stagingDir));
        Assert.False(File.Exists(markerPath));

        if (Directory.Exists(parentDir))
        {
            string[] stagingDirs = Directory.GetDirectories(parentDir, ".staging-*");
            Assert.Empty(stagingDirs);
        }
    }

    [Fact]
    public async Task ExportAsync_SuccessfulExport_ProducesValidImmutableEvidencePackAndNoLingeringStaging()
    {
        (WorkflowPersistenceStore store, WorkflowState created) = this.CreateStoreWithWorkflow();
        this.AddSampleCheckpointJournal(store, created, "task-1", 1);
        this.AddSampleGateJournal(store, created, "task-1", 1);

        WorkflowEvidencePackExporter exporter = new(store);

        string suffix = "success-" + Guid.NewGuid().ToString("N");
        exporter.StagingDirectorySuffixForTest = suffix;

        string workflowIdHex = Convert.ToHexString(Encoding.UTF8.GetBytes(store.WorkflowId.Value)).ToLowerInvariant();
        string capturedRevD20 = 1L.ToString("D20");
        string parentDir = Path.Combine(store.StorageRoot, "workflows", workflowIdHex, "evidence-packs", capturedRevD20);

        WorkflowEvidencePack pack = await exporter.ExportAsync();

        // Validate exported evidence pack properties
        Assert.NotNull(pack);
        Assert.Equal(64, pack.PackId.Length);
        Assert.Equal(store.WorkflowId, pack.WorkflowId);
        Assert.Equal(1L, pack.CapturedPersistenceRevision);
        Assert.Equal(64, pack.ManifestFingerprint.Length);
        Assert.True(Directory.Exists(pack.PackDirectory));
        Assert.True(File.Exists(pack.ManifestPath));

        // Verify fingerprint matches manifest.json content
        byte[] manifestBytes = File.ReadAllBytes(pack.ManifestPath);
        string computedFp = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        Assert.Equal(pack.ManifestFingerprint, computedFp);

        // Verify verification succeeds
        WorkflowEvidencePack verified = await exporter.VerifyPackAsync(
            pack.CapturedPersistenceRevision,
            pack.PackId,
            pack.ManifestFingerprint);
        Assert.Equal(pack.PackId, verified.PackId);

        // Verify no lingering staging directories
        if (Directory.Exists(parentDir))
        {
            string[] stagingDirs = Directory.GetDirectories(parentDir, ".staging-*");
            Assert.Empty(stagingDirs);
        }

        // Final pack immutability assertions
        Assert.IsAssignableFrom<IReadOnlyList<WorkflowEvidenceSourceRecord>>(pack.Manifest.SourceRecords);
        Assert.IsAssignableFrom<IReadOnlyList<WorkflowEvidenceEvent>>(pack.Manifest.Events);
        Assert.IsAssignableFrom<IReadOnlyList<WorkflowTaskEvidence>>(pack.Manifest.Tasks);

        // Tampering with manifest.json causes verification to fail
        byte[] originalManifestBytes = File.ReadAllBytes(pack.ManifestPath);
        byte[] tamperedBytes = (byte[])originalManifestBytes.Clone();
        tamperedBytes[^1] ^= 0xFF;
        File.WriteAllBytes(pack.ManifestPath, tamperedBytes);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            exporter.VerifyPackAsync(pack.CapturedPersistenceRevision, pack.PackId, pack.ManifestFingerprint));

        // Restore manifest bytes
        File.WriteAllBytes(pack.ManifestPath, originalManifestBytes);

        WorkflowEvidencePack restoredVerified = await exporter.VerifyPackAsync(
            pack.CapturedPersistenceRevision,
            pack.PackId,
            pack.ManifestFingerprint);
        Assert.Equal(pack.PackId, restoredVerified.PackId);
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private (WorkflowPersistenceStore Store, WorkflowState State) CreateStoreWithWorkflow(
        string taskId = "task-1")
    {
        ExecutableWork work = new(
            1,
            [
                new ExecutableTask(
                    taskId,
                    "step-1",
                    "instruction")
            ],
            [],
            [],
            [],
            []);

        WorkflowPersistenceStore store = new(
            this._tempDirectory,
            new WorkflowId("wf-test-" + Guid.NewGuid().ToString("N")[..8]));

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        return (store, created);
    }

    private WorkflowCheckpointJournal AddSampleCheckpointJournal(
        WorkflowPersistenceStore store_,
        WorkflowState state_,
        string taskId_,
        long baseRevision_)
    {
        string baseFp = WorkflowCheckpointCoordinatorContext.ComputeBaseWorkflowStateFingerprint(state_);
        string inputFp = ValidHash1;
        string registryFp = ValidHash2;

        return WorkflowCheckpointJournal.CreateNew(
            store_.StorageRoot,
            store_.WorkflowId,
            taskId_,
            baseRevision_,
            baseFp,
            inputFp,
            registryFp);
    }

    private WorkflowGateJournal AddSampleGateJournal(
        WorkflowPersistenceStore store_,
        WorkflowState state_,
        string taskId_,
        long baseRevision_)
    {
        string baseFp = WorkflowGateCoordinator.ComputeBaseStateFingerprint(state_);
        string inputFp = ValidHash1;
        string registryFp = ValidHash2;
        string policyFp = ValidHash3;

        string gateId = WorkflowGateCoordinator.ComputeGateId(
            store_.WorkflowId,
            taskId_,
            baseRevision_,
            state_.SourceImplementationPlanRevision,
            ExecutionPermission.WorkspaceWrite,
            baseFp,
            inputFp,
            registryFp,
            policyFp);

        WorkflowGateChallenge challenge = new(
            gateId,
            store_.WorkflowId,
            taskId_,
            baseRevision_,
            state_.SourceImplementationPlanRevision,
            ExecutionPermission.WorkspaceWrite,
            1L,
            baseFp,
            inputFp,
            registryFp,
            policyFp);

        return WorkflowGateJournal.CreateNew(
            store_.StorageRoot,
            store_.WorkflowId,
            taskId_,
            baseRevision_,
            challenge);
    }

    private static bool IsLowerHex(string value_)
    {
        for (int i = 0; i < value_.Length; i++)
        {
            char c = value_[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }
        return true;
    }

    private WorkflowGateJournal AddSampleGateJournalWithGrant(
        WorkflowPersistenceStore store_,
        WorkflowState state_,
        string taskId_,
        long baseRevision_,
        string principalId_ = "admin-1",
        string mechanismId_ = "cli-token",
        string evidenceFingerprint_ = ValidHash4)
    {
        WorkflowGateJournal journal = this.AddSampleGateJournal(store_, state_, taskId_, baseRevision_);
        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            SchemaId = WorkflowGateJournal.SchemaId,
            SchemaVersion = WorkflowGateJournal.CurrentSchemaVersion,
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = principalId_,
            MechanismId = mechanismId_,
            EvidenceFingerprint = evidenceFingerprint_
        });
        return journal;
    }

    private WorkflowGateJournal AddSampleGateJournalWithDenial(
        WorkflowPersistenceStore store_,
        WorkflowState state_,
        string taskId_,
        long baseRevision_,
        string principalId_ = "admin-1",
        string mechanismId_ = "cli-token",
        string evidenceFingerprint_ = ValidHash4)
    {
        WorkflowGateJournal journal = this.AddSampleGateJournal(store_, state_, taskId_, baseRevision_);
        journal.AppendRecord(new AuthorizationDeniedRecord
        {
            SchemaId = WorkflowGateJournal.SchemaId,
            SchemaVersion = WorkflowGateJournal.CurrentSchemaVersion,
            Sequence = 2,
            GateId = journal.ChallengeRecord.GateId,
            PrincipalId = principalId_,
            MechanismId = mechanismId_,
            EvidenceFingerprint = evidenceFingerprint_
        });
        return journal;
    }

    private WorkflowCheckpointJournal AddSampleCheckpointJournalWithOutcome(
        WorkflowPersistenceStore store_,
        WorkflowState state_,
        string taskId_,
        long baseRevision_,
        int attemptCount_ = 1,
        string finalStateFingerprint_ = ValidHash3)
    {
        WorkflowCheckpointJournal journal = this.AddSampleCheckpointJournal(store_, state_, taskId_, baseRevision_);
        journal.AppendRecord(new InvocationIntentRecord
        {
            SchemaId = WorkflowCheckpointJournal.SchemaId,
            SchemaVersion = WorkflowCheckpointJournal.CurrentSchemaVersion,
            Sequence = 2,
            Kind = JournalRecordKind.InvocationIntent,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            ExternalIdentity = "agent-1"
        });
        journal.AppendRecord(new InvocationOutcomeRecord
        {
            SchemaId = WorkflowCheckpointJournal.SchemaId,
            SchemaVersion = WorkflowCheckpointJournal.CurrentSchemaVersion,
            Sequence = 3,
            Kind = JournalRecordKind.InvocationOutcome,
            OperationOrdinal = 1,
            OperationId = "op-1",
            OperationKind = "Agent",
            InvocationGeneration = 1,
            RequestFingerprint = ValidHash1,
            IsNullReturn = false,
            AgentStatus = 0,
            AgentOutputText = "done output",
            AgentDiagnosticText = "no diag",
            AgentSessionReference = "sess-1",
            ValidationPassed = true,
            ValidationEvidence = "valid evidence"
        });
        journal.AppendRecord(new ExecutionCompletedRecord
        {
            SchemaId = WorkflowCheckpointJournal.SchemaId,
            SchemaVersion = WorkflowCheckpointJournal.CurrentSchemaVersion,
            Sequence = 4,
            Kind = JournalRecordKind.ExecutionCompleted,
            AttemptCount = attemptCount_,
            FinalStateFingerprint = finalStateFingerprint_,
            BudgetExhausted = false,
            ExpectedProjectionTransitionCount = 1
        });
        return journal;
    }

    private static void CopyDirectoryRecursively(string sourceDir_, string destinationDir_)
    {
        Directory.CreateDirectory(destinationDir_);
        foreach (string file in Directory.GetFiles(sourceDir_))
        {
            File.Copy(file, Path.Combine(destinationDir_, Path.GetFileName(file)), overwrite: true);
        }
        foreach (string dir in Directory.GetDirectories(sourceDir_))
        {
            CopyDirectoryRecursively(dir, Path.Combine(destinationDir_, Path.GetFileName(dir)));
        }
    }

    private static bool TryCreateReparsePoint(string linkPath_, string targetPath_)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath_, targetPath_);
            return true;
        }
        catch (Exception)
        {
            return TryCreateJunction(linkPath_, targetPath_);
        }
    }

    private static bool TryCreateJunction(string linkPath_, string targetPath_)
    {
        try
        {
            Directory.CreateDirectory(linkPath_);
            string targetFullPath = Path.GetFullPath(targetPath_);
            string substituteName = @"\??\" + targetFullPath;
            string printName = targetFullPath;

            byte[] substituteBytes = Encoding.Unicode.GetBytes(substituteName);
            byte[] printBytes = Encoding.Unicode.GetBytes(printName);

            int reparseDataLength = 8 + substituteBytes.Length + 2 + printBytes.Length + 2;
            byte[] buffer = new byte[8 + reparseDataLength];

            BitConverter.GetBytes(0xA0000003U).CopyTo(buffer, 0);
            BitConverter.GetBytes((ushort)reparseDataLength).CopyTo(buffer, 4);
            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);

            BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);
            BitConverter.GetBytes((ushort)substituteBytes.Length).CopyTo(buffer, 10);
            BitConverter.GetBytes((ushort)(substituteBytes.Length + 2)).CopyTo(buffer, 12);
            BitConverter.GetBytes((ushort)printBytes.Length).CopyTo(buffer, 14);

            Array.Copy(substituteBytes, 0, buffer, 16, substituteBytes.Length);
            Array.Copy(printBytes, 0, buffer, 16 + substituteBytes.Length + 2, printBytes.Length);

            using SafeFileHandle handle = CreateFileNative(
                linkPath_,
                0x40000000U,
                7U,
                IntPtr.Zero,
                3U,
                0x02000000U | 0x00200000U,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return false;
            }

            return DeviceIoControlNative(
                handle,
                0x000900A4U,
                buffer,
                (uint)buffer.Length,
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileNative(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DeviceIoControlNative(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
