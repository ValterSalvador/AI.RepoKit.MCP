namespace AiRepoKit.WorkflowExecution;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed class WorkflowEvidencePackExporter
{
    public const string AlgorithmId =
        "ai.repokit.workflow-evidence-pack/v1";

    public const string ManifestSchemaId =
        "ai.repokit.workflow-evidence-manifest";

    public const int CurrentSchemaVersion =
        1;

    private static readonly Regex _checkpointRecordPathPattern =
        new(@"^workflows/([0-9a-f]+)/execution-checkpoints/([0-9a-f]+)/([0-9]{20})/records/([0-9]{20})\.json$", RegexOptions.Compiled);

    private static readonly Regex _gateRecordPathPattern =
        new(@"^workflows/([0-9a-f]+)/human-gates/([0-9a-f]+)/([0-9]{20})/records/([0-9]{20})\.json$", RegexOptions.Compiled);

    private static readonly Regex _persistenceRecordPathPattern =
        new(@"^workflows/([0-9a-f]+)/records/([0-9]{20})\.json$", RegexOptions.Compiled);

    private readonly WorkflowPersistenceStore _persistenceStore;

    internal Func<Task>? OnBeforePublicationForTest { get; set; }

    internal Func<Task>? OnStagingInitializationForTest { get; set; }

    internal Func<string, Task>? OnStagingDirectoryCreationForTest { get; set; }

    internal Func<string, string, Task>? OnStagingOwnerMarkerWriteForTest { get; set; }

    internal Func<FileStream, string, Task>? OnStagingOwnerMarkerStreamWriteForTest { get; set; }

    internal string? StagingOwnerTokenForTest { get; set; }

    internal string? StagingDirectorySuffixForTest { get; set; }

    internal string? LastComputedPackIdForTest { get; private set; }

    public WorkflowEvidencePackExporter(
        WorkflowPersistenceStore persistenceStore_)
    {
        ArgumentNullException.ThrowIfNull(
            persistenceStore_,
            nameof(persistenceStore_));

        this._persistenceStore =
            persistenceStore_;
    }

    public async Task<WorkflowEvidencePack> ExportAsync(
        CancellationToken cancellationToken_ = default)
    {
        cancellationToken_.ThrowIfCancellationRequested();

        WorkflowId workflowId =
            this._persistenceStore.WorkflowId;
        string storageRoot =
            Path.GetFullPath(this._persistenceStore.StorageRoot);

        ValidatePathRootAndReparse(storageRoot, storageRoot);

        string encodedWorkflowId =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    workflowId.Value)).ToLowerInvariant();

        string recordsDirectory =
            Path.Combine(storageRoot, "workflows", encodedWorkflowId, "records");
        ValidatePathRootAndReparse(storageRoot, recordsDirectory);

        WorkflowPersistenceSnapshot? currentSnapshot =
            this._persistenceStore.Load();

        if (currentSnapshot is null)
        {
            throw new InvalidOperationException(
                "Workflow persistence store has not been initialized.");
        }

        long capturedPersistenceRevision =
            currentSnapshot.Revision;
        WorkflowState currentState =
            currentSnapshot.State;

        List<WorkflowEvidenceSourceRecord> sourceRecords = [];
        Dictionary<long, ParsedPersistenceRecord> persistenceRecordsByRevision = [];

        // 1. Collect canonical persistence records for revisions 1..capturedPersistenceRevision
        for (long rev = 1; rev <= capturedPersistenceRevision; rev++)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            string relPath = $"workflows/{encodedWorkflowId}/records/{rev:D20}.json";
            string fullPath = Path.Combine(storageRoot, "workflows", encodedWorkflowId, "records", $"{rev:D20}.json");

            ValidateSourcePathSecurity(storageRoot, fullPath, relPath);

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"Missing canonical persistence record file at revision {rev}: '{fullPath}'.");
            }

            byte[] bytes = ReadFileBytesAndValidateNoBom(fullPath);
            string sha256 = WorkflowEvidenceManifestJson.ComputeSha256LowerHex(bytes);

            ParsedPersistenceRecord parsedRecord =
                ParsePersistenceRecord(bytes);

            if (parsedRecord.Revision != rev)
            {
                throw new InvalidOperationException(
                    $"Persistence record internal revision {parsedRecord.Revision} does not match expected revision {rev}.");
            }

            if (!string.Equals(parsedRecord.WorkflowId, workflowId.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Persistence record workflowId '{parsedRecord.WorkflowId}' does not match expected '{workflowId.Value}'.");
            }

            if (!string.Equals(parsedRecord.SchemaId, WorkflowPersistenceStore.SchemaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Persistence record schemaId '{parsedRecord.SchemaId}' does not match expected '{WorkflowPersistenceStore.SchemaId}'.");
            }

            if (parsedRecord.SchemaVersion != WorkflowPersistenceStore.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Persistence record schemaVersion {parsedRecord.SchemaVersion} does not match expected {WorkflowPersistenceStore.CurrentSchemaVersion}.");
            }

            if (!string.Equals(parsedRecord.Event.WorkflowId, workflowId.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Persistence record event workflowId '{parsedRecord.Event.WorkflowId}' does not match expected '{workflowId.Value}'.");
            }

            if (parsedRecord.Event.Sequence != rev)
            {
                throw new InvalidOperationException(
                    $"Persistence record event sequence {parsedRecord.Event.Sequence} does not match expected revision {rev}.");
            }

            persistenceRecordsByRevision[rev] = parsedRecord;
            sourceRecords.Add(new WorkflowEvidenceSourceRecord(relPath, bytes.Length, sha256));
        }

        // 2. Collect events up to capturedPersistenceRevision
        IReadOnlyList<WorkflowExecutionEvent> rawEvents =
            this._persistenceStore.ReadEvents();

        List<WorkflowEvidenceEvent> events = [];
        for (int i = 0; i < rawEvents.Count; i++)
        {
            WorkflowExecutionEvent rawEvent = rawEvents[i];
            if (rawEvent.Sequence <= capturedPersistenceRevision)
            {
                events.Add(new WorkflowEvidenceEvent(
                    workflowId,
                    rawEvent.Sequence,
                    rawEvent.Kind,
                    rawEvent.TaskId,
                    rawEvent.PreviousWorkflowStatus,
                    rawEvent.TargetWorkflowStatus,
                    rawEvent.PreviousStepStatus,
                    rawEvent.TargetStepStatus));
            }
        }

        if (events.Count != capturedPersistenceRevision)
        {
            throw new InvalidOperationException(
                $"Event count {events.Count} does not match captured persistence revision {capturedPersistenceRevision}.");
        }

        // 3. Collect task evidence (checkpoint journals and gate journals)
        List<WorkflowTaskEvidence> tasks = [];

        foreach (WorkflowStepState step in currentState.Steps)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            string taskId = step.TaskId;
            string taskIdHex =
                Convert.ToHexString(
                    Encoding.UTF8.GetBytes(
                        taskId)).ToLowerInvariant();

            string cpTaskDir =
                Path.Combine(storageRoot, "workflows", encodedWorkflowId, "execution-checkpoints", taskIdHex);
            ValidatePathRootAndReparse(storageRoot, cpTaskDir);

            // Discover and load checkpoint journals <= capturedPersistenceRevision
            IReadOnlyList<long> cpRevisions =
                WorkflowCheckpointJournal.DiscoverCandidateRevisions(
                    storageRoot,
                    workflowId,
                    taskId);

            List<long> eligibleCpRevisions =
                cpRevisions
                    .Where(r => r <= capturedPersistenceRevision)
                    .OrderBy(r => r)
                    .ToList();

            List<WorkflowCheckpointJournalEvidence> checkpointJournals = [];

            foreach (long cpRev in eligibleCpRevisions)
            {
                cancellationToken_.ThrowIfCancellationRequested();

                string cpRevDir =
                    Path.Combine(cpTaskDir, cpRev.ToString("D20"));
                ValidatePathRootAndReparse(storageRoot, cpRevDir);
                string cpRecDir =
                    Path.Combine(cpRevDir, "records");
                ValidatePathRootAndReparse(storageRoot, cpRecDir);

                WorkflowCheckpointJournal cpJournal =
                    WorkflowCheckpointJournal.OpenExisting(
                        storageRoot,
                        workflowId,
                        taskId,
                        cpRev);

                CheckpointStartedRecord started = cpJournal.StartedRecord;

                if (!string.Equals(started.WorkflowId, workflowId.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Checkpoint journal workflowId '{started.WorkflowId}' does not match expected '{workflowId.Value}'.");
                }

                if (!string.Equals(started.TaskId, taskId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Checkpoint journal taskId '{started.TaskId}' does not match expected '{taskId}'.");
                }

                if (started.BasePersistenceRevision != cpRev)
                {
                    throw new InvalidOperationException(
                        $"Checkpoint journal base persistence revision {started.BasePersistenceRevision} does not match expected {cpRev}.");
                }

                if (!persistenceRecordsByRevision.TryGetValue(cpRev, out ParsedPersistenceRecord? baseParsedRecord))
                {
                    throw new InvalidOperationException(
                        $"Base persistence revision {cpRev} not found for checkpoint journal of task '{taskId}'.");
                }

                // Verify base workflow state fingerprint using original step order
                string expectedBaseFp =
                    ComputeP09BaseStateFingerprint(
                        baseParsedRecord.State);

                if (!string.Equals(
                        cpJournal.StartedRecord.BaseWorkflowStateFingerprint,
                        expectedBaseFp,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Checkpoint journal base state fingerprint mismatch for task '{taskId}' at revision {cpRev}.");
                }

                // Add source records for each journal record file
                for (long seq = 1; seq <= cpJournal.Records.Count; seq++)
                {
                    string recRelPath = $"workflows/{encodedWorkflowId}/execution-checkpoints/{taskIdHex}/{cpRev:D20}/records/{seq:D20}.json";
                    string recFullPath = Path.Combine(storageRoot, "workflows", encodedWorkflowId, "execution-checkpoints", taskIdHex, cpRev.ToString("D20"), "records", $"{seq:D20}.json");

                    ValidateSourcePathSecurity(storageRoot, recFullPath, recRelPath);

                    if (!File.Exists(recFullPath))
                    {
                        throw new InvalidOperationException(
                            $"Missing checkpoint journal record file: '{recFullPath}'.");
                    }

                    byte[] recBytes = ReadFileBytesAndValidateNoBom(recFullPath);
                    string recSha = WorkflowEvidenceManifestJson.ComputeSha256LowerHex(recBytes);
                    sourceRecords.Add(new WorkflowEvidenceSourceRecord(recRelPath, recBytes.Length, recSha));
                }

                // Map journal records with privacy boundaries
                List<WorkflowJournalRecordEvidence> journalRecords = [];
                foreach (JournalRecord jr in cpJournal.Records)
                {
                    journalRecords.Add(MapCheckpointJournalRecord(jr));
                }

                bool executionCompleted = cpJournal.CompletedRecord is not null;
                string? finalStateFp = cpJournal.CompletedRecord?.FinalStateFingerprint;
                int? attemptCount = cpJournal.CompletedRecord?.AttemptCount;
                bool? budgetExhausted = cpJournal.CompletedRecord?.BudgetExhausted;
                int? expectedProjTransCount = cpJournal.CompletedRecord?.ExpectedProjectionTransitionCount;

                bool projCompleted = cpJournal.ProjectionCompletedRecord is not null;
                long? finalPersistRev = cpJournal.ProjectionCompletedRecord?.FinalPersistenceRevision;

                checkpointJournals.Add(new WorkflowCheckpointJournalEvidence(
                    cpRev,
                    cpJournal.StartedRecord.BaseWorkflowStateFingerprint,
                    cpJournal.StartedRecord.InputFingerprint,
                    cpJournal.StartedRecord.RegistryFingerprint,
                    executionCompleted,
                    finalStateFp,
                    attemptCount,
                    budgetExhausted,
                    expectedProjTransCount,
                    projCompleted,
                    finalPersistRev,
                    journalRecords));
            }

            string gateTaskDir =
                Path.Combine(storageRoot, "workflows", encodedWorkflowId, "human-gates", taskIdHex);
            ValidatePathRootAndReparse(storageRoot, gateTaskDir);

            // Discover and load human gate journals <= capturedPersistenceRevision
            IReadOnlyList<long> gateRevisions =
                WorkflowGateJournal.DiscoverCandidateRevisions(
                    storageRoot,
                    workflowId,
                    taskId);

            List<long> eligibleGateRevisions =
                gateRevisions
                    .Where(r => r <= capturedPersistenceRevision)
                    .OrderBy(r => r)
                    .ToList();

            List<WorkflowGateJournalEvidence> gateJournals = [];

            foreach (long gRev in eligibleGateRevisions)
            {
                cancellationToken_.ThrowIfCancellationRequested();

                string gateRevDir =
                    Path.Combine(gateTaskDir, gRev.ToString("D20"));
                ValidatePathRootAndReparse(storageRoot, gateRevDir);
                string gateRecDir =
                    Path.Combine(gateRevDir, "records");
                ValidatePathRootAndReparse(storageRoot, gateRecDir);

                WorkflowGateJournal gateJournal =
                    WorkflowGateJournal.OpenExisting(
                        storageRoot,
                        workflowId,
                        taskId,
                        gRev);

                if (!persistenceRecordsByRevision.TryGetValue(gRev, out ParsedPersistenceRecord? baseParsedRecord))
                {
                    throw new InvalidOperationException(
                        $"Base persistence revision {gRev} not found for gate journal of task '{taskId}'.");
                }

                // Recompute P10 base state fingerprint using ordinal TaskId ordering
                string expectedGateBaseFp =
                    ComputeP10BaseStateFingerprint(
                        baseParsedRecord.State);

                ChallengeCreatedRecord chRec =
                    gateJournal.ChallengeRecord;

                if (!string.Equals(chRec.WorkflowId, workflowId.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gate challenge workflowId '{chRec.WorkflowId}' does not match expected '{workflowId.Value}'.");
                }

                if (!string.Equals(chRec.TaskId, taskId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gate challenge taskId '{chRec.TaskId}' does not match expected '{taskId}'.");
                }

                if (chRec.BasePersistenceRevision != gRev)
                {
                    throw new InvalidOperationException(
                        $"Gate challenge base persistence revision {chRec.BasePersistenceRevision} does not match expected {gRev}.");
                }

                if (!string.Equals(
                        chRec.BaseStateFingerprint,
                        expectedGateBaseFp,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gate challenge base state fingerprint mismatch for task '{taskId}' at revision {gRev}.");
                }

                ExecutionPermission reqPerm =
                    (ExecutionPermission) chRec.RequiredPermission;

                if (!Enum.IsDefined(typeof(ExecutionPermission), reqPerm))
                {
                    throw new InvalidOperationException(
                        $"Invalid required permission {chRec.RequiredPermission} in gate challenge.");
                }

                string expectedGateId =
                    WorkflowGateCoordinator.ComputeGateId(
                        workflowId,
                        taskId,
                        gRev,
                        baseParsedRecord.State.SourceImplementationPlanRevision,
                        reqPerm,
                        expectedGateBaseFp,
                        chRec.InputFingerprint,
                        chRec.RegistryFingerprint,
                        chRec.PolicyFingerprint);

                if (!string.Equals(
                        chRec.GateId,
                        expectedGateId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gate challenge GateId mismatch for task '{taskId}' at revision {gRev}.");
                }

                // Add source records for each gate journal record file
                for (long seq = 1; seq <= gateJournal.Records.Count; seq++)
                {
                    string recRelPath = $"workflows/{encodedWorkflowId}/human-gates/{taskIdHex}/{gRev:D20}/records/{seq:D20}.json";
                    string recFullPath = Path.Combine(storageRoot, "workflows", encodedWorkflowId, "human-gates", taskIdHex, gRev.ToString("D20"), "records", $"{seq:D20}.json");

                    ValidateSourcePathSecurity(storageRoot, recFullPath, recRelPath);

                    if (!File.Exists(recFullPath))
                    {
                        throw new InvalidOperationException(
                            $"Missing gate journal record file: '{recFullPath}'.");
                    }

                    byte[] recBytes = ReadFileBytesAndValidateNoBom(recFullPath);
                    string recSha = WorkflowEvidenceManifestJson.ComputeSha256LowerHex(recBytes);
                    sourceRecords.Add(new WorkflowEvidenceSourceRecord(recRelPath, recBytes.Length, recSha));
                }

                WorkflowGateChallenge challenge = new(
                    chRec.GateId,
                    new WorkflowId(chRec.WorkflowId),
                    chRec.TaskId,
                    chRec.BasePersistenceRevision,
                    baseParsedRecord.State.SourceImplementationPlanRevision,
                    reqPerm,
                    chRec.GateOrdinal,
                    chRec.BaseStateFingerprint,
                    chRec.InputFingerprint,
                    chRec.RegistryFingerprint,
                    chRec.PolicyFingerprint);

                bool? authorized = null;
                string? principalId = null;
                string? mechanismId = null;
                string? evidenceFingerprint = null;

                if (gateJournal.GrantedRecord is not null && gateJournal.DeniedRecord is not null)
                {
                    throw new InvalidOperationException(
                        $"Gate journal at revision {gRev} contains both an authorization grant and denial.");
                }

                if (gateJournal.GrantedRecord is not null)
                {
                    if (!string.Equals(
                            gateJournal.GrantedRecord.GateId,
                            chRec.GateId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Gate grant record GateId '{gateJournal.GrantedRecord.GateId}' does not match challenge GateId '{chRec.GateId}'.");
                    }

                    authorized = true;
                    principalId = gateJournal.GrantedRecord.PrincipalId;
                    mechanismId = gateJournal.GrantedRecord.MechanismId;
                    evidenceFingerprint = gateJournal.GrantedRecord.EvidenceFingerprint;
                }
                else if (gateJournal.DeniedRecord is not null)
                {
                    if (!string.Equals(
                            gateJournal.DeniedRecord.GateId,
                            chRec.GateId,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Gate denial record GateId '{gateJournal.DeniedRecord.GateId}' does not match challenge GateId '{chRec.GateId}'.");
                    }

                    authorized = false;
                    principalId = gateJournal.DeniedRecord.PrincipalId;
                    mechanismId = gateJournal.DeniedRecord.MechanismId;
                    evidenceFingerprint = gateJournal.DeniedRecord.EvidenceFingerprint;
                }

                gateJournals.Add(new WorkflowGateJournalEvidence(
                    gRev,
                    challenge,
                    authorized,
                    principalId,
                    mechanismId,
                    evidenceFingerprint));
            }

            tasks.Add(new WorkflowTaskEvidence(
                taskId,
                step.Status,
                checkpointJournals,
                gateJournals));
        }

        // 4. Sort source records ordinally and verify unique paths
        sourceRecords.Sort(
            static (left_, right_) =>
                string.CompareOrdinal(
                    left_.RelativePath,
                    right_.RelativePath));

        for (int i = 1; i < sourceRecords.Count; i++)
        {
            if (string.Equals(
                    sourceRecords[i - 1].RelativePath,
                    sourceRecords[i].RelativePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Duplicate source record path detected: '{sourceRecords[i].RelativePath}'.");
            }
        }

        // 5. Compute fingerprints and PackId
        string sourceSetFingerprint =
            WorkflowEvidenceManifestJson.ComputeSourceSetFingerprint(
                sourceRecords);

        string packId =
            WorkflowEvidenceManifestJson.ComputePackId(
                AlgorithmId,
                ManifestSchemaId,
                CurrentSchemaVersion,
                workflowId.Value,
                currentState.SourceImplementationPlanRevision,
                capturedPersistenceRevision,
                sourceSetFingerprint);

        this.LastComputedPackIdForTest = packId;

        // 6. Construct manifest and serialize
        WorkflowEvidenceManifest manifest =
            new(
                ManifestSchemaId,
                CurrentSchemaVersion,
                AlgorithmId,
                packId,
                workflowId,
                currentState.SourceImplementationPlanRevision,
                capturedPersistenceRevision,
                currentState.Status,
                sourceSetFingerprint,
                sourceRecords,
                events,
                tasks);

        byte[] manifestBytes =
            WorkflowEvidenceManifestJson.Serialize(
                manifest);

        string manifestFingerprint =
            WorkflowEvidenceManifestJson.ComputeSha256LowerHex(
                manifestBytes);

        // 7. Atomic publication to storage root
        string capturedRevD20 =
            capturedPersistenceRevision.ToString("D20");

        string packDirectory =
            Path.Combine(
                storageRoot,
                "workflows",
                encodedWorkflowId,
                "evidence-packs",
                capturedRevD20,
                packId);

        string manifestPath =
            Path.Combine(
                packDirectory,
                "manifest.json");

        string parentDir =
            Path.GetDirectoryName(packDirectory)!;

        ValidatePathRootAndReparse(storageRoot, parentDir);

        if (!Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
            ValidatePathRootAndReparse(storageRoot, parentDir);
        }

        // Check if destination pack directory already exists
        if (Directory.Exists(packDirectory))
        {
            ValidatePathRootAndReparse(storageRoot, packDirectory);
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException(
                    $"Destination evidence pack directory '{packDirectory}' already exists without manifest.json.");
            }

            WorkflowEvidencePack verifiedExisting =
                await this.VerifyPackAsync(
                    capturedPersistenceRevision,
                    packId,
                    manifestFingerprint,
                    cancellationToken_).ConfigureAwait(false);

            return verifiedExisting;
        }

        // Create unique private sibling staging directory
        string randomSuffix;
        if (this.StagingDirectorySuffixForTest is not null)
        {
            randomSuffix = this.StagingDirectorySuffixForTest;
        }
        else
        {
            byte[] randomBytes = new byte[16];
            RandomNumberGenerator.Fill(randomBytes);
            randomSuffix =
                Convert.ToHexString(randomBytes).ToLowerInvariant();
        }

        string stagingDirName = this.StagingDirectorySuffixForTest is not null
            ? $".staging-{this.StagingDirectorySuffixForTest}"
            : $".staging-{packId}-{randomSuffix}.tmp";

        string tempContainerDir =
            Path.Combine(
                parentDir,
                stagingDirName);

        ValidatePathRootAndReparse(storageRoot, tempContainerDir);

        if (Directory.Exists(tempContainerDir) || File.Exists(tempContainerDir))
        {
            throw new InvalidOperationException(
                $"Temporary staging directory already exists: '{tempContainerDir}'.");
        }

        string ownerToken;
        if (this.StagingOwnerTokenForTest is not null)
        {
            ownerToken = this.StagingOwnerTokenForTest;
        }
        else
        {
            byte[] ownerBytes = new byte[32];
            RandomNumberGenerator.Fill(ownerBytes);
            ownerToken =
                Convert.ToHexString(ownerBytes).ToLowerInvariant();
        }

        bool stagingDirectoryCreated = false;
        bool ownerMarkerCreated = false;
        bool ownerMarkerWritten = false;
        string? ownerMarkerPath = null;

        try
        {
            if (this.OnStagingDirectoryCreationForTest is not null)
            {
                await this.OnStagingDirectoryCreationForTest.Invoke(tempContainerDir).ConfigureAwait(false);
            }

            Directory.CreateDirectory(tempContainerDir);
            stagingDirectoryCreated = Directory.Exists(tempContainerDir);

            ValidatePathRootAndReparse(storageRoot, tempContainerDir);

            if (Directory.EnumerateFileSystemEntries(tempContainerDir).Any())
            {
                throw new InvalidOperationException(
                    $"Temporary staging directory is not empty upon creation: '{tempContainerDir}'.");
            }

            if (this.OnStagingInitializationForTest is not null)
            {
                await this.OnStagingInitializationForTest.Invoke().ConfigureAwait(false);
            }

            ownerMarkerPath =
                Path.Combine(tempContainerDir, ".staging-owner");

            if (this.OnStagingOwnerMarkerWriteForTest is not null)
            {
                await this.OnStagingOwnerMarkerWriteForTest.Invoke(ownerMarkerPath, ownerToken).ConfigureAwait(false);
                ownerMarkerCreated = true;
                ownerMarkerWritten = true;
            }
            else
            {
                using (FileStream ownerStream = new(
                    ownerMarkerPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough))
                {
                    ownerMarkerCreated = true;

                    if (this.OnStagingOwnerMarkerStreamWriteForTest is not null)
                    {
                        await this.OnStagingOwnerMarkerStreamWriteForTest.Invoke(ownerStream, ownerToken).ConfigureAwait(false);
                    }
                    else
                    {
                        byte[] tokenBytes = Encoding.UTF8.GetBytes(ownerToken);
                        ownerStream.Write(tokenBytes);
                        ownerStream.Flush(flushToDisk: true);
                    }
                }

                ownerMarkerWritten = true;
            }

            string tempPayloadDir =
                Path.Combine(
                    tempContainerDir,
                    packId);

            Directory.CreateDirectory(tempPayloadDir);
            ValidatePathRootAndReparse(storageRoot, tempPayloadDir);

            string tempManifestPath =
                Path.Combine(
                    tempPayloadDir,
                    "manifest.json");

            using (FileStream stream = new(
                tempManifestPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(manifestBytes);
                stream.Flush(flushToDisk: true);
            }

            // Controlled test synchronization point
            if (this.OnBeforePublicationForTest is not null)
            {
                await this.OnBeforePublicationForTest.Invoke().ConfigureAwait(false);
            }

            // Re-verify that source records have NOT drifted prior to atomic rename
            this.ValidatePrepublicationSourceDrift(
                storageRoot,
                workflowId,
                capturedPersistenceRevision,
                currentState,
                sourceRecords);

            // Re-check destination collision right before move
            if (Directory.Exists(packDirectory))
            {
                ValidatePathRootAndReparse(storageRoot, packDirectory);
                CleanupOwnedStagingContainer(
                    storageRoot,
                    tempContainerDir,
                    ownerToken,
                    packId,
                    ownerMarkerWritten,
                    ownerMarkerCreated);
                stagingDirectoryCreated = false;

                WorkflowEvidencePack verifiedExisting =
                    await this.VerifyPackAsync(
                        capturedPersistenceRevision,
                        packId,
                        manifestFingerprint,
                        cancellationToken_).ConfigureAwait(false);

                return verifiedExisting;
            }

            ValidateStagingContainerOwnership(
                storageRoot,
                tempContainerDir,
                ownerToken,
                packId,
                manifestBytes,
                manifestFingerprint);

            try
            {
                Directory.Move(tempPayloadDir, packDirectory);
            }
            catch (IOException)
            {
                if (Directory.Exists(packDirectory))
                {
                    ValidatePathRootAndReparse(storageRoot, packDirectory);
                    CleanupOwnedStagingContainer(
                        storageRoot,
                        tempContainerDir,
                        ownerToken,
                        packId,
                        ownerMarkerWritten,
                        ownerMarkerCreated);
                    stagingDirectoryCreated = false;

                    WorkflowEvidencePack verifiedExisting =
                        await this.VerifyPackAsync(
                            capturedPersistenceRevision,
                            packId,
                            manifestFingerprint,
                            cancellationToken_).ConfigureAwait(false);

                    return verifiedExisting;
                }

                throw;
            }

            CleanupOwnedStagingContainer(
                storageRoot,
                tempContainerDir,
                ownerToken,
                packId,
                ownerMarkerWritten,
                ownerMarkerCreated);
            stagingDirectoryCreated = false;
        }
        finally
        {
            if (stagingDirectoryCreated || Directory.Exists(tempContainerDir))
            {
                try
                {
                    CleanupOwnedStagingContainer(
                        storageRoot,
                        tempContainerDir,
                        ownerToken,
                        packId,
                        ownerMarkerWritten,
                        ownerMarkerCreated);
                }
                catch
                {
                }
            }
        }

        return new WorkflowEvidencePack(
            packId,
            workflowId,
            capturedPersistenceRevision,
            manifestFingerprint,
            packDirectory,
            manifestPath,
            manifest);
    }

    public Task<WorkflowEvidencePack> VerifyPackAsync(
        long capturedPersistenceRevision_,
        string packId_,
        string expectedManifestFingerprint_,
        CancellationToken cancellationToken_ = default)
    {
        cancellationToken_.ThrowIfCancellationRequested();

        if (capturedPersistenceRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedPersistenceRevision_),
                capturedPersistenceRevision_,
                "Captured persistence revision must be greater than zero.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            packId_,
            nameof(packId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            expectedManifestFingerprint_,
            nameof(expectedManifestFingerprint_));

        if (packId_.Length != 64 || !IsLowerHex(packId_))
        {
            throw new ArgumentException(
                "PackId must be exactly 64 lowercase hexadecimal characters.",
                nameof(packId_));
        }

        if (expectedManifestFingerprint_.Length != 64 || !IsLowerHex(expectedManifestFingerprint_))
        {
            throw new ArgumentException(
                "Expected manifest fingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(expectedManifestFingerprint_));
        }

        WorkflowId workflowId =
            this._persistenceStore.WorkflowId;
        string storageRoot =
            Path.GetFullPath(this._persistenceStore.StorageRoot);

        ValidatePathRootAndReparse(storageRoot, storageRoot);

        string encodedWorkflowId =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    workflowId.Value)).ToLowerInvariant();

        string capturedRevD20 =
            capturedPersistenceRevision_.ToString("D20");

        string packDirectory =
            Path.Combine(
                storageRoot,
                "workflows",
                encodedWorkflowId,
                "evidence-packs",
                capturedRevD20,
                packId_);

        ValidatePathRootAndReparse(storageRoot, packDirectory);

        if (!Directory.Exists(packDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Evidence pack directory not found: '{packDirectory}'.");
        }

        string manifestPath =
            Path.Combine(
                packDirectory,
                "manifest.json");

        ValidatePathRootAndReparse(storageRoot, manifestPath);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"Evidence pack manifest file not found: '{manifestPath}'.");
        }

        byte[] manifestBytes =
            ReadFileBytesAndValidateNoBom(manifestPath);

        string actualManifestFingerprint =
            WorkflowEvidenceManifestJson.ComputeSha256LowerHex(
                manifestBytes);

        if (!string.Equals(
                actualManifestFingerprint,
                expectedManifestFingerprint_,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Manifest fingerprint '{actualManifestFingerprint}' does not match expected '{expectedManifestFingerprint_}'.");
        }

        WorkflowEvidenceManifest manifest =
            WorkflowEvidenceManifestJson.Deserialize(
                manifestBytes,
                workflowId);

        byte[] canonicalBytes =
            WorkflowEvidenceManifestJson.Serialize(
                manifest);

        if (!manifestBytes.AsSpan().SequenceEqual(canonicalBytes))
        {
            throw new InvalidOperationException(
                "Manifest JSON bytes are not strictly canonical (non-canonical whitespace, escaping, or formatting detected).");
        }

        if (!string.Equals(manifest.PackId, packId_, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Manifest packId '{manifest.PackId}' does not match expected '{packId_}'.");
        }

        if (manifest.CapturedPersistenceRevision != capturedPersistenceRevision_)
        {
            throw new InvalidOperationException(
                $"Manifest captured revision {manifest.CapturedPersistenceRevision} does not match expected {capturedPersistenceRevision_}.");
        }

        if (manifest.SourceRecords.Count == 0)
        {
            throw new InvalidOperationException(
                "Evidence manifest contains empty source records.");
        }

        // Verify ordinal order and uniqueness of source records in manifest
        for (int i = 1; i < manifest.SourceRecords.Count; i++)
        {
            int comparison =
                string.CompareOrdinal(
                    manifest.SourceRecords[i - 1].RelativePath,
                    manifest.SourceRecords[i].RelativePath);

            if (comparison >= 0)
            {
                throw new InvalidOperationException(
                    $"Manifest source records are not strictly ordinally sorted: '{manifest.SourceRecords[i - 1].RelativePath}' vs '{manifest.SourceRecords[i].RelativePath}'.");
            }
        }

        // Verify sourceSetFingerprint
        string recomputedSourceSetFingerprint =
            WorkflowEvidenceManifestJson.ComputeSourceSetFingerprint(
                manifest.SourceRecords);

        if (!string.Equals(
                recomputedSourceSetFingerprint,
                manifest.SourceSetFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Source set fingerprint mismatch in evidence manifest.");
        }

        // Verify packId
        string recomputedPackId =
            WorkflowEvidenceManifestJson.ComputePackId(
                manifest.AlgorithmId,
                manifest.SchemaId,
                manifest.SchemaVersion,
                manifest.WorkflowId.Value,
                manifest.SourceImplementationPlanRevision,
                manifest.CapturedPersistenceRevision,
                manifest.SourceSetFingerprint);

        if (!string.Equals(
                recomputedPackId,
                manifest.PackId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Deterministic packId mismatch in evidence manifest.");
        }

        // 1. Separate captured sources and verify disk hashes
        Dictionary<long, ParsedPersistenceRecord> capturedSnapshots = [];
        Dictionary<(string TaskIdHex, long BaseRev), List<(long Sequence, byte[] Payload)>> cpJournalFiles = [];
        Dictionary<(string TaskIdHex, long BaseRev), List<(long Sequence, byte[] Payload)>> gateJournalFiles = [];

        foreach (WorkflowEvidenceSourceRecord sourceRecord in manifest.SourceRecords)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            ValidateCanonicalRelativePath(sourceRecord.RelativePath);

            string fullSourcePath =
                Path.Combine(
                    storageRoot,
                    sourceRecord.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            ValidatePathRootAndReparse(storageRoot, fullSourcePath);

            if (!File.Exists(fullSourcePath))
            {
                throw new InvalidOperationException(
                    $"Captured source file does not exist on disk: '{sourceRecord.RelativePath}'.");
            }

            byte[] sourceBytes =
                ReadFileBytesAndValidateNoBom(fullSourcePath);

            if (sourceBytes.Length != sourceRecord.ByteLength)
            {
                throw new InvalidOperationException(
                    $"Source byte length mismatch for '{sourceRecord.RelativePath}'. Expected {sourceRecord.ByteLength}, found {sourceBytes.Length}.");
            }

            string actualSourceSha =
                WorkflowEvidenceManifestJson.ComputeSha256LowerHex(
                    sourceBytes);

            if (!string.Equals(
                    actualSourceSha,
                    sourceRecord.Sha256Fingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Source SHA-256 fingerprint mismatch for '{sourceRecord.RelativePath}'. Expected '{sourceRecord.Sha256Fingerprint}', found '{actualSourceSha}'.");
            }

            // Categorize source path
            Match prMatch = _persistenceRecordPathPattern.Match(sourceRecord.RelativePath);
            if (prMatch.Success)
            {
                if (!string.Equals(prMatch.Groups[1].Value, encodedWorkflowId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Persistence record path has invalid workflowId hex: '{sourceRecord.RelativePath}'.");
                }

                long rev = long.Parse(prMatch.Groups[2].Value);
                ParsedPersistenceRecord parsed = ParsePersistenceRecord(sourceBytes);
                if (parsed.Revision != rev)
                {
                    throw new InvalidOperationException($"Persistence record internal revision {parsed.Revision} does not match file path revision {rev}.");
                }

                if (!string.Equals(parsed.WorkflowId, workflowId.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Persistence record workflowId '{parsed.WorkflowId}' does not match expected '{workflowId.Value}'.");
                }

                if (!string.Equals(parsed.SchemaId, WorkflowPersistenceStore.SchemaId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Persistence record schemaId '{parsed.SchemaId}' does not match expected '{WorkflowPersistenceStore.SchemaId}'.");
                }

                if (parsed.SchemaVersion != WorkflowPersistenceStore.CurrentSchemaVersion)
                {
                    throw new InvalidOperationException($"Persistence record schemaVersion {parsed.SchemaVersion} does not match expected {WorkflowPersistenceStore.CurrentSchemaVersion}.");
                }

                if (!string.Equals(parsed.Event.WorkflowId, workflowId.Value, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Persistence record event workflowId '{parsed.Event.WorkflowId}' does not match expected '{workflowId.Value}'.");
                }

                if (parsed.Event.Sequence != rev)
                {
                    throw new InvalidOperationException($"Persistence record event sequence {parsed.Event.Sequence} does not match expected {rev}.");
                }

                capturedSnapshots[rev] = parsed;
                continue;
            }

            Match cpMatch = _checkpointRecordPathPattern.Match(sourceRecord.RelativePath);
            if (cpMatch.Success)
            {
                if (!string.Equals(cpMatch.Groups[1].Value, encodedWorkflowId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Checkpoint journal record path has invalid workflowId hex: '{sourceRecord.RelativePath}'.");
                }

                string tIdHex = cpMatch.Groups[2].Value;
                long baseRev = long.Parse(cpMatch.Groups[3].Value);
                long seq = long.Parse(cpMatch.Groups[4].Value);

                var key = (tIdHex, baseRev);
                if (!cpJournalFiles.TryGetValue(key, out var list))
                {
                    list = [];
                    cpJournalFiles[key] = list;
                }

                list.Add((seq, sourceBytes));
                continue;
            }

            Match gateMatch = _gateRecordPathPattern.Match(sourceRecord.RelativePath);
            if (gateMatch.Success)
            {
                if (!string.Equals(gateMatch.Groups[1].Value, encodedWorkflowId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Gate journal record path has invalid workflowId hex: '{sourceRecord.RelativePath}'.");
                }

                string tIdHex = gateMatch.Groups[2].Value;
                long baseRev = long.Parse(gateMatch.Groups[3].Value);
                long seq = long.Parse(gateMatch.Groups[4].Value);

                var key = (tIdHex, baseRev);
                if (!gateJournalFiles.TryGetValue(key, out var list))
                {
                    list = [];
                    gateJournalFiles[key] = list;
                }

                list.Add((seq, sourceBytes));
                continue;
            }

            throw new InvalidOperationException(
                $"Unrecognized or noncanonical source record path in manifest: '{sourceRecord.RelativePath}'.");
        }

        // 2. Verify contiguous persistence snapshots 1..capturedPersistenceRevision_
        for (long rev = 1; rev <= capturedPersistenceRevision_; rev++)
        {
            if (!capturedSnapshots.TryGetValue(rev, out ParsedPersistenceRecord? parsed))
            {
                throw new InvalidOperationException(
                    $"Missing captured persistence record for revision {rev}.");
            }

            if (rev == capturedPersistenceRevision_)
            {
                if ((int) manifest.WorkflowStatus != parsed.State.Status)
                {
                    throw new InvalidOperationException(
                        $"Captured persistence state status '{parsed.State.Status}' does not match manifest status '{(int) manifest.WorkflowStatus}'.");
                }

                if (parsed.State.SourceImplementationPlanRevision != manifest.SourceImplementationPlanRevision)
                {
                    throw new InvalidOperationException(
                        $"Captured persistence state plan revision {parsed.State.SourceImplementationPlanRevision} does not match manifest plan revision {manifest.SourceImplementationPlanRevision}.");
                }
            }
        }

        if (capturedSnapshots.Count != capturedPersistenceRevision_)
        {
            throw new InvalidOperationException(
                $"Captured persistence records count {capturedSnapshots.Count} does not match captured revision {capturedPersistenceRevision_}.");
        }

        // 3. Verify events match snapshots
        if (manifest.Events.Count != capturedPersistenceRevision_)
        {
            throw new InvalidOperationException(
                $"Manifest event count {manifest.Events.Count} does not match captured revision {capturedPersistenceRevision_}.");
        }

        for (int i = 0; i < manifest.Events.Count; i++)
        {
            WorkflowEvidenceEvent ev = manifest.Events[i];
            long expectedRev = i + 1;
            if (ev.Revision != expectedRev)
            {
                throw new InvalidOperationException(
                    $"Event at index {i} has revision {ev.Revision}, expected {expectedRev}.");
            }

            if (!ev.WorkflowId.Equals(workflowId))
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} workflowId '{ev.WorkflowId.Value}' does not match expected '{workflowId.Value}'.");
            }

            if (!Enum.IsDefined(typeof(WorkflowExecutionEventKind), ev.Kind))
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} has invalid event kind '{(int) ev.Kind}'.");
            }

            if (ev.PreviousWorkflowStatus.HasValue && !Enum.IsDefined(typeof(WorkflowStatus), ev.PreviousWorkflowStatus.Value))
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} has invalid previous workflow status '{(int) ev.PreviousWorkflowStatus.Value}'.");
            }

            if (ev.TargetWorkflowStatus.HasValue && !Enum.IsDefined(typeof(WorkflowStatus), ev.TargetWorkflowStatus.Value))
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} has invalid target workflow status '{(int) ev.TargetWorkflowStatus.Value}'.");
            }

            if (ev.PreviousStepStatus.HasValue && !Enum.IsDefined(typeof(WorkflowStepStatus), ev.PreviousStepStatus.Value))
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} has invalid previous step status '{(int) ev.PreviousStepStatus.Value}'.");
            }

            if (ev.TargetStepStatus.HasValue && !Enum.IsDefined(typeof(WorkflowStepStatus), ev.TargetStepStatus.Value))
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} has invalid target step status '{(int) ev.TargetStepStatus.Value}'.");
            }

            ParsedPersistenceEvent snapEvent = capturedSnapshots[expectedRev].Event;
            if (snapEvent.Sequence != expectedRev)
            {
                throw new InvalidOperationException(
                    $"Captured persistence event sequence {snapEvent.Sequence} does not match expected {expectedRev}.");
            }

            if (!string.Equals(snapEvent.WorkflowId, workflowId.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Captured persistence event workflowId '{snapEvent.WorkflowId}' does not match expected '{workflowId.Value}'.");
            }

            if ((int) ev.Kind != snapEvent.Kind ||
                !string.Equals(ev.TaskId, snapEvent.TaskId, StringComparison.Ordinal) ||
                (ev.PreviousWorkflowStatus.HasValue ? (int) ev.PreviousWorkflowStatus.Value : (int?) null) != snapEvent.PreviousWorkflowStatus ||
                (ev.TargetWorkflowStatus.HasValue ? (int) ev.TargetWorkflowStatus.Value : (int?) null) != snapEvent.TargetWorkflowStatus ||
                (ev.PreviousStepStatus.HasValue ? (int) ev.PreviousStepStatus.Value : (int?) null) != snapEvent.PreviousStepStatus ||
                (ev.TargetStepStatus.HasValue ? (int) ev.TargetStepStatus.Value : (int?) null) != snapEvent.TargetStepStatus)
            {
                throw new InvalidOperationException(
                    $"Manifest event at revision {expectedRev} does not match persistence record event.");
            }
        }

        // 3b. Validate full historical persistence semantics under P01 state machine rules
        ValidateCapturedPersistenceHistory(capturedSnapshots, capturedPersistenceRevision_);

        // 4. Reconstruct P09 Checkpoint Journals strictly from captured source bytes
        Dictionary<string, List<WorkflowCheckpointJournalEvidence>> reconstructedCpByTaskHex = [];

        foreach (var (key, rawRecords) in cpJournalFiles)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            (string tIdHex, long baseRev) = key;
            if (baseRev > capturedPersistenceRevision_)
            {
                throw new InvalidOperationException(
                    $"Captured checkpoint journal base persistence revision {baseRev} exceeds captured revision {capturedPersistenceRevision_}.");
            }

            if (!capturedSnapshots.TryGetValue(baseRev, out ParsedPersistenceRecord? baseParsedSnap))
            {
                throw new InvalidOperationException(
                    $"Base snapshot {baseRev} not found for checkpoint journal.");
            }

            rawRecords.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
            long expectedSeq = 1;
            List<JournalRecord> parsedRecords = [];

            foreach ((long seq, byte[] payload) in rawRecords)
            {
                if (seq != expectedSeq)
                {
                    throw new InvalidOperationException(
                        $"Checkpoint journal records sequence gap: expected {expectedSeq}, found {seq}.");
                }

                JournalRecord parsedRec = DeserializeCheckpointJournalRecord(payload);
                if (parsedRec.Sequence != expectedSeq)
                {
                    throw new InvalidOperationException(
                        $"Internal checkpoint journal record sequence {parsedRec.Sequence} does not match expected {expectedSeq}.");
                }

                if (expectedSeq == 1)
                {
                    if (parsedRec is not CheckpointStartedRecord started)
                    {
                        throw new InvalidOperationException(
                            "First checkpoint journal record must be CheckpointStarted.");
                    }

                    if (!string.Equals(started.WorkflowId, workflowId.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Checkpoint started record workflowId '{started.WorkflowId}' does not match expected '{workflowId.Value}'.");
                    }

                    if (started.BasePersistenceRevision != baseRev)
                    {
                        throw new InvalidOperationException(
                            $"Checkpoint started record base revision {started.BasePersistenceRevision} does not match {baseRev}.");
                    }

                    string actualTaskIdHex =
                        Convert.ToHexString(Encoding.UTF8.GetBytes(started.TaskId)).ToLowerInvariant();
                    if (!string.Equals(actualTaskIdHex, tIdHex, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Checkpoint started record taskId '{started.TaskId}' does not match directory taskIdHex '{tIdHex}'.");
                    }

                    string expectedBaseFp =
                        ComputeP09BaseStateFingerprint(baseParsedSnap.State);
                    if (!string.Equals(started.BaseWorkflowStateFingerprint, expectedBaseFp, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Checkpoint started record base state fingerprint mismatch for task '{started.TaskId}' at revision {baseRev}.");
                    }
                }

                parsedRecords.Add(parsedRec);
                expectedSeq++;
            }

            CheckpointStartedRecord startedRecord =
                (CheckpointStartedRecord) parsedRecords[0];

            ExecutionCompletedRecord? completedRec =
                parsedRecords.OfType<ExecutionCompletedRecord>().LastOrDefault();

            ProjectionCompletedRecord? projCompletedRec =
                parsedRecords.OfType<ProjectionCompletedRecord>().LastOrDefault();

            List<WorkflowJournalRecordEvidence> recordEvidences = [];
            foreach (JournalRecord jr in parsedRecords)
            {
                recordEvidences.Add(MapCheckpointJournalRecord(jr));
            }

            WorkflowCheckpointJournalEvidence reconstructedCp = new(
                baseRev,
                startedRecord.BaseWorkflowStateFingerprint,
                startedRecord.InputFingerprint,
                startedRecord.RegistryFingerprint,
                completedRec is not null,
                completedRec?.FinalStateFingerprint,
                completedRec?.AttemptCount,
                completedRec?.BudgetExhausted,
                completedRec?.ExpectedProjectionTransitionCount,
                projCompletedRec is not null,
                projCompletedRec?.FinalPersistenceRevision,
                recordEvidences);

            if (!reconstructedCpByTaskHex.TryGetValue(tIdHex, out var cpList))
            {
                cpList = [];
                reconstructedCpByTaskHex[tIdHex] = cpList;
            }

            cpList.Add(reconstructedCp);
        }

        // Sort reconstructed checkpoint journals by baseRevision ascending
        foreach (var list in reconstructedCpByTaskHex.Values)
        {
            list.Sort(static (a, b) => a.BasePersistenceRevision.CompareTo(b.BasePersistenceRevision));
        }

        // 5. Reconstruct P10 Gate Journals strictly from captured source bytes
        Dictionary<string, List<WorkflowGateJournalEvidence>> reconstructedGateByTaskHex = [];

        foreach (var (key, rawRecords) in gateJournalFiles)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            (string tIdHex, long baseRev) = key;
            if (baseRev > capturedPersistenceRevision_)
            {
                throw new InvalidOperationException(
                    $"Captured gate journal base persistence revision {baseRev} exceeds captured revision {capturedPersistenceRevision_}.");
            }

            if (!capturedSnapshots.TryGetValue(baseRev, out ParsedPersistenceRecord? baseParsedSnap))
            {
                throw new InvalidOperationException(
                    $"Base snapshot {baseRev} not found for gate journal.");
            }

            if (rawRecords.Count > 2)
            {
                throw new InvalidOperationException(
                    $"Gate journal record count exceeds maximum of 2 records.");
            }

            rawRecords.Sort(static (a, b) => a.Sequence.CompareTo(b.Sequence));
            long expectedSeq = 1;
            List<WorkflowGateJournalRecord> parsedRecords = [];

            foreach ((long seq, byte[] payload) in rawRecords)
            {
                if (seq != expectedSeq)
                {
                    throw new InvalidOperationException(
                        $"Gate journal records sequence gap: expected {expectedSeq}, found {seq}.");
                }

                WorkflowGateJournalRecord parsedRec = DeserializeGateJournalRecord(payload);
                if (parsedRec.Sequence != expectedSeq)
                {
                    throw new InvalidOperationException(
                        $"Internal gate journal record sequence {parsedRec.Sequence} does not match expected {expectedSeq}.");
                }

                if (expectedSeq == 1)
                {
                    if (parsedRec is not ChallengeCreatedRecord ch)
                    {
                        throw new InvalidOperationException(
                            "First gate journal record must be ChallengeCreated.");
                    }

                    if (!string.Equals(ch.WorkflowId, workflowId.Value, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Gate challenge workflowId '{ch.WorkflowId}' does not match expected '{workflowId.Value}'.");
                    }

                    if (ch.BasePersistenceRevision != baseRev)
                    {
                        throw new InvalidOperationException(
                            $"Gate challenge base revision {ch.BasePersistenceRevision} does not match {baseRev}.");
                    }

                    string actualTaskIdHex =
                        Convert.ToHexString(Encoding.UTF8.GetBytes(ch.TaskId)).ToLowerInvariant();
                    if (!string.Equals(actualTaskIdHex, tIdHex, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Gate challenge taskId '{ch.TaskId}' does not match directory taskIdHex '{tIdHex}'.");
                    }

                    string expectedGateBaseFp =
                        ComputeP10BaseStateFingerprint(baseParsedSnap.State);
                    if (!string.Equals(ch.BaseStateFingerprint, expectedGateBaseFp, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Gate challenge base state fingerprint mismatch for task '{ch.TaskId}' at revision {baseRev}.");
                    }

                    ExecutionPermission seq1Perm = (ExecutionPermission) ch.RequiredPermission;
                    string expectedGateId =
                        WorkflowGateCoordinator.ComputeGateId(
                            workflowId,
                            ch.TaskId,
                            baseRev,
                            baseParsedSnap.State.SourceImplementationPlanRevision,
                            seq1Perm,
                            expectedGateBaseFp,
                            ch.InputFingerprint,
                            ch.RegistryFingerprint,
                            ch.PolicyFingerprint);

                    if (!string.Equals(ch.GateId, expectedGateId, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Gate challenge GateId mismatch for task '{ch.TaskId}' at revision {baseRev}.");
                    }
                }
                else if (expectedSeq == 2)
                {
                    if (parsedRec is not AuthorizationGrantedRecord && parsedRec is not AuthorizationDeniedRecord)
                    {
                        throw new InvalidOperationException(
                            "Second gate journal record must be AuthorizationGranted or AuthorizationDenied.");
                    }
                }

                parsedRecords.Add(parsedRec);
                expectedSeq++;
            }

            ChallengeCreatedRecord chRec = (ChallengeCreatedRecord) parsedRecords[0];
            ExecutionPermission reqPerm = (ExecutionPermission) chRec.RequiredPermission;

            WorkflowGateChallenge challenge = new(
                chRec.GateId,
                new WorkflowId(chRec.WorkflowId),
                chRec.TaskId,
                chRec.BasePersistenceRevision,
                baseParsedSnap.State.SourceImplementationPlanRevision,
                reqPerm,
                chRec.GateOrdinal,
                chRec.BaseStateFingerprint,
                chRec.InputFingerprint,
                chRec.RegistryFingerprint,
                chRec.PolicyFingerprint);

            bool? authorized = null;
            string? principalId = null;
            string? mechanismId = null;
            string? evidenceFingerprint = null;

            AuthorizationGrantedRecord? granted =
                parsedRecords.OfType<AuthorizationGrantedRecord>().FirstOrDefault();
            AuthorizationDeniedRecord? denied =
                parsedRecords.OfType<AuthorizationDeniedRecord>().FirstOrDefault();

            if (granted is not null && denied is not null)
            {
                throw new InvalidOperationException(
                    $"Gate journal at revision {baseRev} contains both an authorization grant and denial.");
            }

            if (granted is not null)
            {
                if (!string.Equals(
                        granted.GateId,
                        chRec.GateId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gate grant record GateId '{granted.GateId}' does not match challenge GateId '{chRec.GateId}'.");
                }

                authorized = true;
                principalId = granted.PrincipalId;
                mechanismId = granted.MechanismId;
                evidenceFingerprint = granted.EvidenceFingerprint;
            }
            else if (denied is not null)
            {
                if (!string.Equals(
                        denied.GateId,
                        chRec.GateId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Gate denial record GateId '{denied.GateId}' does not match challenge GateId '{chRec.GateId}'.");
                }

                authorized = false;
                principalId = denied.PrincipalId;
                mechanismId = denied.MechanismId;
                evidenceFingerprint = denied.EvidenceFingerprint;
            }

            WorkflowGateJournalEvidence reconstructedGate = new(
                baseRev,
                challenge,
                authorized,
                principalId,
                mechanismId,
                evidenceFingerprint);

            if (!reconstructedGateByTaskHex.TryGetValue(tIdHex, out var gateList))
            {
                gateList = [];
                reconstructedGateByTaskHex[tIdHex] = gateList;
            }

            gateList.Add(reconstructedGate);
        }

        // Sort reconstructed gate journals by baseRevision ascending
        foreach (var list in reconstructedGateByTaskHex.Values)
        {
            list.Sort(static (a, b) => a.BasePersistenceRevision.CompareTo(b.BasePersistenceRevision));
        }

        // 6. Validate task membership and deep claim equivalence against manifest.Tasks
        ParsedPersistenceState finalSnapState =
            capturedSnapshots[capturedPersistenceRevision_].State;

        if (manifest.Tasks.Count != finalSnapState.Steps.Length)
        {
            throw new InvalidOperationException(
                $"Manifest task count {manifest.Tasks.Count} does not match persistence step count {finalSnapState.Steps.Length}.");
        }

        HashSet<string> taskIdsInWorkflow = [];

        for (int i = 0; i < finalSnapState.Steps.Length; i++)
        {
            (string stepTaskId, int stepStatus) = finalSnapState.Steps[i];
            taskIdsInWorkflow.Add(stepTaskId);
            WorkflowTaskEvidence manifestTask = manifest.Tasks[i];

            if (!string.Equals(manifestTask.TaskId, stepTaskId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Manifest task at index {i} has taskId '{manifestTask.TaskId}', expected '{stepTaskId}'.");
            }

            if ((int) manifestTask.StepStatus != stepStatus)
            {
                throw new InvalidOperationException(
                    $"Manifest task '{manifestTask.TaskId}' step status {(int) manifestTask.StepStatus} does not match persistence state status {stepStatus}.");
            }

            string taskIdHex =
                Convert.ToHexString(Encoding.UTF8.GetBytes(stepTaskId)).ToLowerInvariant();

            // Compare reconstructed Checkpoint Journals
            List<WorkflowCheckpointJournalEvidence> reconstructedCpList =
                reconstructedCpByTaskHex.TryGetValue(taskIdHex, out var rCp) ? rCp : [];

            if (manifestTask.CheckpointJournals.Count != reconstructedCpList.Count)
            {
                throw new InvalidOperationException(
                    $"Manifest task '{stepTaskId}' has {manifestTask.CheckpointJournals.Count} checkpoint journals, source reconstructs {reconstructedCpList.Count}.");
            }

            for (int c = 0; c < reconstructedCpList.Count; c++)
            {
                WorkflowCheckpointJournalEvidence expectedCp = reconstructedCpList[c];
                WorkflowCheckpointJournalEvidence actualCp = manifestTask.CheckpointJournals[c];

                if (!AreCheckpointJournalsEquivalent(expectedCp, actualCp))
                {
                    throw new InvalidOperationException(
                        $"Manifest checkpoint journal claims do not match source-reconstructed facts for task '{stepTaskId}' at base revision {expectedCp.BasePersistenceRevision}.");
                }
            }

            // Compare reconstructed Gate Journals
            List<WorkflowGateJournalEvidence> reconstructedGateList =
                reconstructedGateByTaskHex.TryGetValue(taskIdHex, out var rGj) ? rGj : [];

            if (manifestTask.GateJournals.Count != reconstructedGateList.Count)
            {
                throw new InvalidOperationException(
                    $"Manifest task '{stepTaskId}' has {manifestTask.GateJournals.Count} gate journals, source reconstructs {reconstructedGateList.Count}.");
            }

            for (int g = 0; g < reconstructedGateList.Count; g++)
            {
                WorkflowGateJournalEvidence expectedGj = reconstructedGateList[g];
                WorkflowGateJournalEvidence actualGj = manifestTask.GateJournals[g];

                if (!AreGateJournalsEquivalent(expectedGj, actualGj))
                {
                    throw new InvalidOperationException(
                        $"Manifest gate journal claims do not match source-reconstructed facts for task '{stepTaskId}' at base revision {expectedGj.BasePersistenceRevision}.");
                }
            }
        }

        // 7. Verify no captured journal sources belong to non-existent tasks
        foreach (string tIdHex in reconstructedCpByTaskHex.Keys)
        {
            bool matchesKnown = taskIdsInWorkflow.Any(tId =>
                string.Equals(Convert.ToHexString(Encoding.UTF8.GetBytes(tId)).ToLowerInvariant(), tIdHex, StringComparison.Ordinal));

            if (!matchesKnown)
            {
                throw new InvalidOperationException(
                    $"Captured checkpoint journals contain records for unknown task with hex '{tIdHex}'.");
            }
        }

        foreach (string tIdHex in reconstructedGateByTaskHex.Keys)
        {
            bool matchesKnown = taskIdsInWorkflow.Any(tId =>
                string.Equals(Convert.ToHexString(Encoding.UTF8.GetBytes(tId)).ToLowerInvariant(), tIdHex, StringComparison.Ordinal));

            if (!matchesKnown)
            {
                throw new InvalidOperationException(
                    $"Captured gate journals contain records for unknown task with hex '{tIdHex}'.");
            }
        }

        return Task.FromResult(
            new WorkflowEvidencePack(
                packId_,
                workflowId,
                capturedPersistenceRevision_,
                expectedManifestFingerprint_,
                packDirectory,
                manifestPath,
                manifest));
    }

    public Task<WorkflowEvidencePack> VerifyPackAsync(
        WorkflowEvidencePack pack_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            pack_,
            nameof(pack_));

        return this.VerifyPackAsync(
            pack_.CapturedPersistenceRevision,
            pack_.PackId,
            pack_.ManifestFingerprint,
            cancellationToken_);
    }

    private void ValidatePrepublicationSourceDrift(
        string storageRoot_,
        WorkflowId workflowId_,
        long capturedPersistenceRevision_,
        WorkflowState currentState_,
        IReadOnlyList<WorkflowEvidenceSourceRecord> capturedSourceRecords_)
    {
        ValidatePathRootAndReparse(storageRoot_, storageRoot_);

        string encodedWorkflowId =
            Convert.ToHexString(
                Encoding.UTF8.GetBytes(
                    workflowId_.Value)).ToLowerInvariant();

        string recordsDir =
            Path.Combine(storageRoot_, "workflows", encodedWorkflowId, "records");
        ValidatePathRootAndReparse(storageRoot_, recordsDir);

        // 1. Re-check latest persistence snapshot
        WorkflowPersistenceSnapshot? latestSnapshot;
        try
        {
            latestSnapshot = this._persistenceStore.Load();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Current workflow persistence state could not be reloaded prior to publication.",
                exception);
        }

        if (latestSnapshot is null ||
            latestSnapshot.Revision != capturedPersistenceRevision_)
        {
            throw new InvalidOperationException(
                $"Current workflow persistence revision drifted from {capturedPersistenceRevision_} to {latestSnapshot?.Revision} prior to publication.");
        }

        // 2. Re-check persistence record files
        for (long rev = 1; rev <= capturedPersistenceRevision_; rev++)
        {
            string prPath = Path.Combine(recordsDir, $"{rev:D20}.json");
            ValidatePathRootAndReparse(storageRoot_, prPath);
            if (!File.Exists(prPath))
            {
                throw new InvalidOperationException($"Persistence record {rev} was removed prior to publication.");
            }
        }

        string nextPrPath = Path.Combine(recordsDir, $"{(capturedPersistenceRevision_ + 1):D20}.json");
        ValidatePathRootAndReparse(storageRoot_, nextPrPath);
        if (File.Exists(nextPrPath))
        {
            throw new InvalidOperationException($"New persistence record was appended prior to publication.");
        }

        // 3. Re-enumerate eligible checkpoint and gate journals and check count
        HashSet<string> currentEligibleSourceRelPaths = [];
        for (long rev = 1; rev <= capturedPersistenceRevision_; rev++)
        {
            currentEligibleSourceRelPaths.Add($"workflows/{encodedWorkflowId}/records/{rev:D20}.json");
        }

        foreach (WorkflowStepState step in currentState_.Steps)
        {
            string taskId = step.TaskId;
            string taskIdHex =
                Convert.ToHexString(
                    Encoding.UTF8.GetBytes(
                        taskId)).ToLowerInvariant();

            string cpTaskDir =
                Path.Combine(storageRoot_, "workflows", encodedWorkflowId, "execution-checkpoints", taskIdHex);
            ValidatePathRootAndReparse(storageRoot_, cpTaskDir);

            IReadOnlyList<long> cpRevisions =
                WorkflowCheckpointJournal.DiscoverCandidateRevisions(
                    storageRoot_,
                    workflowId_,
                    taskId);

            foreach (long cpRev in cpRevisions.Where(r => r <= capturedPersistenceRevision_))
            {
                string recDir = WorkflowCheckpointJournal.GetRecordsDirectory(storageRoot_, workflowId_, taskId, cpRev);
                ValidatePathRootAndReparse(storageRoot_, recDir);
                if (Directory.Exists(recDir))
                {
                    string[] recFiles = Directory.GetFiles(recDir, "*.json");
                    foreach (string rf in recFiles)
                    {
                        ValidatePathRootAndReparse(storageRoot_, rf);
                        string fileName = Path.GetFileName(rf);
                        currentEligibleSourceRelPaths.Add($"workflows/{encodedWorkflowId}/execution-checkpoints/{taskIdHex}/{cpRev:D20}/records/{fileName}");
                    }
                }
            }

            string gateTaskDir =
                Path.Combine(storageRoot_, "workflows", encodedWorkflowId, "human-gates", taskIdHex);
            ValidatePathRootAndReparse(storageRoot_, gateTaskDir);

            IReadOnlyList<long> gateRevisions =
                WorkflowGateJournal.DiscoverCandidateRevisions(
                    storageRoot_,
                    workflowId_,
                    taskId);

            foreach (long gRev in gateRevisions.Where(r => r <= capturedPersistenceRevision_))
            {
                string recDir = WorkflowGateJournal.GetRecordsDirectory(storageRoot_, workflowId_, taskId, gRev);
                ValidatePathRootAndReparse(storageRoot_, recDir);
                if (Directory.Exists(recDir))
                {
                    string[] recFiles = Directory.GetFiles(recDir, "*.json");
                    foreach (string rf in recFiles)
                    {
                        ValidatePathRootAndReparse(storageRoot_, rf);
                        string fileName = Path.GetFileName(rf);
                        currentEligibleSourceRelPaths.Add($"workflows/{encodedWorkflowId}/human-gates/{taskIdHex}/{gRev:D20}/records/{fileName}");
                    }
                }
            }
        }

        if (currentEligibleSourceRelPaths.Count != capturedSourceRecords_.Count)
        {
            throw new InvalidOperationException(
                $"Eligible source record count drifted prior to publication. Captured: {capturedSourceRecords_.Count}, Current: {currentEligibleSourceRelPaths.Count}.");
        }

        // 4. Verify all captured source records exist and their byte lengths and hashes match
        foreach (WorkflowEvidenceSourceRecord sr in capturedSourceRecords_)
        {
            if (!currentEligibleSourceRelPaths.Contains(sr.RelativePath))
            {
                throw new InvalidOperationException(
                    $"Source record '{sr.RelativePath}' is no longer eligible or was removed prior to publication.");
            }

            string fullPath = Path.Combine(storageRoot_, sr.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            ValidatePathRootAndReparse(storageRoot_, fullPath);

            if (!File.Exists(fullPath))
            {
                throw new InvalidOperationException(
                    $"Source file '{sr.RelativePath}' was removed prior to publication.");
            }

            byte[] bytes = ReadFileBytesAndValidateNoBom(fullPath);
            if (bytes.Length != sr.ByteLength)
            {
                throw new InvalidOperationException(
                    $"Source file '{sr.RelativePath}' length drifted prior to publication.");
            }

            string sha = WorkflowEvidenceManifestJson.ComputeSha256LowerHex(bytes);
            if (!string.Equals(sha, sr.Sha256Fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Source file '{sr.RelativePath}' content drifted prior to publication.");
            }
        }
    }

    private static bool AreCheckpointJournalsEquivalent(
        WorkflowCheckpointJournalEvidence expected_,
        WorkflowCheckpointJournalEvidence actual_)
    {
        if (expected_.BasePersistenceRevision != actual_.BasePersistenceRevision ||
            !string.Equals(expected_.BaseWorkflowStateFingerprint, actual_.BaseWorkflowStateFingerprint, StringComparison.Ordinal) ||
            !string.Equals(expected_.InputFingerprint, actual_.InputFingerprint, StringComparison.Ordinal) ||
            !string.Equals(expected_.RegistryFingerprint, actual_.RegistryFingerprint, StringComparison.Ordinal) ||
            expected_.ExecutionCompleted != actual_.ExecutionCompleted ||
            !string.Equals(expected_.FinalStateFingerprint, actual_.FinalStateFingerprint, StringComparison.Ordinal) ||
            expected_.AttemptCount != actual_.AttemptCount ||
            expected_.BudgetExhausted != actual_.BudgetExhausted ||
            expected_.ExpectedProjectionTransitionCount != actual_.ExpectedProjectionTransitionCount ||
            expected_.ProjectionCompleted != actual_.ProjectionCompleted ||
            expected_.FinalPersistenceRevision != actual_.FinalPersistenceRevision ||
            expected_.Records.Count != actual_.Records.Count)
        {
            return false;
        }

        for (int i = 0; i < expected_.Records.Count; i++)
        {
            WorkflowJournalRecordEvidence expRec = expected_.Records[i];
            WorkflowJournalRecordEvidence actRec = actual_.Records[i];

            if (expRec.JournalSequence != actRec.JournalSequence ||
                !string.Equals(expRec.RecordKind, actRec.RecordKind, StringComparison.Ordinal) ||
                expRec.OperationOrdinal != actRec.OperationOrdinal ||
                !string.Equals(expRec.OperationId, actRec.OperationId, StringComparison.Ordinal) ||
                !string.Equals(expRec.OperationKind, actRec.OperationKind, StringComparison.Ordinal) ||
                expRec.InvocationGeneration != actRec.InvocationGeneration ||
                !string.Equals(expRec.RequestFingerprint, actRec.RequestFingerprint, StringComparison.Ordinal) ||
                !string.Equals(expRec.ProviderId, actRec.ProviderId, StringComparison.Ordinal) ||
                !string.Equals(expRec.ValidationExternalIdentityDigest, actRec.ValidationExternalIdentityDigest, StringComparison.Ordinal) ||
                expRec.IsNullReturn != actRec.IsNullReturn ||
                expRec.AgentStatus != actRec.AgentStatus ||
                !string.Equals(expRec.AgentOutputDigest, actRec.AgentOutputDigest, StringComparison.Ordinal) ||
                !string.Equals(expRec.AgentDiagnosticDigest, actRec.AgentDiagnosticDigest, StringComparison.Ordinal) ||
                expRec.ValidationPassed != actRec.ValidationPassed ||
                !string.Equals(expRec.ValidationEvidenceDigest, actRec.ValidationEvidenceDigest, StringComparison.Ordinal) ||
                !string.Equals(expRec.FinalStateFingerprint, actRec.FinalStateFingerprint, StringComparison.Ordinal) ||
                expRec.AttemptCount != actRec.AttemptCount ||
                expRec.BudgetExhausted != actRec.BudgetExhausted ||
                expRec.ExpectedProjectionTransitionCount != actRec.ExpectedProjectionTransitionCount ||
                expRec.FinalPersistenceRevision != actRec.FinalPersistenceRevision)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreGateJournalsEquivalent(
        WorkflowGateJournalEvidence expected_,
        WorkflowGateJournalEvidence actual_)
    {
        if (expected_.BasePersistenceRevision != actual_.BasePersistenceRevision ||
            expected_.Authorized != actual_.Authorized ||
            !string.Equals(expected_.PrincipalId, actual_.PrincipalId, StringComparison.Ordinal) ||
            !string.Equals(expected_.MechanismId, actual_.MechanismId, StringComparison.Ordinal) ||
            !string.Equals(expected_.EvidenceFingerprint, actual_.EvidenceFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        WorkflowGateChallenge expCh = expected_.Challenge;
        WorkflowGateChallenge actCh = actual_.Challenge;

        return string.Equals(expCh.GateId, actCh.GateId, StringComparison.Ordinal) &&
               expCh.WorkflowId.Equals(actCh.WorkflowId) &&
               string.Equals(expCh.TaskId, actCh.TaskId, StringComparison.Ordinal) &&
               expCh.BasePersistenceRevision == actCh.BasePersistenceRevision &&
               expCh.SourceImplementationPlanRevision == actCh.SourceImplementationPlanRevision &&
               expCh.RequiredPermission == actCh.RequiredPermission &&
               expCh.GateOrdinal == actCh.GateOrdinal &&
               string.Equals(expCh.BaseStateFingerprint, actCh.BaseStateFingerprint, StringComparison.Ordinal) &&
               string.Equals(expCh.InputFingerprint, actCh.InputFingerprint, StringComparison.Ordinal) &&
               string.Equals(expCh.RegistryFingerprint, actCh.RegistryFingerprint, StringComparison.Ordinal) &&
               string.Equals(expCh.PolicyFingerprint, actCh.PolicyFingerprint, StringComparison.Ordinal);
    }

    private static JournalRecord DeserializeCheckpointJournalRecord(
        byte[] payload_)
    {
        using JsonDocument doc = JsonDocument.Parse(payload_);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("schemaId", out JsonElement schemaIdElem) ||
            !string.Equals(schemaIdElem.GetString(), WorkflowCheckpointJournal.SchemaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unknown or invalid checkpoint journal schema ID.");
        }

        if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVerElem) ||
            schemaVerElem.GetInt32() != WorkflowCheckpointJournal.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Unsupported checkpoint journal schema version.");
        }

        if (!root.TryGetProperty("kind", out JsonElement kindElem))
        {
            throw new InvalidOperationException("Missing checkpoint journal record kind.");
        }

        string? kindString = kindElem.GetString();
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return kindString switch
        {
            nameof(JournalRecordKind.CheckpointStarted) =>
                JsonSerializer.Deserialize<CheckpointStartedRecord>(payload_, options)!,
            nameof(JournalRecordKind.InvocationIntent) =>
                JsonSerializer.Deserialize<InvocationIntentRecord>(payload_, options)!,
            nameof(JournalRecordKind.InvocationOutcome) =>
                JsonSerializer.Deserialize<InvocationOutcomeRecord>(payload_, options)!,
            nameof(JournalRecordKind.ReconciliationNotExecuted) =>
                JsonSerializer.Deserialize<ReconciliationNotExecutedRecord>(payload_, options)!,
            nameof(JournalRecordKind.ExecutionCompleted) =>
                JsonSerializer.Deserialize<ExecutionCompletedRecord>(payload_, options)!,
            nameof(JournalRecordKind.ProjectionCompleted) =>
                JsonSerializer.Deserialize<ProjectionCompletedRecord>(payload_, options)!,
            _ => throw new InvalidOperationException($"Unknown checkpoint journal record kind: '{kindString}'.")
        };
    }

    private static WorkflowGateJournalRecord DeserializeGateJournalRecord(
        byte[] payload_)
    {
        using JsonDocument doc = JsonDocument.Parse(payload_);
        JsonElement root = doc.RootElement;

        if (!root.TryGetProperty("schemaId", out JsonElement schemaIdElem) ||
            !string.Equals(schemaIdElem.GetString(), WorkflowGateJournal.SchemaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unknown or invalid gate journal schema ID.");
        }

        if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVerElem) ||
            schemaVerElem.GetInt32() != WorkflowGateJournal.CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Unsupported gate journal schema version.");
        }

        if (!root.TryGetProperty("kind", out JsonElement kindElem))
        {
            throw new InvalidOperationException("Missing gate journal record kind.");
        }

        string? kindString = kindElem.GetString();
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return kindString switch
        {
            nameof(WorkflowGateJournalRecordKind.ChallengeCreated) =>
                JsonSerializer.Deserialize<ChallengeCreatedRecord>(payload_, options)!,
            nameof(WorkflowGateJournalRecordKind.AuthorizationGranted) =>
                JsonSerializer.Deserialize<AuthorizationGrantedRecord>(payload_, options)!,
            nameof(WorkflowGateJournalRecordKind.AuthorizationDenied) =>
                JsonSerializer.Deserialize<AuthorizationDeniedRecord>(payload_, options)!,
            _ => throw new InvalidOperationException($"Unknown gate journal record kind: '{kindString}'.")
        };
    }

    private static WorkflowJournalRecordEvidence MapCheckpointJournalRecord(
        JournalRecord record_)
    {
        long seq = record_.Sequence;
        string kind = record_.Kind.ToString();

        return record_ switch
        {
            CheckpointStartedRecord started =>
                new WorkflowJournalRecordEvidence(
                    seq,
                    kind,
                    operationOrdinal_: null,
                    operationId_: null,
                    operationKind_: null,
                    invocationGeneration_: null,
                    requestFingerprint_: null,
                    providerId_: null,
                    validationExternalIdentityDigest_: null,
                    isNullReturn_: null,
                    agentStatus_: null,
                    agentOutputDigest_: null,
                    agentDiagnosticDigest_: null,
                    validationPassed_: null,
                    validationEvidenceDigest_: null,
                    finalStateFingerprint_: null,
                    attemptCount_: null,
                    budgetExhausted_: null,
                    expectedProjectionTransitionCount_: null,
                    finalPersistenceRevision_: null),

            InvocationIntentRecord intent =>
                MapInvocationIntentRecord(seq, kind, intent),

            InvocationOutcomeRecord outcome =>
                new WorkflowJournalRecordEvidence(
                    seq,
                    kind,
                    outcome.OperationOrdinal,
                    outcome.OperationId,
                    outcome.OperationKind,
                    outcome.InvocationGeneration,
                    outcome.RequestFingerprint,
                    providerId_: null,
                    validationExternalIdentityDigest_: null,
                    outcome.IsNullReturn,
                    outcome.AgentStatus,
                    outcome.AgentOutputText is not null ? WorkflowEvidenceManifestJson.ComputeSha256LowerHex(Encoding.UTF8.GetBytes(outcome.AgentOutputText)) : null,
                    outcome.AgentDiagnosticText is not null ? WorkflowEvidenceManifestJson.ComputeSha256LowerHex(Encoding.UTF8.GetBytes(outcome.AgentDiagnosticText)) : null,
                    outcome.ValidationPassed,
                    outcome.ValidationEvidence is not null ? WorkflowEvidenceManifestJson.ComputeSha256LowerHex(Encoding.UTF8.GetBytes(outcome.ValidationEvidence)) : null,
                    finalStateFingerprint_: null,
                    attemptCount_: null,
                    budgetExhausted_: null,
                    expectedProjectionTransitionCount_: null,
                    finalPersistenceRevision_: null),

            ReconciliationNotExecutedRecord rne =>
                new WorkflowJournalRecordEvidence(
                    seq,
                    kind,
                    rne.OperationOrdinal,
                    rne.OperationId,
                    operationKind_: null,
                    rne.InvocationGeneration,
                    requestFingerprint_: null,
                    providerId_: null,
                    validationExternalIdentityDigest_: null,
                    isNullReturn_: null,
                    agentStatus_: null,
                    agentOutputDigest_: null,
                    agentDiagnosticDigest_: null,
                    validationPassed_: null,
                    validationEvidenceDigest_: null,
                    finalStateFingerprint_: null,
                    attemptCount_: null,
                    budgetExhausted_: null,
                    expectedProjectionTransitionCount_: null,
                    finalPersistenceRevision_: null),

            ExecutionCompletedRecord ec =>
                new WorkflowJournalRecordEvidence(
                    seq,
                    kind,
                    operationOrdinal_: null,
                    operationId_: null,
                    operationKind_: null,
                    invocationGeneration_: null,
                    requestFingerprint_: null,
                    providerId_: null,
                    validationExternalIdentityDigest_: null,
                    isNullReturn_: null,
                    agentStatus_: null,
                    agentOutputDigest_: null,
                    agentDiagnosticDigest_: null,
                    validationPassed_: null,
                    validationEvidenceDigest_: null,
                    ec.FinalStateFingerprint,
                    ec.AttemptCount,
                    ec.BudgetExhausted,
                    ec.ExpectedProjectionTransitionCount,
                    finalPersistenceRevision_: null),

            ProjectionCompletedRecord pc =>
                new WorkflowJournalRecordEvidence(
                    seq,
                    kind,
                    operationOrdinal_: null,
                    operationId_: null,
                    operationKind_: null,
                    invocationGeneration_: null,
                    requestFingerprint_: null,
                    providerId_: null,
                    validationExternalIdentityDigest_: null,
                    isNullReturn_: null,
                    agentStatus_: null,
                    agentOutputDigest_: null,
                    agentDiagnosticDigest_: null,
                    validationPassed_: null,
                    validationEvidenceDigest_: null,
                    finalStateFingerprint_: null,
                    attemptCount_: null,
                    budgetExhausted_: null,
                    expectedProjectionTransitionCount_: null,
                    pc.FinalPersistenceRevision),

            _ => throw new InvalidOperationException($"Unsupported journal record kind: '{record_.Kind}'.")
        };
    }

    private static WorkflowJournalRecordEvidence MapInvocationIntentRecord(
        long sequence_,
        string kind_,
        InvocationIntentRecord intent_)
    {
        string? providerId = null;
        string? validationDigest = null;

        if (string.Equals(intent_.OperationKind, "Agent", StringComparison.Ordinal))
        {
            if (AgentProviderId.IsValid(intent_.ExternalIdentity))
            {
                providerId = intent_.ExternalIdentity;
            }
            else
            {
                validationDigest =
                    WorkflowEvidenceManifestJson.ComputeSha256LowerHex(
                        Encoding.UTF8.GetBytes(intent_.ExternalIdentity));
            }
        }
        else
        {
            validationDigest =
                WorkflowEvidenceManifestJson.ComputeSha256LowerHex(
                    Encoding.UTF8.GetBytes(intent_.ExternalIdentity));
        }

        return new WorkflowJournalRecordEvidence(
            sequence_,
            kind_,
            intent_.OperationOrdinal,
            intent_.OperationId,
            intent_.OperationKind,
            intent_.InvocationGeneration,
            intent_.RequestFingerprint,
            providerId,
            validationDigest,
            isNullReturn_: null,
            agentStatus_: null,
            agentOutputDigest_: null,
            agentDiagnosticDigest_: null,
            validationPassed_: null,
            validationEvidenceDigest_: null,
            finalStateFingerprint_: null,
            attemptCount_: null,
            budgetExhausted_: null,
            expectedProjectionTransitionCount_: null,
            finalPersistenceRevision_: null);
    }

    private static ParsedPersistenceRecord ParsePersistenceRecord(
        byte[] bytes_)
    {
        using JsonDocument doc = JsonDocument.Parse(bytes_);
        JsonElement root = doc.RootElement;

        string schemaId = root.GetProperty("schemaId").GetString()!;
        if (!string.Equals(schemaId, WorkflowPersistenceStore.SchemaId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Persistence record schemaId '{schemaId}' does not match expected '{WorkflowPersistenceStore.SchemaId}'.");
        }

        int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
        if (schemaVersion != WorkflowPersistenceStore.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Persistence record schemaVersion {schemaVersion} does not match expected {WorkflowPersistenceStore.CurrentSchemaVersion}.");
        }

        string workflowId = root.GetProperty("workflowId").GetString()!;
        long revision = root.GetProperty("revision").GetInt64();
        if (revision <= 0)
        {
            throw new InvalidOperationException(
                $"Persistence record revision must be positive, found {revision}.");
        }

        JsonElement ev = root.GetProperty("event");
        string evWorkflowId = ev.GetProperty("workflowId").GetString()!;
        long evSeq = ev.GetProperty("sequence").GetInt64();
        if (evSeq <= 0)
        {
            throw new InvalidOperationException(
                $"Persistence event sequence must be positive, found {evSeq}.");
        }

        int evKind = ev.GetProperty("kind").GetInt32();
        if (!Enum.IsDefined(typeof(WorkflowExecutionEventKind), evKind))
        {
            throw new InvalidOperationException(
                $"Persistence event kind {evKind} is not a valid WorkflowExecutionEventKind.");
        }

        string? evTaskId = ev.GetProperty("taskId").ValueKind == JsonValueKind.Null ? null : ev.GetProperty("taskId").GetString();
        int? prevWf = ev.GetProperty("previousWorkflowStatus").ValueKind == JsonValueKind.Null ? null : ev.GetProperty("previousWorkflowStatus").GetInt32();
        if (prevWf.HasValue && !Enum.IsDefined(typeof(WorkflowStatus), prevWf.Value))
        {
            throw new InvalidOperationException($"Persistence event previousWorkflowStatus {prevWf.Value} is invalid.");
        }

        int? targetWf = ev.GetProperty("targetWorkflowStatus").ValueKind == JsonValueKind.Null ? null : ev.GetProperty("targetWorkflowStatus").GetInt32();
        if (targetWf.HasValue && !Enum.IsDefined(typeof(WorkflowStatus), targetWf.Value))
        {
            throw new InvalidOperationException($"Persistence event targetWorkflowStatus {targetWf.Value} is invalid.");
        }

        int? prevStep = ev.GetProperty("previousStepStatus").ValueKind == JsonValueKind.Null ? null : ev.GetProperty("previousStepStatus").GetInt32();
        if (prevStep.HasValue && !Enum.IsDefined(typeof(WorkflowStepStatus), prevStep.Value))
        {
            throw new InvalidOperationException($"Persistence event previousStepStatus {prevStep.Value} is invalid.");
        }

        int? targetStep = ev.GetProperty("targetStepStatus").ValueKind == JsonValueKind.Null ? null : ev.GetProperty("targetStepStatus").GetInt32();
        if (targetStep.HasValue && !Enum.IsDefined(typeof(WorkflowStepStatus), targetStep.Value))
        {
            throw new InvalidOperationException($"Persistence event targetStepStatus {targetStep.Value} is invalid.");
        }

        ParsedPersistenceEvent parsedEv = new(evWorkflowId, evSeq, evKind, evTaskId, prevWf, targetWf, prevStep, targetStep);

        JsonElement st = root.GetProperty("state");
        int planRev = st.GetProperty("sourceImplementationPlanRevision").GetInt32();
        int status = st.GetProperty("status").GetInt32();
        if (!Enum.IsDefined(typeof(WorkflowStatus), status))
        {
            throw new InvalidOperationException($"Persistence state status {status} is invalid.");
        }

        JsonElement stepsElem = st.GetProperty("steps");
        List<(string TaskId, int Status)> stepsList = [];
        foreach (JsonElement step in stepsElem.EnumerateArray())
        {
            string tId = step.GetProperty("taskId").GetString()!;
            int tStatus = step.GetProperty("status").GetInt32();
            if (!Enum.IsDefined(typeof(WorkflowStepStatus), tStatus))
            {
                throw new InvalidOperationException($"Persistence state step status {tStatus} for task '{tId}' is invalid.");
            }
            stepsList.Add((tId, tStatus));
        }

        ParsedPersistenceState parsedState = new(planRev, status, stepsList.ToArray());
        return new ParsedPersistenceRecord(schemaId, schemaVersion, workflowId, revision, parsedEv, parsedState);
    }

    private static string ComputeP09BaseStateFingerprint(
        ParsedPersistenceState state_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sourceImplementationPlanRevision", state_.SourceImplementationPlanRevision);
            writer.WriteNumber("status", state_.Status);
            writer.WriteStartArray("steps");
            for (int i = 0; i < state_.Steps.Length; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", state_.Steps[i].TaskId);
                writer.WriteNumber("status", state_.Steps[i].Status);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeP10BaseStateFingerprint(
        ParsedPersistenceState state_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sourceImplementationPlanRevision", state_.SourceImplementationPlanRevision);
            writer.WriteNumber("status", state_.Status);
            writer.WriteStartArray("steps");
            foreach ((string taskId, int status) in state_.Steps.OrderBy(s => s.TaskId, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("taskId", taskId);
                writer.WriteNumber("status", status);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ValidateCanonicalRelativePath(
        string relativePath_)
    {
        if (string.IsNullOrWhiteSpace(relativePath_))
        {
            throw new InvalidOperationException("Relative path cannot be empty.");
        }

        if (relativePath_.Contains('\\', StringComparison.Ordinal) ||
            relativePath_.Contains(':', StringComparison.Ordinal) ||
            relativePath_.StartsWith("/", StringComparison.Ordinal) ||
            relativePath_.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Non-canonical or unsafe relative path detected: '{relativePath_}'.");
        }

        if (!relativePath_.StartsWith("workflows/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Relative path must start with 'workflows/': '{relativePath_}'.");
        }
    }

    private static void ValidateSourcePathSecurity(
        string storageRoot_,
        string fullPath_,
        string relativePath_)
    {
        ValidateCanonicalRelativePath(relativePath_);
        ValidatePathRootAndReparse(storageRoot_, fullPath_);
    }

    private static void ValidatePathRootAndReparse(
        string storageRoot_,
        string fullPath_)
    {
        string normalizedRoot =
            Path.GetFullPath(storageRoot_);

        string normalizedPath =
            Path.GetFullPath(fullPath_);

        string rootWithSep =
            normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedRoot
                : normalizedRoot + Path.DirectorySeparatorChar;

        if (!normalizedPath.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Path traversal detected: path '{normalizedPath}' is not rooted within storage root '{normalizedRoot}'.");
        }

        // Validate normalizedRoot itself
        CheckReparsePoint(normalizedRoot);

        // Validate every existing path component from normalizedRoot to normalizedPath
        string relative =
            Path.GetRelativePath(normalizedRoot, normalizedPath);

        if (string.Equals(relative, ".", StringComparison.Ordinal))
        {
            return;
        }

        string current = normalizedRoot;
        string[] segments =
            relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            current = Path.Combine(current, segment);
            CheckReparsePoint(current);
        }
    }

    private static void CheckReparsePoint(
        string path_)
    {
        if (Directory.Exists(path_))
        {
            DirectoryInfo di = new(path_);
            if (di.Attributes.HasFlag(FileAttributes.ReparsePoint) || di.LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Reparse point / symlink detected at directory: '{path_}'.");
            }
        }
        else if (File.Exists(path_))
        {
            FileInfo fi = new(path_);
            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint) || fi.LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Reparse point / symlink detected at file: '{path_}'.");
            }
        }
        else
        {
            try
            {
                FileSystemInfo? target = Directory.ResolveLinkTarget(path_, returnFinalTarget: false);
                if (target is not null)
                {
                    throw new InvalidOperationException(
                        $"Reparse point / symlink detected at broken link: '{path_}'.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
            }
        }
    }

    private static byte[] ReadFileBytesAndValidateNoBom(
        string filePath_)
    {
        byte[] bytes = File.ReadAllBytes(filePath_);

        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            throw new InvalidOperationException(
                $"File must not contain a UTF-8 BOM: '{filePath_}'.");
        }

        return bytes;
    }

    private static bool IsLowerHex(
        string value_)
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

    private static void CleanupOwnedStagingContainer(
        string storageRoot_,
        string stagingContainerDir_,
        string ownerToken_,
        string packId_,
        bool ownerMarkerWritten_ = true,
        bool ownerMarkerCreated_ = false)
    {
        if (!Directory.Exists(stagingContainerDir_))
        {
            return;
        }

        // Never delete if it's a reparse point or escapes root
        ValidatePathRootAndReparse(storageRoot_, stagingContainerDir_);

        string ownerPath = Path.Combine(stagingContainerDir_, ".staging-owner");
        string tempPayloadDir = Path.Combine(stagingContainerDir_, packId_);
        byte[] expectedTokenBytes = Encoding.UTF8.GetBytes(ownerToken_);

        if (!ownerMarkerWritten_)
        {
            // 1. If ownerMarkerWritten_ == false, a payload directory must not yet exist in the normal export lifecycle.
            // If <packId> exists, preserve the entire affected staging container. Do not delete its manifest.json, even if its name is valid.
            if (Directory.Exists(tempPayloadDir) || File.Exists(tempPayloadDir))
            {
                return;
            }
        }

        if (ownerMarkerWritten_)
        {
            if (!File.Exists(ownerPath))
            {
                // Missing owner marker: directory was replaced by another actor; do NOT delete
                return;
            }

            CheckReparsePoint(ownerPath);
            byte[] existingTokenBytes = File.ReadAllBytes(ownerPath);
            if (!existingTokenBytes.AsSpan().SequenceEqual(expectedTokenBytes))
            {
                // Token mismatch: directory was replaced by another actor; do NOT delete
                return;
            }
        }
        else
        {
            // Initialization failed before owner marker write was completed.
            if (File.Exists(ownerPath))
            {
                // 3. A partial .staging-owner should be eligible for cleanup only when creation by the current invocation
                // is independently established, such as successful exclusive FileMode.CreateNew tracked before the injected write/flush failure.
                // Do not infer ownership solely from File.Exists or matching token prefixes.
                if (!ownerMarkerCreated_)
                {
                    // Ownership was not independently established by this invocation (foreign marker): do NOT delete
                    return;
                }

                CheckReparsePoint(ownerPath);
                byte[] existingBytes = File.ReadAllBytes(ownerPath);

                if (existingBytes.Length > expectedTokenBytes.Length)
                {
                    // Longer than expected token: foreign content
                    return;
                }

                // Must be a valid prefix of our owner token
                if (!existingBytes.AsSpan().SequenceEqual(expectedTokenBytes.AsSpan(0, existingBytes.Length)))
                {
                    return;
                }
            }
            else
            {
                // Marker was never created.
                // 4. Preserve successful cleanup of demonstrably owned partial markers and empty staging containers.
                // If the container is not empty (e.g. foreign content or payload directory created without marker),
                // ownership cannot be established.
                if (Directory.EnumerateFileSystemEntries(stagingContainerDir_).Any())
                {
                    return;
                }
            }
        }

        // 2. Do not treat .staging-owner.tmp as invocation-owned merely because its bytes match a prefix of the expected token.
        // This file is not created by the current production write path. Preserve it unless the invocation can prove exclusive ownership.
        // Check entries of stagingContainerDir_: must only contain .staging-owner and optionally the packId payload directory
        string[] containerEntries = Directory.GetFileSystemEntries(stagingContainerDir_);
        foreach (string entry in containerEntries)
        {
            CheckReparsePoint(entry);
            string name = Path.GetFileName(entry);
            if (!string.Equals(name, ".staging-owner", StringComparison.Ordinal) &&
                !string.Equals(name, packId_, StringComparison.Ordinal))
            {
                // Foreign files/directories present (including .staging-owner.tmp): do NOT delete
                return;
            }
        }

        if (Directory.Exists(tempPayloadDir))
        {
            CheckReparsePoint(tempPayloadDir);
            string[] payloadEntries = Directory.GetFileSystemEntries(tempPayloadDir);
            foreach (string entry in payloadEntries)
            {
                CheckReparsePoint(entry);
                string name = Path.GetFileName(entry);
                if (!string.Equals(name, "manifest.json", StringComparison.Ordinal))
                {
                    // Foreign files present: do NOT delete
                    return;
                }
            }

            string tempManifestPath = Path.Combine(tempPayloadDir, "manifest.json");
            if (File.Exists(tempManifestPath))
            {
                CheckReparsePoint(tempManifestPath);
                File.Delete(tempManifestPath);
            }

            Directory.Delete(tempPayloadDir, recursive: false);
        }

        if (File.Exists(ownerPath))
        {
            CheckReparsePoint(ownerPath);
            File.Delete(ownerPath);
        }

        Directory.Delete(stagingContainerDir_, recursive: false);
    }

    private static void ValidateStagingContainerOwnership(
        string storageRoot_,
        string stagingContainerDir_,
        string ownerToken_,
        string packId_,
        byte[] expectedManifestBytes_,
        string expectedManifestFingerprint_)
    {
        ValidatePathRootAndReparse(storageRoot_, stagingContainerDir_);

        string ownerPath = Path.Combine(stagingContainerDir_, ".staging-owner");
        if (!File.Exists(ownerPath))
        {
            throw new InvalidOperationException(
                $"Staging directory ownership token missing: '{ownerPath}'.");
        }

        CheckReparsePoint(ownerPath);
        byte[] existingTokenBytes = File.ReadAllBytes(ownerPath);
        string existingToken = Encoding.UTF8.GetString(existingTokenBytes);
        if (!string.Equals(existingToken, ownerToken_, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Staging directory ownership token mismatch: staging directory was replaced by another actor.");
        }

        string[] entries = Directory.GetFileSystemEntries(stagingContainerDir_);
        if (entries.Length != 2)
        {
            throw new InvalidOperationException(
                $"Staging container must contain exactly two entries, but found {entries.Length}.");
        }

        foreach (string entry in entries)
        {
            CheckReparsePoint(entry);
            string name = Path.GetFileName(entry);
            if (!string.Equals(name, ".staging-owner", StringComparison.Ordinal) &&
                !string.Equals(name, packId_, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Unexpected entry found in staging directory: '{entry}'.");
            }
        }

        string tempPayloadDir = Path.Combine(stagingContainerDir_, packId_);
        if (!Directory.Exists(tempPayloadDir))
        {
            throw new InvalidOperationException(
                $"Staging payload directory missing: '{tempPayloadDir}'.");
        }

        CheckReparsePoint(tempPayloadDir);

        string[] payloadEntries = Directory.GetFileSystemEntries(tempPayloadDir);
        if (payloadEntries.Length != 1)
        {
            throw new InvalidOperationException(
                $"Staging payload directory must contain exactly one entry ('manifest.json'), but found {payloadEntries.Length}.");
        }

        string payloadEntry = payloadEntries[0];
        CheckReparsePoint(payloadEntry);
        string payloadName = Path.GetFileName(payloadEntry);
        if (!string.Equals(payloadName, "manifest.json", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected entry found in staging payload directory: '{payloadEntry}'.");
        }

        string stagedManifestPath = Path.Combine(tempPayloadDir, "manifest.json");
        if (!File.Exists(stagedManifestPath))
        {
            throw new InvalidOperationException(
                $"Staged manifest file missing or is not a regular file: '{stagedManifestPath}'.");
        }

        FileInfo fileInfo = new(stagedManifestPath);
        if (fileInfo.Length == 0)
        {
            throw new InvalidOperationException(
                "Staged manifest file is empty.");
        }

        if (fileInfo.Length != expectedManifestBytes_.Length)
        {
            throw new InvalidOperationException(
                $"Staged manifest file size mismatch: expected {expectedManifestBytes_.Length} bytes, found {fileInfo.Length} bytes.");
        }

        byte[] stagedBytes = ReadFileBytesAndValidateNoBom(stagedManifestPath);
        if (!stagedBytes.AsSpan().SequenceEqual(expectedManifestBytes_))
        {
            throw new InvalidOperationException(
                "Staged manifest bytes do not match expected manifest bytes.");
        }

        string stagedSha = WorkflowEvidenceManifestJson.ComputeSha256LowerHex(stagedBytes);
        if (!string.Equals(stagedSha, expectedManifestFingerprint_, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Staged manifest SHA-256 digest '{stagedSha}' does not match expected '{expectedManifestFingerprint_}'.");
        }
    }

    private static void ValidateCapturedPersistenceHistory(
        IReadOnlyDictionary<long, ParsedPersistenceRecord> capturedSnapshots_,
        long capturedPersistenceRevision_)
    {
        if (!capturedSnapshots_.TryGetValue(1, out ParsedPersistenceRecord? snap1))
        {
            throw new InvalidOperationException(
                "Captured persistence records must contain revision 1.");
        }

        if (snap1.Revision != 1 ||
            snap1.Event.Kind != (int) WorkflowExecutionEventKind.WorkflowInitialized ||
            snap1.State.Status != (int) WorkflowStatus.Created)
        {
            throw new InvalidOperationException(
                "First workflow persistence record must be a Created initialization at revision 1.");
        }

        for (int i = 0; i < snap1.State.Steps.Length; i++)
        {
            if (snap1.State.Steps[i].Status != (int) WorkflowStepStatus.Pending)
            {
                throw new InvalidOperationException(
                    "Initial persisted workflow state must contain only Pending steps.");
            }
        }

        ExecutableTask[] initialTasks =
            new ExecutableTask[snap1.State.Steps.Length];

        for (int i = 0; i < snap1.State.Steps.Length; i++)
        {
            initialTasks[i] = new ExecutableTask(
                snap1.State.Steps[i].TaskId,
                snap1.State.Steps[i].TaskId,
                "workflow-step");
        }

        ExecutableWork work = new(
            snap1.State.SourceImplementationPlanRevision,
            initialTasks);

        WorkflowState reconstructedState;
        try
        {
            reconstructedState = WorkflowStateMachine.Create(work);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Failed to construct initial workflow state under P01 state machine.",
                ex);
        }

        ValidateReconstructedStateMatches(reconstructedState, snap1.State, 1);

        for (long rev = 2; rev <= capturedPersistenceRevision_; rev++)
        {
            if (!capturedSnapshots_.TryGetValue(rev, out ParsedPersistenceRecord? snap))
            {
                throw new InvalidOperationException(
                    $"Missing contiguous captured persistence record at revision {rev}.");
            }

            ParsedPersistenceEvent ev = snap.Event;

            switch ((WorkflowExecutionEventKind) ev.Kind)
            {
                case WorkflowExecutionEventKind.WorkflowStatusTransitioned:
                    if (!ev.PreviousWorkflowStatus.HasValue ||
                        ev.PreviousWorkflowStatus.Value != (int) reconstructedState.Status)
                    {
                        throw new InvalidOperationException(
                            $"Persisted workflow event at revision {rev} previous status does not match prior state.");
                    }

                    if (!ev.TargetWorkflowStatus.HasValue ||
                        !Enum.IsDefined(typeof(WorkflowStatus), (WorkflowStatus) ev.TargetWorkflowStatus.Value))
                    {
                        throw new InvalidOperationException(
                            $"Persisted workflow event at revision {rev} has invalid target workflow status.");
                    }

                    try
                    {
                        reconstructedState = WorkflowStateMachine.TransitionWorkflow(
                            reconstructedState,
                            (WorkflowStatus) ev.TargetWorkflowStatus.Value);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        throw new InvalidOperationException(
                            $"Persisted workflow transition is not legal under the P01 state machine (revision {rev}).",
                            ex);
                    }
                    break;

                case WorkflowExecutionEventKind.StepStatusTransitioned:
                    if (string.IsNullOrWhiteSpace(ev.TaskId))
                    {
                        throw new InvalidOperationException(
                            $"Persisted step event at revision {rev} missing taskId.");
                    }

                    int stepIndex = -1;
                    for (int i = 0; i < reconstructedState.Steps.Count; i++)
                    {
                        if (string.Equals(reconstructedState.Steps[i].TaskId, ev.TaskId, StringComparison.Ordinal))
                        {
                            stepIndex = i;
                            break;
                        }
                    }

                    if (stepIndex < 0)
                    {
                        throw new InvalidOperationException(
                            $"Persisted step event at revision {rev} references an unknown task '{ev.TaskId}'.");
                    }

                    if (!ev.PreviousStepStatus.HasValue ||
                        ev.PreviousStepStatus.Value != (int) reconstructedState.Steps[stepIndex].Status)
                    {
                        throw new InvalidOperationException(
                            $"Persisted step event at revision {rev} previous status does not match prior state.");
                    }

                    if (!ev.TargetStepStatus.HasValue ||
                        !Enum.IsDefined(typeof(WorkflowStepStatus), (WorkflowStepStatus) ev.TargetStepStatus.Value))
                    {
                        throw new InvalidOperationException(
                            $"Persisted step event at revision {rev} has invalid target step status.");
                    }

                    try
                    {
                        reconstructedState = WorkflowStateMachine.TransitionStep(
                            reconstructedState,
                            ev.TaskId,
                            (WorkflowStepStatus) ev.TargetStepStatus.Value);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        throw new InvalidOperationException(
                            $"Persisted step transition is not legal under the P01 state machine (revision {rev}).",
                            ex);
                    }
                    break;

                case WorkflowExecutionEventKind.WorkflowInitialized:
                    throw new InvalidOperationException(
                        $"WorkflowInitialized event may appear only at revision 1, but found at revision {rev}.");

                default:
                    throw new InvalidOperationException(
                        $"Persisted workflow event kind '{ev.Kind}' at revision {rev} is invalid.");
            }

            ValidateReconstructedStateMatches(reconstructedState, snap.State, rev);
        }
    }

    private static void ValidateReconstructedStateMatches(
        WorkflowState reconstructed_,
        ParsedPersistenceState recorded_,
        long revision_)
    {
        if (reconstructed_.SourceImplementationPlanRevision != recorded_.SourceImplementationPlanRevision)
        {
            throw new InvalidOperationException(
                $"Persisted state plan revision mismatch at revision {revision_}.");
        }

        if ((int) reconstructed_.Status != recorded_.Status)
        {
            throw new InvalidOperationException(
                $"Persisted state status mismatch at revision {revision_}: reconstructed '{(int) reconstructed_.Status}', recorded '{recorded_.Status}'.");
        }

        if (reconstructed_.Steps.Count != recorded_.Steps.Length)
        {
            throw new InvalidOperationException(
                $"Persisted state step count mismatch at revision {revision_}: reconstructed '{reconstructed_.Steps.Count}', recorded '{recorded_.Steps.Length}'.");
        }

        for (int i = 0; i < reconstructed_.Steps.Count; i++)
        {
            if (!string.Equals(reconstructed_.Steps[i].TaskId, recorded_.Steps[i].TaskId, StringComparison.Ordinal) ||
                (int) reconstructed_.Steps[i].Status != recorded_.Steps[i].Status)
            {
                throw new InvalidOperationException(
                    $"Persisted state step mismatch at index {i} for revision {revision_}.");
            }
        }
    }

    private sealed record ParsedPersistenceRecord(
        string SchemaId,
        int SchemaVersion,
        string WorkflowId,
        long Revision,
        ParsedPersistenceEvent Event,
        ParsedPersistenceState State);

    private sealed record ParsedPersistenceEvent(
        string WorkflowId,
        long Sequence,
        int Kind,
        string? TaskId,
        int? PreviousWorkflowStatus,
        int? TargetWorkflowStatus,
        int? PreviousStepStatus,
        int? TargetStepStatus);

    private sealed record ParsedPersistenceState(
        int SourceImplementationPlanRevision,
        int Status,
        (string TaskId, int Status)[] Steps);
}
