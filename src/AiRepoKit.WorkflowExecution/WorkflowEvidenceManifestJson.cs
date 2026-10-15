namespace AiRepoKit.WorkflowExecution;

using System.Security.Cryptography;
using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Orchestration;

internal static class WorkflowEvidenceManifestJson
{
    private static readonly string[] RootPropertyOrder =
    [
        "schemaId",
        "schemaVersion",
        "algorithmId",
        "packId",
        "workflowId",
        "sourceImplementationPlanRevision",
        "capturedPersistenceRevision",
        "workflowStatus",
        "sourceSetFingerprint",
        "sourceRecords",
        "events",
        "tasks"
    ];

    private static readonly string[] SourceRecordPropertyOrder =
    [
        "relativePath",
        "byteLength",
        "sha256Fingerprint"
    ];

    private static readonly string[] EventPropertyOrder =
    [
        "workflowId",
        "revision",
        "kind",
        "taskId",
        "previousWorkflowStatus",
        "targetWorkflowStatus",
        "previousStepStatus",
        "targetStepStatus"
    ];

    private static readonly string[] TaskPropertyOrder =
    [
        "taskId",
        "stepStatus",
        "checkpointJournals",
        "gateJournals"
    ];

    private static readonly string[] CheckpointJournalPropertyOrder =
    [
        "basePersistenceRevision",
        "baseWorkflowStateFingerprint",
        "inputFingerprint",
        "registryFingerprint",
        "executionCompleted",
        "finalStateFingerprint",
        "attemptCount",
        "budgetExhausted",
        "expectedProjectionTransitionCount",
        "projectionCompleted",
        "finalPersistenceRevision",
        "records"
    ];

    private static readonly string[] JournalRecordPropertyOrder =
    [
        "journalSequence",
        "recordKind",
        "operationOrdinal",
        "operationId",
        "operationKind",
        "invocationGeneration",
        "requestFingerprint",
        "providerId",
        "validationExternalIdentityDigest",
        "isNullReturn",
        "agentStatus",
        "agentOutputDigest",
        "agentDiagnosticDigest",
        "validationPassed",
        "validationEvidenceDigest",
        "finalStateFingerprint",
        "attemptCount",
        "budgetExhausted",
        "expectedProjectionTransitionCount",
        "finalPersistenceRevision"
    ];

    private static readonly string[] GateJournalPropertyOrder =
    [
        "basePersistenceRevision",
        "authorized",
        "principalId",
        "mechanismId",
        "evidenceFingerprint",
        "challenge"
    ];

    private static readonly string[] ChallengePropertyOrder =
    [
        "gateId",
        "workflowId",
        "taskId",
        "basePersistenceRevision",
        "sourceImplementationPlanRevision",
        "requiredPermission",
        "gateOrdinal",
        "baseStateFingerprint",
        "inputFingerprint",
        "registryFingerprint",
        "policyFingerprint"
    ];

    public static byte[] Serialize(
        WorkflowEvidenceManifest manifest_)
    {
        ArgumentNullException.ThrowIfNull(
            manifest_,
            nameof(manifest_));

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            // P11_MANIFEST_PROPERTY_ORDER: schemaId,schemaVersion,algorithmId,packId,workflowId,sourceImplementationPlanRevision,capturedPersistenceRevision,workflowStatus,sourceSetFingerprint,sourceRecords,events,tasks
            writer.WriteString("schemaId", manifest_.SchemaId);
            writer.WriteNumber("schemaVersion", manifest_.SchemaVersion);
            writer.WriteString("algorithmId", manifest_.AlgorithmId);
            writer.WriteString("packId", manifest_.PackId);
            writer.WriteString("workflowId", manifest_.WorkflowId.Value);
            writer.WriteNumber("sourceImplementationPlanRevision", manifest_.SourceImplementationPlanRevision);
            writer.WriteNumber("capturedPersistenceRevision", manifest_.CapturedPersistenceRevision);
            writer.WriteString("workflowStatus", FormatWorkflowStatus(manifest_.WorkflowStatus));
            writer.WriteString("sourceSetFingerprint", manifest_.SourceSetFingerprint);

            // sourceRecords
            writer.WriteStartArray("sourceRecords");
            foreach (WorkflowEvidenceSourceRecord sourceRecord in manifest_.SourceRecords)
            {
                writer.WriteStartObject();
                // P11_SOURCE_RECORD_PROPERTY_ORDER: relativePath,byteLength,sha256Fingerprint
                writer.WriteString("relativePath", sourceRecord.RelativePath);
                writer.WriteNumber("byteLength", sourceRecord.ByteLength);
                writer.WriteString("sha256Fingerprint", sourceRecord.Sha256Fingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // events
            writer.WriteStartArray("events");
            foreach (WorkflowEvidenceEvent evt in manifest_.Events)
            {
                writer.WriteStartObject();
                // P11_EVENT_PROPERTY_ORDER: workflowId,revision,kind,taskId,previousWorkflowStatus,targetWorkflowStatus,previousStepStatus,targetStepStatus
                writer.WriteString("workflowId", evt.WorkflowId.Value);
                writer.WriteNumber("revision", evt.Revision);
                writer.WriteString("kind", FormatEventKind(evt.Kind));
                WriteNullableString(writer, "taskId", evt.TaskId);
                WriteNullableString(writer, "previousWorkflowStatus", evt.PreviousWorkflowStatus.HasValue ? FormatWorkflowStatus(evt.PreviousWorkflowStatus.Value) : null);
                WriteNullableString(writer, "targetWorkflowStatus", evt.TargetWorkflowStatus.HasValue ? FormatWorkflowStatus(evt.TargetWorkflowStatus.Value) : null);
                WriteNullableString(writer, "previousStepStatus", evt.PreviousStepStatus.HasValue ? FormatStepStatus(evt.PreviousStepStatus.Value) : null);
                WriteNullableString(writer, "targetStepStatus", evt.TargetStepStatus.HasValue ? FormatStepStatus(evt.TargetStepStatus.Value) : null);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // tasks
            writer.WriteStartArray("tasks");
            foreach (WorkflowTaskEvidence task in manifest_.Tasks)
            {
                writer.WriteStartObject();
                // P11_TASK_PROPERTY_ORDER: taskId,stepStatus,checkpointJournals,gateJournals
                writer.WriteString("taskId", task.TaskId);
                writer.WriteString("stepStatus", FormatStepStatus(task.StepStatus));

                // checkpointJournals
                writer.WriteStartArray("checkpointJournals");
                foreach (WorkflowCheckpointJournalEvidence cp in task.CheckpointJournals)
                {
                    writer.WriteStartObject();
                    // P11_CHECKPOINT_JOURNAL_PROPERTY_ORDER: basePersistenceRevision,baseWorkflowStateFingerprint,inputFingerprint,registryFingerprint,executionCompleted,finalStateFingerprint,attemptCount,budgetExhausted,expectedProjectionTransitionCount,projectionCompleted,finalPersistenceRevision,records
                    writer.WriteNumber("basePersistenceRevision", cp.BasePersistenceRevision);
                    writer.WriteString("baseWorkflowStateFingerprint", cp.BaseWorkflowStateFingerprint);
                    writer.WriteString("inputFingerprint", cp.InputFingerprint);
                    writer.WriteString("registryFingerprint", cp.RegistryFingerprint);
                    writer.WriteBoolean("executionCompleted", cp.ExecutionCompleted);
                    WriteNullableString(writer, "finalStateFingerprint", cp.FinalStateFingerprint);
                    WriteNullableNumber(writer, "attemptCount", cp.AttemptCount);
                    WriteNullableBoolean(writer, "budgetExhausted", cp.BudgetExhausted);
                    WriteNullableNumber(writer, "expectedProjectionTransitionCount", cp.ExpectedProjectionTransitionCount);
                    writer.WriteBoolean("projectionCompleted", cp.ProjectionCompleted);
                    WriteNullableNumber(writer, "finalPersistenceRevision", cp.FinalPersistenceRevision);

                    writer.WriteStartArray("records");
                    foreach (WorkflowJournalRecordEvidence rec in cp.Records)
                    {
                        writer.WriteStartObject();
                        // P11_JOURNAL_RECORD_PROPERTY_ORDER: journalSequence,recordKind,operationOrdinal,operationId,operationKind,invocationGeneration,requestFingerprint,providerId,validationExternalIdentityDigest,isNullReturn,agentStatus,agentOutputDigest,agentDiagnosticDigest,validationPassed,validationEvidenceDigest,finalStateFingerprint,attemptCount,budgetExhausted,expectedProjectionTransitionCount,finalPersistenceRevision
                        writer.WriteNumber("journalSequence", rec.JournalSequence);
                        writer.WriteString("recordKind", rec.RecordKind);
                        WriteNullableNumber(writer, "operationOrdinal", rec.OperationOrdinal);
                        WriteNullableString(writer, "operationId", rec.OperationId);
                        WriteNullableString(writer, "operationKind", rec.OperationKind);
                        WriteNullableNumber(writer, "invocationGeneration", rec.InvocationGeneration);
                        WriteNullableString(writer, "requestFingerprint", rec.RequestFingerprint);
                        WriteNullableString(writer, "providerId", rec.ProviderId);
                        WriteNullableString(writer, "validationExternalIdentityDigest", rec.ValidationExternalIdentityDigest);
                        WriteNullableBoolean(writer, "isNullReturn", rec.IsNullReturn);
                        WriteNullableNumber(writer, "agentStatus", rec.AgentStatus);
                        WriteNullableString(writer, "agentOutputDigest", rec.AgentOutputDigest);
                        WriteNullableString(writer, "agentDiagnosticDigest", rec.AgentDiagnosticDigest);
                        WriteNullableBoolean(writer, "validationPassed", rec.ValidationPassed);
                        WriteNullableString(writer, "validationEvidenceDigest", rec.ValidationEvidenceDigest);
                        WriteNullableString(writer, "finalStateFingerprint", rec.FinalStateFingerprint);
                        WriteNullableNumber(writer, "attemptCount", rec.AttemptCount);
                        WriteNullableBoolean(writer, "budgetExhausted", rec.BudgetExhausted);
                        WriteNullableNumber(writer, "expectedProjectionTransitionCount", rec.ExpectedProjectionTransitionCount);
                        WriteNullableNumber(writer, "finalPersistenceRevision", rec.FinalPersistenceRevision);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();

                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                // gateJournals
                writer.WriteStartArray("gateJournals");
                foreach (WorkflowGateJournalEvidence gj in task.GateJournals)
                {
                    writer.WriteStartObject();
                    // P11_GATE_JOURNAL_PROPERTY_ORDER: basePersistenceRevision,authorized,principalId,mechanismId,evidenceFingerprint,challenge
                    writer.WriteNumber("basePersistenceRevision", gj.BasePersistenceRevision);
                    WriteNullableBoolean(writer, "authorized", gj.Authorized);
                    WriteNullableString(writer, "principalId", gj.PrincipalId);
                    WriteNullableString(writer, "mechanismId", gj.MechanismId);
                    WriteNullableString(writer, "evidenceFingerprint", gj.EvidenceFingerprint);

                    // challenge
                    // P11_GATE_CHALLENGE_PROPERTY_ORDER: gateId,workflowId,taskId,basePersistenceRevision,sourceImplementationPlanRevision,requiredPermission,gateOrdinal,baseStateFingerprint,inputFingerprint,registryFingerprint,policyFingerprint
                    writer.WriteStartObject("challenge");
                    writer.WriteString("gateId", gj.Challenge.GateId);
                    writer.WriteString("workflowId", gj.Challenge.WorkflowId.Value);
                    writer.WriteString("taskId", gj.Challenge.TaskId);
                    writer.WriteNumber("basePersistenceRevision", gj.Challenge.BasePersistenceRevision);
                    writer.WriteNumber("sourceImplementationPlanRevision", gj.Challenge.SourceImplementationPlanRevision);
                    writer.WriteString("requiredPermission", FormatExecutionPermission(gj.Challenge.RequiredPermission));
                    writer.WriteNumber("gateOrdinal", gj.Challenge.GateOrdinal);
                    writer.WriteString("baseStateFingerprint", gj.Challenge.BaseStateFingerprint);
                    writer.WriteString("inputFingerprint", gj.Challenge.InputFingerprint);
                    writer.WriteString("registryFingerprint", gj.Challenge.RegistryFingerprint);
                    writer.WriteString("policyFingerprint", gj.Challenge.PolicyFingerprint);
                    writer.WriteEndObject();

                    writer.WriteEndObject();
                }
                writer.WriteEndArray();

                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static WorkflowEvidenceManifest Deserialize(
        byte[] payload_,
        WorkflowId expectedWorkflowId_)
    {
        ArgumentNullException.ThrowIfNull(
            payload_,
            nameof(payload_));

        if (payload_.Length >= 3 &&
            payload_[0] == 0xEF &&
            payload_[1] == 0xBB &&
            payload_[2] == 0xBF)
        {
            throw new InvalidOperationException(
                "Evidence pack manifest must not contain a UTF-8 BOM.");
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload_);
            JsonElement root = doc.RootElement;

            ValidateObjectPropertyOrder(root, RootPropertyOrder);

            string schemaId = GetRequiredString(root, "schemaId");
            if (!string.Equals(schemaId, WorkflowEvidencePackExporter.ManifestSchemaId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported manifest schema ID: '{schemaId}'.");
            }

            int schemaVersion = GetRequiredInt32(root, "schemaVersion");
            if (schemaVersion != WorkflowEvidencePackExporter.CurrentSchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported manifest schema version: {schemaVersion}.");
            }

            string algorithmId = GetRequiredString(root, "algorithmId");
            if (!string.Equals(algorithmId, WorkflowEvidencePackExporter.AlgorithmId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported manifest algorithm ID: '{algorithmId}'.");
            }

            string packId = GetRequiredString(root, "packId");
            string workflowIdString = GetRequiredString(root, "workflowId");
            WorkflowId workflowId = new(workflowIdString);
            if (!workflowId.Equals(expectedWorkflowId_))
            {
                throw new InvalidOperationException($"Manifest workflowId '{workflowIdString}' does not match expected '{expectedWorkflowId_.Value}'.");
            }

            int sourcePlanRev = GetRequiredInt32(root, "sourceImplementationPlanRevision");
            long capturedRev = GetRequiredInt64(root, "capturedPersistenceRevision");
            string workflowStatusStr = GetRequiredString(root, "workflowStatus");
            WorkflowStatus workflowStatus = ParseWorkflowStatus(workflowStatusStr);
            string sourceSetFingerprint = GetRequiredString(root, "sourceSetFingerprint");

            // sourceRecords
            List<WorkflowEvidenceSourceRecord> sourceRecords = [];
            if (root.TryGetProperty("sourceRecords", out JsonElement sourceRecordsElem) &&
                sourceRecordsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement sr in sourceRecordsElem.EnumerateArray())
                {
                    ValidateObjectPropertyOrder(sr, SourceRecordPropertyOrder);
                    string relativePath = GetRequiredString(sr, "relativePath");
                    long byteLength = GetRequiredInt64(sr, "byteLength");
                    string sha256 = GetRequiredString(sr, "sha256Fingerprint");
                    sourceRecords.Add(new WorkflowEvidenceSourceRecord(relativePath, byteLength, sha256));
                }
            }
            else
            {
                throw new InvalidOperationException("Missing or invalid sourceRecords in manifest.");
            }

            // events
            List<WorkflowEvidenceEvent> events = [];
            if (root.TryGetProperty("events", out JsonElement eventsElem) &&
                eventsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement ev in eventsElem.EnumerateArray())
                {
                    ValidateObjectPropertyOrder(ev, EventPropertyOrder);
                    WorkflowId evWorkflowId = new(GetRequiredString(ev, "workflowId"));
                    long rev = GetRequiredInt64(ev, "revision");
                    WorkflowExecutionEventKind kind = ParseEventKind(GetRequiredString(ev, "kind"));
                    string? taskId = GetOptionalString(ev, "taskId");
                    WorkflowStatus? prevWfStatus = ParseOptionalWorkflowStatus(GetOptionalString(ev, "previousWorkflowStatus"));
                    WorkflowStatus? targetWfStatus = ParseOptionalWorkflowStatus(GetOptionalString(ev, "targetWorkflowStatus"));
                    WorkflowStepStatus? prevStepStatus = ParseOptionalStepStatus(GetOptionalString(ev, "previousStepStatus"));
                    WorkflowStepStatus? targetStepStatus = ParseOptionalStepStatus(GetOptionalString(ev, "targetStepStatus"));

                    events.Add(new WorkflowEvidenceEvent(
                        evWorkflowId,
                        rev,
                        kind,
                        taskId,
                        prevWfStatus,
                        targetWfStatus,
                        prevStepStatus,
                        targetStepStatus));
                }
            }
            else
            {
                throw new InvalidOperationException("Missing or invalid events in manifest.");
            }

            // tasks
            List<WorkflowTaskEvidence> tasks = [];
            if (root.TryGetProperty("tasks", out JsonElement tasksElem) &&
                tasksElem.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement tElem in tasksElem.EnumerateArray())
                {
                    ValidateObjectPropertyOrder(tElem, TaskPropertyOrder);
                    string taskId = GetRequiredString(tElem, "taskId");
                    WorkflowStepStatus stepStatus = ParseStepStatus(GetRequiredString(tElem, "stepStatus"));

                    List<WorkflowCheckpointJournalEvidence> checkpointJournals = [];
                    if (tElem.TryGetProperty("checkpointJournals", out JsonElement cpJournalsElem) &&
                        cpJournalsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement cpElem in cpJournalsElem.EnumerateArray())
                        {
                            ValidateObjectPropertyOrder(cpElem, CheckpointJournalPropertyOrder);
                            long baseRev = GetRequiredInt64(cpElem, "basePersistenceRevision");
                            string baseWfStateFp = GetRequiredString(cpElem, "baseWorkflowStateFingerprint");
                            string inputFp = GetRequiredString(cpElem, "inputFingerprint");
                            string registryFp = GetRequiredString(cpElem, "registryFingerprint");
                            bool execCompleted = GetRequiredBoolean(cpElem, "executionCompleted");
                            string? finalStateFp = GetOptionalString(cpElem, "finalStateFingerprint");
                            int? attemptCount = GetOptionalInt32(cpElem, "attemptCount");
                            bool? budgetExhausted = GetOptionalBoolean(cpElem, "budgetExhausted");
                            int? expectedProjTransCount = GetOptionalInt32(cpElem, "expectedProjectionTransitionCount");
                            bool projCompleted = GetRequiredBoolean(cpElem, "projectionCompleted");
                            long? finalPersistRev = GetOptionalInt64(cpElem, "finalPersistenceRevision");

                            List<WorkflowJournalRecordEvidence> recs = [];
                            if (cpElem.TryGetProperty("records", out JsonElement recsElem) &&
                                recsElem.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement r in recsElem.EnumerateArray())
                                {
                                    ValidateObjectPropertyOrder(r, JournalRecordPropertyOrder);
                                    recs.Add(new WorkflowJournalRecordEvidence(
                                        GetRequiredInt64(r, "journalSequence"),
                                        GetRequiredString(r, "recordKind"),
                                        GetOptionalInt64(r, "operationOrdinal"),
                                        GetOptionalString(r, "operationId"),
                                        GetOptionalString(r, "operationKind"),
                                        GetOptionalInt32(r, "invocationGeneration"),
                                        GetOptionalString(r, "requestFingerprint"),
                                        GetOptionalString(r, "providerId"),
                                        GetOptionalString(r, "validationExternalIdentityDigest"),
                                        GetOptionalBoolean(r, "isNullReturn"),
                                        GetOptionalInt32(r, "agentStatus"),
                                        GetOptionalString(r, "agentOutputDigest"),
                                        GetOptionalString(r, "agentDiagnosticDigest"),
                                        GetOptionalBoolean(r, "validationPassed"),
                                        GetOptionalString(r, "validationEvidenceDigest"),
                                        GetOptionalString(r, "finalStateFingerprint"),
                                        GetOptionalInt32(r, "attemptCount"),
                                        GetOptionalBoolean(r, "budgetExhausted"),
                                        GetOptionalInt32(r, "expectedProjectionTransitionCount"),
                                        GetOptionalInt64(r, "finalPersistenceRevision")));
                                }
                            }

                            checkpointJournals.Add(new WorkflowCheckpointJournalEvidence(
                                baseRev,
                                baseWfStateFp,
                                inputFp,
                                registryFp,
                                execCompleted,
                                finalStateFp,
                                attemptCount,
                                budgetExhausted,
                                expectedProjTransCount,
                                projCompleted,
                                finalPersistRev,
                                recs));
                        }
                    }

                    List<WorkflowGateJournalEvidence> gateJournals = [];
                    if (tElem.TryGetProperty("gateJournals", out JsonElement gateJournalsElem) &&
                        gateJournalsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement gjElem in gateJournalsElem.EnumerateArray())
                        {
                            ValidateObjectPropertyOrder(gjElem, GateJournalPropertyOrder);
                            long baseRev = GetRequiredInt64(gjElem, "basePersistenceRevision");
                            bool? authorized = GetOptionalBoolean(gjElem, "authorized");
                            string? principalId = GetOptionalString(gjElem, "principalId");
                            string? mechanismId = GetOptionalString(gjElem, "mechanismId");
                            string? evidenceFingerprint = GetOptionalString(gjElem, "evidenceFingerprint");

                            if (!gjElem.TryGetProperty("challenge", out JsonElement chElem) ||
                                chElem.ValueKind != JsonValueKind.Object)
                            {
                                throw new InvalidOperationException("Missing or invalid challenge in gate journal evidence.");
                            }

                            ValidateObjectPropertyOrder(chElem, ChallengePropertyOrder);

                            string gateId = GetRequiredString(chElem, "gateId");
                            WorkflowId chWorkflowId = new(GetRequiredString(chElem, "workflowId"));
                            string chTaskId = GetRequiredString(chElem, "taskId");
                            long chBaseRev = GetRequiredInt64(chElem, "basePersistenceRevision");
                            int chSourcePlanRev = GetRequiredInt32(chElem, "sourceImplementationPlanRevision");
                            ExecutionPermission reqPerm = ParseExecutionPermission(GetRequiredString(chElem, "requiredPermission"));
                            long gateOrdinal = GetRequiredInt64(chElem, "gateOrdinal");
                            string chBaseStateFp = GetRequiredString(chElem, "baseStateFingerprint");
                            string chInputFp = GetRequiredString(chElem, "inputFingerprint");
                            string chRegistryFp = GetRequiredString(chElem, "registryFingerprint");
                            string chPolicyFp = GetRequiredString(chElem, "policyFingerprint");

                            WorkflowGateChallenge challenge = new(
                                gateId,
                                chWorkflowId,
                                chTaskId,
                                chBaseRev,
                                chSourcePlanRev,
                                reqPerm,
                                gateOrdinal,
                                chBaseStateFp,
                                chInputFp,
                                chRegistryFp,
                                chPolicyFp);

                            gateJournals.Add(new WorkflowGateJournalEvidence(
                                baseRev,
                                challenge,
                                authorized,
                                principalId,
                                mechanismId,
                                evidenceFingerprint));
                        }
                    }

                    tasks.Add(new WorkflowTaskEvidence(
                        taskId,
                        stepStatus,
                        checkpointJournals,
                        gateJournals));
                }
            }
            else
            {
                throw new InvalidOperationException("Missing or invalid tasks in manifest.");
            }

            return new WorkflowEvidenceManifest(
                schemaId,
                schemaVersion,
                algorithmId,
                packId,
                workflowId,
                sourcePlanRev,
                capturedRev,
                workflowStatus,
                sourceSetFingerprint,
                sourceRecords,
                events,
                tasks);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Corrupted or invalid evidence manifest JSON.", ex);
        }
    }

    private static void ValidateObjectPropertyOrder(
        JsonElement element_,
        string[] expectedOrder_)
    {
        if (element_.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Expected JSON object.");
        }

        int index = 0;
        foreach (JsonProperty prop in element_.EnumerateObject())
        {
            if (index >= expectedOrder_.Length)
            {
                throw new InvalidOperationException(
                    $"Unexpected property '{prop.Name}' in canonical JSON object.");
            }

            if (!string.Equals(prop.Name, expectedOrder_[index], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Property '{prop.Name}' at position {index} does not match expected canonical property '{expectedOrder_[index]}'.");
            }

            index++;
        }

        if (index != expectedOrder_.Length)
        {
            throw new InvalidOperationException(
                $"Expected {expectedOrder_.Length} canonical properties, found {index}.");
        }
    }

    public static string ComputeSourceSetFingerprint(
        IReadOnlyList<WorkflowEvidenceSourceRecord> sourceRecords_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartArray();
            for (int i = 0; i < sourceRecords_.Count; i++)
            {
                writer.WriteStartObject();
                writer.WriteString("relativePath", sourceRecords_[i].RelativePath);
                writer.WriteNumber("byteLength", sourceRecords_[i].ByteLength);
                writer.WriteString("sha256Fingerprint", sourceRecords_[i].Sha256Fingerprint);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputePackId(
        string algorithmId_,
        string schemaId_,
        int schemaVersion_,
        string workflowId_,
        int sourceImplementationPlanRevision_,
        long capturedPersistenceRevision_,
        string sourceSetFingerprint_)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("algorithmId", algorithmId_);
            writer.WriteString("schemaId", schemaId_);
            writer.WriteNumber("schemaVersion", schemaVersion_);
            writer.WriteString("workflowId", workflowId_);
            writer.WriteNumber("sourceImplementationPlanRevision", sourceImplementationPlanRevision_);
            writer.WriteNumber("capturedPersistenceRevision", capturedPersistenceRevision_);
            writer.WriteString("sourceSetFingerprint", sourceSetFingerprint_);
            writer.WriteEndObject();
        }

        byte[] hash = SHA256.HashData(stream.ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSha256LowerHex(
        byte[] bytes_)
    {
        byte[] hash = SHA256.HashData(bytes_);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer_,
        string propertyName_,
        string? value_)
    {
        if (value_ is null)
        {
            writer_.WriteNull(propertyName_);
        }
        else
        {
            writer_.WriteString(propertyName_, value_);
        }
    }

    private static void WriteNullableNumber(
        Utf8JsonWriter writer_,
        string propertyName_,
        long? value_)
    {
        if (value_.HasValue)
        {
            writer_.WriteNumber(propertyName_, value_.Value);
        }
        else
        {
            writer_.WriteNull(propertyName_);
        }
    }

    private static void WriteNullableNumber(
        Utf8JsonWriter writer_,
        string propertyName_,
        int? value_)
    {
        if (value_.HasValue)
        {
            writer_.WriteNumber(propertyName_, value_.Value);
        }
        else
        {
            writer_.WriteNull(propertyName_);
        }
    }

    private static void WriteNullableBoolean(
        Utf8JsonWriter writer_,
        string propertyName_,
        bool? value_)
    {
        if (value_.HasValue)
        {
            writer_.WriteBoolean(propertyName_, value_.Value);
        }
        else
        {
            writer_.WriteNull(propertyName_);
        }
    }

    private static string FormatWorkflowStatus(
        WorkflowStatus status_)
    {
        return status_ switch
        {
            WorkflowStatus.Created => nameof(WorkflowStatus.Created),
            WorkflowStatus.Running => nameof(WorkflowStatus.Running),
            WorkflowStatus.Completed => nameof(WorkflowStatus.Completed),
            WorkflowStatus.Failed => nameof(WorkflowStatus.Failed),
            WorkflowStatus.Cancelled => nameof(WorkflowStatus.Cancelled),
            _ => throw new InvalidOperationException($"Unsupported workflow status: '{status_}'.")
        };
    }

    private static WorkflowStatus ParseWorkflowStatus(
        string value_)
    {
        return value_ switch
        {
            nameof(WorkflowStatus.Created) => WorkflowStatus.Created,
            nameof(WorkflowStatus.Running) => WorkflowStatus.Running,
            nameof(WorkflowStatus.Completed) => WorkflowStatus.Completed,
            nameof(WorkflowStatus.Failed) => WorkflowStatus.Failed,
            nameof(WorkflowStatus.Cancelled) => WorkflowStatus.Cancelled,
            _ => throw new InvalidOperationException($"Unknown workflow status: '{value_}'.")
        };
    }

    private static WorkflowStatus? ParseOptionalWorkflowStatus(
        string? value_)
    {
        if (value_ is null)
        {
            return null;
        }

        return ParseWorkflowStatus(value_);
    }

    private static string FormatStepStatus(
        WorkflowStepStatus status_)
    {
        return status_ switch
        {
            WorkflowStepStatus.Pending => nameof(WorkflowStepStatus.Pending),
            WorkflowStepStatus.Running => nameof(WorkflowStepStatus.Running),
            WorkflowStepStatus.AwaitingValidation => nameof(WorkflowStepStatus.AwaitingValidation),
            WorkflowStepStatus.Blocked => nameof(WorkflowStepStatus.Blocked),
            WorkflowStepStatus.Failed => nameof(WorkflowStepStatus.Failed),
            WorkflowStepStatus.Completed => nameof(WorkflowStepStatus.Completed),
            WorkflowStepStatus.Cancelled => nameof(WorkflowStepStatus.Cancelled),
            _ => throw new InvalidOperationException($"Unsupported step status: '{status_}'.")
        };
    }

    private static WorkflowStepStatus ParseStepStatus(
        string value_)
    {
        return value_ switch
        {
            nameof(WorkflowStepStatus.Pending) => WorkflowStepStatus.Pending,
            nameof(WorkflowStepStatus.Running) => WorkflowStepStatus.Running,
            nameof(WorkflowStepStatus.AwaitingValidation) => WorkflowStepStatus.AwaitingValidation,
            nameof(WorkflowStepStatus.Blocked) => WorkflowStepStatus.Blocked,
            nameof(WorkflowStepStatus.Failed) => WorkflowStepStatus.Failed,
            nameof(WorkflowStepStatus.Completed) => WorkflowStepStatus.Completed,
            nameof(WorkflowStepStatus.Cancelled) => WorkflowStepStatus.Cancelled,
            _ => throw new InvalidOperationException($"Unknown step status: '{value_}'.")
        };
    }

    private static WorkflowStepStatus? ParseOptionalStepStatus(
        string? value_)
    {
        if (value_ is null)
        {
            return null;
        }

        return ParseStepStatus(value_);
    }

    private static string FormatEventKind(
        WorkflowExecutionEventKind kind_)
    {
        return kind_ switch
        {
            WorkflowExecutionEventKind.WorkflowInitialized => nameof(WorkflowExecutionEventKind.WorkflowInitialized),
            WorkflowExecutionEventKind.WorkflowStatusTransitioned => nameof(WorkflowExecutionEventKind.WorkflowStatusTransitioned),
            WorkflowExecutionEventKind.StepStatusTransitioned => nameof(WorkflowExecutionEventKind.StepStatusTransitioned),
            _ => throw new InvalidOperationException($"Unsupported event kind: '{kind_}'.")
        };
    }

    private static WorkflowExecutionEventKind ParseEventKind(
        string value_)
    {
        return value_ switch
        {
            nameof(WorkflowExecutionEventKind.WorkflowInitialized) => WorkflowExecutionEventKind.WorkflowInitialized,
            nameof(WorkflowExecutionEventKind.WorkflowStatusTransitioned) => WorkflowExecutionEventKind.WorkflowStatusTransitioned,
            nameof(WorkflowExecutionEventKind.StepStatusTransitioned) => WorkflowExecutionEventKind.StepStatusTransitioned,
            _ => throw new InvalidOperationException($"Unknown event kind: '{value_}'.")
        };
    }

    private static string FormatExecutionPermission(
        ExecutionPermission permission_)
    {
        return permission_ switch
        {
            ExecutionPermission.ReadOnly => nameof(ExecutionPermission.ReadOnly),
            ExecutionPermission.WorkspaceWrite => nameof(ExecutionPermission.WorkspaceWrite),
            ExecutionPermission.Unrestricted => nameof(ExecutionPermission.Unrestricted),
            _ => throw new InvalidOperationException($"Unsupported execution permission: '{permission_}'.")
        };
    }

    private static ExecutionPermission ParseExecutionPermission(
        string value_)
    {
        return value_ switch
        {
            nameof(ExecutionPermission.ReadOnly) => ExecutionPermission.ReadOnly,
            nameof(ExecutionPermission.WorkspaceWrite) => ExecutionPermission.WorkspaceWrite,
            nameof(ExecutionPermission.Unrestricted) => ExecutionPermission.Unrestricted,
            _ => throw new InvalidOperationException($"Unknown execution permission: '{value_}'.")
        };
    }

    private static string GetRequiredString(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString()!;
        }

        throw new InvalidOperationException($"Missing required string property: '{propertyName_}'.");
    }

    private static string? GetOptionalString(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }

            throw new InvalidOperationException($"Property '{propertyName_}' is neither null nor string.");
        }

        return null;
    }

    private static int GetRequiredInt32(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt32(out int value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required int property: '{propertyName_}'.");
    }

    private static int? GetOptionalInt32(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int value))
            {
                return value;
            }

            throw new InvalidOperationException($"Property '{propertyName_}' is neither null nor int.");
        }

        return null;
    }

    private static long GetRequiredInt64(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt64(out long value))
        {
            return value;
        }

        throw new InvalidOperationException($"Missing required long property: '{propertyName_}'.");
    }

    private static long? GetOptionalInt64(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out long value))
            {
                return value;
            }

            throw new InvalidOperationException($"Property '{propertyName_}' is neither null nor long.");
        }

        return null;
    }

    private static bool GetRequiredBoolean(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop) &&
            (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
        {
            return prop.GetBoolean();
        }

        throw new InvalidOperationException($"Missing required boolean property: '{propertyName_}'.");
    }

    private static bool? GetOptionalBoolean(
        JsonElement element_,
        string propertyName_)
    {
        if (element_.TryGetProperty(propertyName_, out JsonElement prop))
        {
            if (prop.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
            {
                return prop.GetBoolean();
            }

            throw new InvalidOperationException($"Property '{propertyName_}' is neither null nor boolean.");
        }

        return null;
    }
}
